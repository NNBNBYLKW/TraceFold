# TraceFold UI acceptance toolkit - workflow helper tests.
#
# These lock the interaction rules a scenario depends on: a dynamic element is addressed through a
# selector observed in the current tree, a composite control through its own descendant, and a UI
# mutation is dispatched once. The last rule is the one worth the most care, because resending a
# dispatched action is silent, unrecoverable corruption - so the two outcomes are separated here:
# a lookup-stage miss may be re-resolved, and every other failure must come straight back.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-WorkflowNode {
    param(
        [string] $AutomationId = '',
        [Parameter(Mandatory)][string] $Type,
        [string] $Name = '',
        [string] $ClassName = '',
        [string] $GradientSelector = '',
        [bool] $Offscreen = $false,
        [object[]] $Children = @()
    )

    $node = [ordered]@{
        type        = $Type
        isEnabled   = $true
        isOffscreen = $Offscreen
        x           = 0
        y           = 0
        width       = 100
        height      = 20
        selector    = $GradientSelector
    }
    if (-not [string]::IsNullOrEmpty($AutomationId)) { $node['automationId'] = $AutomationId }
    if (-not [string]::IsNullOrEmpty($Name)) { $node['name'] = $Name }
    if (-not [string]::IsNullOrEmpty($ClassName)) { $node['className'] = $ClassName }
    if (@($Children).Count -gt 0) { $node['children'] = @($Children) }
    return ,$node
}

function New-WorkflowTree {
    param([Parameter(Mandatory)][object[]] $Elements)

    $payload = [ordered]@{
        depth         = 20
        interactive   = $false
        hideDisabled  = $false
        hideOffscreen = $false
        windows       = @(
            [ordered]@{ hwnd = 99; title = 't'; elementCount = @($Elements).Count; elements = @($Elements) }
        )
    }
    # The production tree arrives as parsed JSON; round-tripping here keeps the fixture honest,
    # because a parsed object exposes its fields through PSObject.Properties and an ordered
    # dictionary does not.
    return ,(ConvertTo-Json -InputObject $payload -Depth 20 -Compress | ConvertFrom-Json)
}

function New-WorkflowRowTree {
    # A pending row whose accessible name carries a state suffix, plus its same-named inner
    # container and its filename TextBlocks: only the ListItem may ever be selected.
    $rowName = '待入库文件 paired.mp4，状态 需要前置处理'
    $row = New-WorkflowNode -Type 'ListItem' -Name $rowName -GradientSelector 'itm-pairedmp4-0001'
    $inner = New-WorkflowNode -Type 'Group' -Name $rowName -AutomationId 'InboxCard_deadbeef' -GradientSelector 'grp-inboxcard-dead'
    $fileText = New-WorkflowNode -Type 'Text' -Name 'paired.mp4'
    $grid = New-WorkflowNode -Type 'List' -Name '待入库文件列表' -AutomationId 'InboxGrid' -GradientSelector 'InboxGrid' -Children @($row, $inner, $fileText)
    return New-WorkflowTree -Elements @($grid)
}

function New-WorkflowCompositeTree {
    # The composite picker: the stable id belongs to the outer AutoSuggestBox, the value lives on
    # its single inner Edit, which carries its own selector.
    $inner = New-WorkflowNode -Type 'Edit' -ClassName 'TextBox' -Name '输入待入库资料的作者' -GradientSelector 'txt-textbox-0001'
    $picker = New-WorkflowNode -Type 'Group' -Name '输入待入库资料的作者' -AutomationId 'InboxAuthorInput' -GradientSelector 'grp-inboxauthorinpu-0001' -Children @($inner)
    $terms = New-WorkflowNode -Type 'Group' -Name '已添加的待入库资料作者' -AutomationId 'InboxAuthorInputTerms' -Offscreen $true
    return New-WorkflowTree -Elements @($picker, $terms)
}

function Get-TestCases {
    return @(
    @{
        Name = 'flat nodes are produced from the driver response shape and a foreign shape is refused'
        Body = {
            $tree = New-WorkflowRowTree
            $nodes = ConvertTo-UiAcceptanceFlatNodes -Tree $tree -FailureCode 'ui.driver_schema_incompatible'
            Assert-Equal 4 $nodes.Count
            Assert-Throws { ConvertTo-UiAcceptanceFlatNodes -Tree ([pscustomobject]@{ tree = @() }) -FailureCode 'ui.driver_schema_incompatible' }
        }
    },
    @{
        Name = 'an element the driver reports twice while a pop-up is open collapses to one node'
        Body = {
            # Measured product behaviour: with a combo box drop-down open the pinned driver returns
            # the same elements more than once, each copy carrying the identical slug and bounds.
            $row = New-WorkflowNode -Type 'ListItem' -Name '待入库文件 paired.mp4，状态 需要前置处理' `
                -GradientSelector 'itm-pairedmp4-0001'
            $nodes = ConvertTo-UiAcceptanceFlatNodes -Tree (New-WorkflowTree -Elements @($row, $row)) `
                -FailureCode 'ui.driver_schema_incompatible'
            # A single node comes back as a scalar in Windows PowerShell, so the count is taken
            # after re-wrapping rather than from the returned value.
            Assert-Equal 1 @($nodes).Count
            Assert-Equal 'itm-pairedmp4-0001' (Get-UiAcceptanceNodeSelector -Node @($nodes)[0] -FailureCode 'row')
        }
    },
    @{
        Name = 'nodes that share a slug but differ in an observed field are both kept'
        Body = {
            # The collapse must key on the whole observed identity, never on the slug alone, so a
            # genuine pair of elements still reaches the uniqueness rule and is refused there.
            $first = New-WorkflowNode -Type 'ListItem' -Name '待入库文件 paired.mp4，状态 需要前置处理' `
                -GradientSelector 'itm-shared-0001'
            $second = New-WorkflowNode -Type 'ListItem' -Name '待入库文件 paired.mp4，状态 待入库' `
                -GradientSelector 'itm-shared-0001'
            $nodes = ConvertTo-UiAcceptanceFlatNodes -Tree (New-WorkflowTree -Elements @($first, $second)) `
                -FailureCode 'ui.driver_schema_incompatible'
            Assert-Equal 2 @($nodes).Count
            Assert-Throws { Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType 'ListItem' `
                    -NamePrefix '待入库文件 paired.mp4' -FailureCode 'row' }
        }
    },
    @{
        Name = 'the dynamic row is addressed by its ListItem type and name prefix, never by a sibling'
        Body = {
            $nodes = ConvertTo-UiAcceptanceFlatNodes -Tree (New-WorkflowRowTree) -FailureCode 'ui.driver_schema_incompatible'
            $row = Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType 'ListItem' `
                -NamePrefix '待入库文件 paired.mp4' -NameContains 'paired.mp4' -FailureCode 'row'
            Assert-True ($null -ne $row)
            Assert-Equal 'itm-pairedmp4-0001' (Get-UiAcceptanceNodeSelector -Node $row -FailureCode 'row')
            # A same-named Group and the filename TextBlocks also exist, so the type criterion is
            # what keeps the choice unique: only one ListItem carries this prefix.
            Assert-Equal 1 @($nodes | Where-Object {
                (Get-UiAcceptanceNodeProperty -Node $_ -Name 'type') -ceq 'ListItem' -and
                (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -like '待入库文件 paired.mp4*' }).Count
            Assert-Equal 2 @($nodes | Where-Object {
                (Get-UiAcceptanceNodeProperty -Node $_ -Name 'name') -like '待入库文件 paired.mp4*' }).Count
            Assert-Equal 'ListItem' (Get-UiAcceptanceNodeProperty -Node $row -Name 'type')
            Assert-True ((Get-UiAcceptanceNodeProperty -Node $row -Name 'automationId') -ceq '')
        }
    },
    @{
        Name = 'an ambiguous or absent match is refused instead of picking one candidate'
        Body = {
            $a = New-WorkflowNode -Type 'ListItem' -Name '待入库文件 paired.mp4，状态 需要前置处理'
            $b = New-WorkflowNode -Type 'ListItem' -Name '待入库文件 paired.mp4，状态 待入库'
            $nodes = ConvertTo-UiAcceptanceFlatNodes -Tree (New-WorkflowTree -Elements @($a, $b)) -FailureCode 'schema'
            Assert-Throws { Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType 'ListItem' -NamePrefix '待入库文件 paired.mp4' -FailureCode 'row' }
            $none = Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType 'ListItem' -NamePrefix '待入库文件 photo.jpg' -FailureCode 'row'
            Assert-True ($null -eq $none)
        }
    },
    @{
        Name = 'a node without a usable selector is refused rather than guessed'
        Body = {
            $nodes = ConvertTo-UiAcceptanceFlatNodes -Tree (New-WorkflowTree -Elements @(
                (New-WorkflowNode -Type 'ListItem' -Name '待入库文件 paired.mp4，状态 需要前置处理'))) -FailureCode 'schema'
            Assert-Throws { Get-UiAcceptanceNodeSelector -Node $nodes[0] -FailureCode 'row.selector' }
        }
    },
    @{
        Name = 'the composite value is addressed through its unique inner Edit descendant'
        Body = {
            $nodes = ConvertTo-UiAcceptanceFlatNodes -Tree (New-WorkflowCompositeTree) -FailureCode 'schema'
            $outer = @($nodes | Where-Object { (Get-UiAcceptanceNodeProperty -Node $_ -Name 'automationId') -ceq 'InboxAuthorInput' })[0]
            Assert-Equal 'txt-textbox-0001' (Get-UiAcceptanceDescendantSelector -Node $outer -NativeType 'Edit' -ClassName 'TextBox' -FailureCode 'author.composite')
            Assert-Equal 'grp-inboxauthorinpu-0001' (Get-UiAcceptanceNodeSelector -Node $outer -FailureCode 'author.composite')
        }
    },
    @{
        Name = 'a composite whose inner control is missing or doubled is refused'
        Body = {
            $inner1 = New-WorkflowNode -Type 'Edit' -ClassName 'TextBox' -GradientSelector 'txt-a'
            $inner2 = New-WorkflowNode -Type 'Edit' -ClassName 'TextBox' -GradientSelector 'txt-b'
            $doubled = New-WorkflowNode -Type 'Group' -AutomationId 'InboxAuthorInput' -GradientSelector 'grp-a' -Children @($inner1, $inner2)
            $nodes = ConvertTo-UiAcceptanceFlatNodes -Tree (New-WorkflowTree -Elements @($doubled)) -FailureCode 'schema'
            Assert-Throws { Get-UiAcceptanceDescendantSelector -Node $nodes[0] -NativeType 'Edit' -ClassName 'TextBox' -FailureCode 'author.composite' }
            $empty = New-WorkflowNode -Type 'Group' -AutomationId 'InboxAuthorInput' -GradientSelector 'grp-b'
            $nodes2 = ConvertTo-UiAcceptanceFlatNodes -Tree (New-WorkflowTree -Elements @($empty)) -FailureCode 'schema'
            Assert-Throws { Get-UiAcceptanceDescendantSelector -Node $nodes2[0] -NativeType 'Edit' -ClassName 'TextBox' -FailureCode 'author.composite' }
        }
    },
    @{
        Name = 'a lookup-stage miss re-resolves the selector and dispatches again under a new step id'
        Body = {
            $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-wf-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($base)
            try {
                Reset-UiAcceptanceMutationSteps
                $env:TRACEFOLD_FAKE_ARGV_LOG = Join-Path $base 'argv.jsonl'
                $env:TRACEFOLD_FAKE_MISS_MARKER = Join-Path $base 'miss-once'
                $script:resolvedCalls = 0
                $resolve = {
                    $script:resolvedCalls = $script:resolvedCalls + 1
                    return 'itm-pairedmp4-000' + $script:resolvedCalls
                }
                $driver = Join-Path $script:uiRoot 'tests/fixtures/fake-winapp.ps1'
                $result = Invoke-UiAcceptanceResolvedMutation -StepId 'row.select' -ProcessId 4242 `
                    -WinAppPath $driver -RunToken 't' -OwnedRoot $base -Verb 'invoke' `
                    -ResolveSelector $resolve -FailureCode 'row.select' -MaxAttempts 3 -DeadlineSeconds 20
                Assert-Equal 'Passed' $result.Status
                Assert-Equal 2 $script:resolvedCalls
                $log = @([IO.File]::ReadAllLines($env:TRACEFOLD_FAKE_ARGV_LOG, [Text.Encoding]::UTF8) |
                    ForEach-Object { ConvertFrom-Json $_ })
                $invoked = @($log | Where-Object { $_.argv[1] -ceq 'invoke' })
                Assert-Equal 2 $invoked.Count
                # The selector is re-resolved, so the second dispatch uses the newer selector.
                Assert-Equal 'itm-pairedmp4-0002' ([string] $invoked[1].argv[2])
            }
            finally {
                Remove-Item Env:TRACEFOLD_FAKE_ARGV_LOG -ErrorAction SilentlyContinue
                Remove-Item Env:TRACEFOLD_FAKE_MISS_MARKER -ErrorAction SilentlyContinue
                Remove-UiTestTree -Path $base
            }
        }
    },
    @{
        Name = 'a lookup miss reported as a generic slug error is also re-resolved'
            Body = {
                $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-wf-' + [guid]::NewGuid().ToString('N'))
                [void][IO.Directory]::CreateDirectory($base)
                try {
                    Reset-UiAcceptanceMutationSteps
                    $env:TRACEFOLD_FAKE_ARGV_LOG = Join-Path $base 'argv.jsonl'
                    $env:TRACEFOLD_FAKE_SLUG_MISS_MARKER = Join-Path $base 'slug-miss-once'
                    $script:slugResolves = 0
                    $driver = Join-Path $script:uiRoot 'tests/fixtures/fake-winapp.ps1'
                    $result = Invoke-UiAcceptanceResolvedMutation -StepId 'row.select' -ProcessId 4242 `
                        -WinAppPath $driver -RunToken 't' -OwnedRoot $base -Verb 'invoke' `
                        -ResolveSelector {
                            $script:slugResolves = $script:slugResolves + 1
                            return 'itm-photojpg-000' + $script:slugResolves
                        } -FailureCode 'row.select' -MaxAttempts 3 -DeadlineSeconds 20
                    Assert-Equal 'Passed' $result.Status
                    # The grid re-renders, so the selector observed first is stale; re-resolving is
                    # the only safe response because nothing was dispatched.
                    Assert-Equal 2 $script:slugResolves
                    $log = @(Get-Content -LiteralPath $env:TRACEFOLD_FAKE_ARGV_LOG | ForEach-Object { ConvertFrom-Json $_ })
                    Assert-Equal 2 @($log | Where-Object { $_.argv[1] -ceq 'invoke' }).Count
                }
                finally {
                    Remove-Item Env:TRACEFOLD_FAKE_ARGV_LOG -ErrorAction SilentlyContinue
                    Remove-Item Env:TRACEFOLD_FAKE_SLUG_MISS_MARKER -ErrorAction SilentlyContinue
                    Remove-UiTestTree -Path $base
                }
            }
        },
        @{
            Name = 'a timeout is reported and the mutation is never resent'
        Body = {
            $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-wf-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($base)
            try {
                Reset-UiAcceptanceMutationSteps
                $env:TRACEFOLD_FAKE_ARGV_LOG = Join-Path $base 'argv.jsonl'
                $env:TRACEFOLD_FAKE_HANG_MS = '3000'
                $driver = Join-Path $script:uiRoot 'tests/fixtures/fake-winapp.ps1'
                $result = Invoke-UiAcceptanceResolvedMutation -StepId 'row.select' -ProcessId 4242 `
                    -WinAppPath $driver -RunToken 't' -OwnedRoot $base -Verb 'invoke' `
                    -ResolveSelector { 'itm-pairedmp4-0001' } -FailureCode 'row.select' `
                    -MaxAttempts 3 -DeadlineSeconds 20 -TimeoutMs 1000
                Assert-True ([bool] $result.TimedOut)
                Assert-True ($result.Status -cne 'Passed')
                $log = @([IO.File]::ReadAllLines($env:TRACEFOLD_FAKE_ARGV_LOG, [Text.Encoding]::UTF8) | ForEach-Object { ConvertFrom-Json $_ })
                Assert-Equal 1 @($log | Where-Object { $_.argv[1] -ceq 'invoke' }).Count
            }
            finally {
                Remove-Item Env:TRACEFOLD_FAKE_ARGV_LOG -ErrorAction SilentlyContinue
                Remove-Item Env:TRACEFOLD_FAKE_HANG_MS -ErrorAction SilentlyContinue
                Remove-UiTestTree -Path $base
            }
        }
    },
    @{
        Name = 'a dispatched failure that is not a lookup miss is returned, not resent'
        Body = {
            $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-wf-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($base)
            try {
                Reset-UiAcceptanceMutationSteps
                $env:TRACEFOLD_FAKE_ARGV_LOG = Join-Path $base 'argv.jsonl'
                # No raw payload here: the raw switch answers with exit 0, while a real failed
                # dispatch is a non-zero exit carrying no element_not_found code.
                $env:TRACEFOLD_FAKE_EXIT_CODE = '1'
                $env:TRACEFOLD_FAKE_STDERR = 'the element could not be invoked'
                $driver = Join-Path $script:uiRoot 'tests/fixtures/fake-winapp.ps1'
                $result = Invoke-UiAcceptanceResolvedMutation -StepId 'row.select' -ProcessId 4242 `
                    -WinAppPath $driver -RunToken 't' -OwnedRoot $base -Verb 'invoke' `
                    -ResolveSelector { 'itm-pairedmp4-0001' } -FailureCode 'row.select' -MaxAttempts 3 -DeadlineSeconds 20
                Assert-Equal 'Failed' $result.Status
                $log = @([IO.File]::ReadAllLines($env:TRACEFOLD_FAKE_ARGV_LOG, [Text.Encoding]::UTF8) | ForEach-Object { ConvertFrom-Json $_ })
                Assert-Equal 1 @($log | Where-Object { $_.argv[1] -ceq 'invoke' }).Count
            }
            finally {
                foreach ($name in @('TRACEFOLD_FAKE_ARGV_LOG','TRACEFOLD_FAKE_EXIT_CODE','TRACEFOLD_FAKE_STDERR')) {
                    Remove-Item ('Env:' + $name) -ErrorAction SilentlyContinue
                }
                Remove-UiTestTree -Path $base
            }
        }
    },
    @{
        Name = 'a persistent lookup miss exhausts the bounded attempts instead of looping'
        Body = {
            $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-wf-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($base)
            try {
                Reset-UiAcceptanceMutationSteps
                $env:TRACEFOLD_FAKE_ARGV_LOG = Join-Path $base 'argv.jsonl'
                $env:TRACEFOLD_FAKE_MISS_ALWAYS = '1'
                $driver = Join-Path $script:uiRoot 'tests/fixtures/fake-winapp.ps1'
                Assert-Throws {
                    Invoke-UiAcceptanceResolvedMutation -StepId 'row.select' -ProcessId 4242 `
                        -WinAppPath $driver -RunToken 't' -OwnedRoot $base -Verb 'invoke' `
                        -ResolveSelector { 'itm-pairedmp4-0001' } -FailureCode 'row.select' `
                        -MaxAttempts 2 -DeadlineSeconds 20
                }
                $log = @([IO.File]::ReadAllLines($env:TRACEFOLD_FAKE_ARGV_LOG, [Text.Encoding]::UTF8) | ForEach-Object { ConvertFrom-Json $_ })
                Assert-Equal 2 @($log | Where-Object { $_.argv[1] -ceq 'invoke' }).Count
            }
            finally {
                foreach ($name in @('TRACEFOLD_FAKE_ARGV_LOG','TRACEFOLD_FAKE_MISS_ALWAYS')) {
                    Remove-Item ('Env:' + $name) -ErrorAction SilentlyContinue
                }
                Remove-UiTestTree -Path $base
            }
        }
    },
    @{
        Name = 'a scenario cannot dispatch the same resolved mutation step twice'
        Body = {
            $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-wf-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($base)
            try {
                Reset-UiAcceptanceMutationSteps
                $driver = Join-Path $script:uiRoot 'tests/fixtures/fake-winapp.ps1'
                $first = Invoke-UiAcceptanceResolvedMutation -StepId 'row.select' -ProcessId 4242 `
                    -WinAppPath $driver -RunToken 't' -OwnedRoot $base -Verb 'invoke' `
                    -ResolveSelector { 'itm-pairedmp4-0001' } -FailureCode 'row.select'
                Assert-Equal 'Passed' $first.Status
                # The toolkit records a step once, so a second dispatch under the same id is refused
                # outright rather than silently repeated.
                Assert-Throws {
                    Invoke-UiAcceptanceResolvedMutation -StepId 'row.select' -ProcessId 4242 `
                        -WinAppPath $driver -RunToken 't' -OwnedRoot $base -Verb 'invoke' `
                        -ResolveSelector { 'itm-pairedmp4-0002' } -FailureCode 'row.select'
                }
            }
            finally { Remove-UiTestTree -Path $base }
        }
    },
    @{
        Name = 'taxonomy token commits through the observed inner Edit exactly once'
        Body = {
            $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-wf-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($base)
            try {
                Reset-UiAcceptanceMutationSteps
                $env:TRACEFOLD_FAKE_ARGV_LOG = Join-Path $base 'argv.jsonl'
                $driver = Join-Path $script:uiRoot 'tests/fixtures/fake-winapp.ps1'
                $term = [string][char]0x4F5C + [char]0x8005 + [char]0x7532
                $result = Set-UiAcceptanceTaxonomyTerm -StepId 'author-one' `
                    -InnerSelector 'txt-author-inner-1234' -Term $term -ProcessId 4242 `
                    -WinAppPath $driver -RunToken 't' -OwnedRoot $base -FailureCode 'author.commit' `
                    -ForegroundCheck { param($id) $true } -DesktopCheck { $true }
                Assert-Equal 'Passed' $result.Status
                $log = @([IO.File]::ReadAllLines($env:TRACEFOLD_FAKE_ARGV_LOG, [Text.Encoding]::UTF8) | ForEach-Object { ConvertFrom-Json $_ })
                Assert-Equal 2 $log.Count
                Assert-Equal 'set-value' $log[0].argv[1]
                Assert-Equal 'txt-author-inner-1234' $log[0].argv[2]
                Assert-Equal $term $log[0].argv[3]
                Assert-Equal 'send-keys' $log[1].argv[1]
                Assert-Equal 'enter' $log[1].argv[2]
                Assert-Equal 'txt-author-inner-1234' $log[1].argv[4]
                Assert-Equal 1 @($log | Where-Object { $_.argv[1] -ceq 'send-keys' }).Count
            }
            finally {
                Remove-Item Env:TRACEFOLD_FAKE_ARGV_LOG -ErrorAction SilentlyContinue
                Remove-UiTestTree -Path $base
            }
        }
    },
    @{
        Name = 'a fixture must match the manifest size and hash before it may be used'
        Body = {
            $asset = [pscustomobject] @{
                fixedFilename  = 'paired.mp4'
                repositoryPath = 'apps/media-api/tests/fixtures/media/paired.mp4'
                sha256         = 'B5B85F2CA95EA5EB781B42D202D1C3DB361BF37F5BF4B3107A79469FC716A1A3'
                sizeBytes      = 2405
                seedDirectory  = 'inbox'
            }
            $verified = Test-UiAcceptanceFixtureAsset -Asset $asset -RepositoryRoot $script:root
            Assert-Equal 'B5B85F2CA95EA5EB781B42D202D1C3DB361BF37F5BF4B3107A79469FC716A1A3' $verified.Sha256
            $wrongSize = $asset.PSObject.Copy()
            $wrongSize.sizeBytes = 2406
            Assert-Throws { Test-UiAcceptanceFixtureAsset -Asset $wrongSize -RepositoryRoot $script:root }
            $wrongHash = $asset.PSObject.Copy()
            $wrongHash.sha256 = ('0' * 64)
            Assert-Throws { Test-UiAcceptanceFixtureAsset -Asset $wrongHash -RepositoryRoot $script:root }
            $outside = $asset.PSObject.Copy()
            $outside.repositoryPath = '../outside/paired.mp4'
            Assert-Throws { Test-UiAcceptanceFixtureAsset -Asset $outside -RepositoryRoot $script:root }
        }
    }
    )
}
