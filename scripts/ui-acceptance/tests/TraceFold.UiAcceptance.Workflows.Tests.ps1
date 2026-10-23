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

function Convert-ToFlat {
    param([Parameter(Mandatory)][object[]] $Elements)

    $flat = ConvertTo-UiAcceptanceFlatNodes -Tree (New-WorkflowTree -Elements $Elements) `
        -FailureCode 'ui.driver_schema_incompatible'
    return ,@($flat)
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
        Name = 'a hidden candidate is excluded on request while the visible one stays addressable'
        Body = {
            # A library row is a template, so its identity is the file name in its own name prefix.
            # Two rows render the same file, but only one is on screen: without the visibility
            # criterion the pair would be refused as ambiguous and the on-screen row could never be
            # addressed at all, which is the defect this criterion exists to prevent.
            $visible = New-WorkflowNode -Type 'ListItem' -Name '正式文件 photo.jpg，实际文件 photo.jpg，图片' `
                -GradientSelector 'itm-photo-0001'
            $hidden = New-WorkflowNode -Type 'ListItem' -Name '正式文件 photo.jpg，实际文件 photo.jpg，图片' `
                -Offscreen $true -GradientSelector 'itm-photo-0002'
            $nodes = ConvertTo-UiAcceptanceFlatNodes -Tree (New-WorkflowTree -Elements @($visible, $hidden)) `
                -FailureCode 'ui.driver_schema_incompatible'
            Assert-Throws { Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType 'ListItem' `
                    -NameContains 'photo.jpg' -FailureCode 'row' }
            $row = Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType 'ListItem' `
                -NameContains 'photo.jpg' -VisibleOnly -FailureCode 'row'
            Assert-True ($null -ne $row)
            Assert-Equal 'itm-photo-0001' (Get-UiAcceptanceNodeSelector -Node $row -FailureCode 'row')
            # The criterion only excludes: a node that is genuinely absent is still reported absent.
            $absent = Find-UiAcceptanceUniqueNode -Nodes $nodes -NativeType 'ListItem' `
                -NameContains 'photo-third.jpg' -VisibleOnly -FailureCode 'row'
            Assert-True ($null -eq $absent)
            # Two on-screen candidates remain ambiguous, so the exclusion never weakens uniqueness.
            $second = New-WorkflowNode -Type 'ListItem' -Name '正式文件 photo.jpg，实际文件 photo.jpg，图片'
            $both = ConvertTo-UiAcceptanceFlatNodes -Tree (New-WorkflowTree -Elements @($visible, $second)) `
                -FailureCode 'ui.driver_schema_incompatible'
            Assert-Throws { Find-UiAcceptanceUniqueNode -Nodes $both -NativeType 'ListItem' `
                    -NameContains 'photo.jpg' -VisibleOnly -FailureCode 'row' }
        }
    },
    @{
        Name = 'list rows come from the named container only, in render order, never from a sibling container'
        Body = {
            # The row prefix is shared by every row of the list, so it alone cannot tell a row from a
            # same-prefixed item that belongs to the header or to the navigation pane.
            $rowName = '正式文件 photo.jpg，实际文件 photo.jpg，图片'
            $otherRowName = '正式文件 photo-third.jpg，实际文件 photo-third.jpg，图片'
            $nestedRow = New-WorkflowNode -Type 'ListItem' -Name $rowName
            $card = New-WorkflowNode -Type 'Group' -Name $rowName -Children @($nestedRow)
            $gridRow = New-WorkflowNode -Type 'ListItem' -Name $otherRowName
            $grid = New-WorkflowNode -Type 'List' -AutomationId 'LibraryGrid' -Name '正式文件列表' `
                -Children @($card, $gridRow)
            $headerRow = New-WorkflowNode -Type 'ListItem' -Name '正式文件 photo-replacement.jpg，实际文件 photo-replacement.jpg，图片'
            $header = New-WorkflowNode -Type 'Group' -AutomationId 'LibraryListHeader' -Children @($headerRow)
            $navItem = New-WorkflowNode -Type 'ListItem' -Name '正式文件 nav.jpg，实际文件 nav.jpg，图片' `
                -AutomationId 'NavLibrary'
            $nodes = ConvertTo-UiAcceptanceFlatNodes `
                -Tree (New-WorkflowTree -Elements @($header, $navItem, $grid)) `
                -FailureCode 'ui.driver_schema_incompatible'

            $rows = Get-UiAcceptanceListRowNames -Nodes $nodes -ContainerId 'LibraryGrid' `
                -NamePrefix '正式文件 ' -FailureCode 'grid'
            Assert-Equal 2 @($rows).Count
            Assert-Equal $rowName (@($rows)[0])
            Assert-Equal $otherRowName (@($rows)[1])
        }
    },
    @{
        Name = 'a row reported off screen is left out and a repeated row is not collapsed'
        Body = {
            $rowName = '正式文件 photo.jpg，实际文件 photo.jpg，图片'
            $visible = New-WorkflowNode -Type 'ListItem' -Name $rowName
            $hiddenOther = New-WorkflowNode -Type 'ListItem' `
                -Name '正式文件 hidden.jpg，实际文件 hidden.jpg，图片' -Offscreen $true
            $visibleRepeat = New-WorkflowNode -Type 'ListItem' -Name $rowName
            $hiddenRepeat = New-WorkflowNode -Type 'ListItem' -Name $rowName -Offscreen $true
            $grid = New-WorkflowNode -Type 'List' -AutomationId 'LibraryGrid' `
                -Children @($visible, $hiddenOther, $visibleRepeat, $hiddenRepeat)
            $nodes = ConvertTo-UiAcceptanceFlatNodes `
                -Tree (New-WorkflowTree -Elements @($grid)) -FailureCode 'ui.driver_schema_incompatible'

            $rows = Get-UiAcceptanceListRowNames -Nodes $nodes -ContainerId 'LibraryGrid' `
                -NamePrefix '正式文件 ' -FailureCode 'grid'
            # Two identical rows are two rows, and the off-screen ones are none of them.
            Assert-Equal 2 @($rows).Count
            Assert-Equal $rowName (@($rows)[0])
            Assert-Equal $rowName (@($rows)[1])
        }
    },
    @{
        Name = 'a missing or ambiguous row container is refused rather than guessed'
        Body = {
            $only = New-WorkflowNode -Type 'List' -AutomationId 'LibraryListHeader'
            $nodes = Convert-ToFlat -Elements @($only)
            Assert-Throws { Get-UiAcceptanceListRowNames -Nodes $nodes -ContainerId 'LibraryGrid' `
                    -NamePrefix 'x' -FailureCode 'grid' }
            $first = New-WorkflowNode -Type 'List' -AutomationId 'LibraryGrid' -GradientSelector 'lst-a'
            $second = New-WorkflowNode -Type 'List' -AutomationId 'LibraryGrid' -GradientSelector 'lst-b'
            $ambiguous = Convert-ToFlat -Elements @($first, $second)
            Assert-Throws { Get-UiAcceptanceListRowNames -Nodes $ambiguous -ContainerId 'LibraryGrid' `
                    -NamePrefix 'x' -FailureCode 'grid' }
        }
    },
    @{
        Name = 'list rows carry the text they render, in order, and only from their own container'
        Body = {
            $sourceRow = New-WorkflowNode -Type 'ListItem' -GradientSelector 'itm-source-0001' -Children @(
                (New-WorkflowNode -Type 'Group' -Children @(
                        (New-WorkflowNode -Type 'Text' -Name '合并源'),
                        (New-WorkflowNode -Type 'Text' -Name '2'))))
            $targetRow = New-WorkflowNode -Type 'ListItem' -GradientSelector 'itm-target-0001' -Children @(
                (New-WorkflowNode -Type 'Text' -Name '合并目标'))
            $termList = New-WorkflowNode -Type 'List' -AutomationId 'TaxonomyTermList' `
                -Children @($sourceRow, $targetRow)
            # A same-shaped row that belongs to another list, which must never be read as a row of this one.
            $outsideRow = New-WorkflowNode -Type 'ListItem' -GradientSelector 'itm-outside-0001' -Children @(
                (New-WorkflowNode -Type 'Text' -Name '范围外'))
            $otherList = New-WorkflowNode -Type 'List' -AutomationId 'TaxonomyMergeTargetResults' `
                -Children @($outsideRow)
            $nodes = Convert-ToFlat -Elements @($otherList, $termList)

            $rows = Get-UiAcceptanceListRowTexts -Nodes $nodes -ContainerId 'TaxonomyTermList' `
                -FailureCode 'term_rows'
            Assert-Equal 2 @($rows).Count
            Assert-Equal 2 @(@($rows)[0].Texts).Count
            Assert-Equal '合并源' (@($rows)[0].Texts)[0]
            Assert-Equal '2' (@($rows)[0].Texts)[1]
            Assert-Equal 'itm-source-0001' (@($rows)[0].Selector)
            Assert-Equal '合并目标' (@($rows)[1].Texts)[0]
            Assert-Equal 0 @($rows | Where-Object { @($_.Texts) -ccontains '范围外' }).Count
        }
    },
    @{
        Name = 'a hidden row and a hidden text are left out, and two rows that render the same text both count'
        Body = {
            $visibleRow = New-WorkflowNode -Type 'ListItem' -GradientSelector 'itm-a-0001' -Children @(
                (New-WorkflowNode -Type 'Text' -Name '甲'))
            $hiddenRow = New-WorkflowNode -Type 'ListItem' -GradientSelector 'itm-hidden-0001' -Offscreen $true `
                -Children @((New-WorkflowNode -Type 'Text' -Name '隐藏'))
            # Two rows that render the same text are two rows: their slugs differ, as two real rows do.
            $duplicateRow = New-WorkflowNode -Type 'ListItem' -GradientSelector 'itm-b-0001' -Children @(
                (New-WorkflowNode -Type 'Text' -Name '甲'))
            $mixedRow = New-WorkflowNode -Type 'ListItem' -GradientSelector 'itm-mixed-0001' -Children @(
                (New-WorkflowNode -Type 'Text' -Name '乙'),
                (New-WorkflowNode -Type 'Text' -Name '不可见文本' -Offscreen $true))
            $termList = New-WorkflowNode -Type 'List' -AutomationId 'TaxonomyTermList' `
                -Children @($visibleRow, $hiddenRow, $duplicateRow, $mixedRow)
            $nodes = Convert-ToFlat -Elements @($termList)

            $rows = Get-UiAcceptanceListRowTexts -Nodes $nodes -ContainerId 'TaxonomyTermList' `
                -FailureCode 'term_rows'
            Assert-Equal 3 @($rows).Count
            Assert-Equal '甲' (@($rows)[0].Texts)[0]
            Assert-Equal '甲' (@($rows)[1].Texts)[0]
            Assert-Equal 1 @(@($rows)[2].Texts).Count
            Assert-Equal '乙' (@($rows)[2].Texts)[0]
            Assert-Equal 0 @($rows | Where-Object { @($_.Texts) -ccontains '隐藏' }).Count
        }
    },
    @{
        Name = 'a missing or ambiguous row-text container is refused rather than guessed'
        Body = {
            $only = New-WorkflowNode -Type 'List' -AutomationId 'LibraryGrid'
            $nodes = Convert-ToFlat -Elements @($only)
            Assert-Throws { Get-UiAcceptanceListRowTexts -Nodes $nodes -ContainerId 'TaxonomyTermList' `
                    -FailureCode 'term_rows' }
            $first = New-WorkflowNode -Type 'List' -AutomationId 'TaxonomyTermList' -GradientSelector 'lst-a'
            $second = New-WorkflowNode -Type 'List' -AutomationId 'TaxonomyTermList' -GradientSelector 'lst-b'
            $ambiguous = Convert-ToFlat -Elements @($first, $second)
            Assert-Throws { Get-UiAcceptanceListRowTexts -Nodes $ambiguous -ContainerId 'TaxonomyTermList' `
                    -FailureCode 'term_rows' }
        }
    },
    @{
        Name = 'a row set comparison ignores order but not repeats, keeps a nameless row, and refuses a wrong set'
        Body = {
            Assert-True (Test-UiAcceptanceRowSetMatch -Observed @('b', 'a') -Expected @('a', 'b'))
            Assert-True (Test-UiAcceptanceRowSetMatch -Observed @() -Expected @())
            # An extra row, a missing row and a substituted row are all different sets.
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @('a') -Expected @('a', 'b')))
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @('a', 'b') -Expected @('a')))
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @('a', 'c') -Expected @('a', 'b')))
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @() -Expected @('a')))
            # A repeat is not the same as its absence: two identical rows must never equal one.
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @('a', 'a') -Expected @('a')))
            Assert-True (Test-UiAcceptanceRowSetMatch -Observed @('a', 'a') -Expected @('a', 'a'))
            # An empty set is a legitimate answer, and the two runtimes deliver it differently, so
            # $null and @() are the two spellings of "nothing" and both have to be accepted as the
            # same set.
            Assert-True (Test-UiAcceptanceRowSetMatch -Observed $null -Expected $null)
            Assert-True (Test-UiAcceptanceRowSetMatch -Observed $null -Expected @())
            Assert-True (Test-UiAcceptanceRowSetMatch -Observed @() -Expected $null)
            Assert-True (Test-UiAcceptanceRowSetMatch -Observed @() -Expected @())
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed $null -Expected @('a')))
            # A row that rendered no text is still a row, and an empty string is not a third spelling
            # of "nothing". Treating it as one would let a nameless row be absorbed into an empty set
            # - or into its own repeat - and so confirm a row set the product never showed.
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @('') -Expected @()))
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @() -Expected @('')))
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @('') -Expected @('', '')))
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @('', '') -Expected @('')))
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @('a', '') -Expected @('a')))
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @('a') -Expected @('a', '')))
            # The same empty rows on both sides still compare equal, and the same multiset read in
            # another order is still the same multiset.
            Assert-True (Test-UiAcceptanceRowSetMatch -Observed @('') -Expected @(''))
            Assert-True (Test-UiAcceptanceRowSetMatch -Observed @('', '') -Expected @('', ''))
            Assert-True (Test-UiAcceptanceRowSetMatch -Observed @('a', '', 'b', 'a') -Expected @('b', 'a', 'a', ''))
            # A repeat is still not the same as its absence, and an empty row is a value whose
            # repeats have to be counted too.
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @('a', 'a', '') -Expected @('a', '')))
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @('a', '', '') -Expected @('a', '')))
            # A row of text that differs only in case is still a different row.
            Assert-True (-not (Test-UiAcceptanceRowSetMatch -Observed @('a') -Expected @('A')))
        }
    },
    @{
        Name = 'a named node query matches only the stated type, the exact name, and on-screen elements'
        Body = {
            $chipName = '移除作者筛选条件'
            $right = New-WorkflowNode -Type 'Button' -Name $chipName -GradientSelector 'btn-chip-0001'
            $wrongType = New-WorkflowNode -Type 'Text' -Name $chipName -GradientSelector 'txt-chip-0001'
            $nameContains = New-WorkflowNode -Type 'Button' -Name ($chipName + 'x') -GradientSelector 'btn-other-0001'
            $hidden = New-WorkflowNode -Type 'Button' -Name $chipName -GradientSelector 'btn-hidden-0001' -Offscreen $true
            $nodes = ConvertTo-UiAcceptanceFlatNodes `
                -Tree (New-WorkflowTree -Elements @($right, $wrongType, $nameContains, $hidden)) `
                -FailureCode 'ui.driver_schema_incompatible'

            $visible = Get-UiAcceptanceNamedNodes -Nodes $nodes -NativeType 'Button' -Name $chipName -VisibleOnly
            Assert-Equal 1 @($visible).Count
            Assert-Equal 'btn-chip-0001' (Get-UiAcceptanceNodeSelector -Node @($visible)[0] -FailureCode 'chip')
            # Without the visibility filter a control inside a collapsed container is returned as well,
            # which is exactly the false confirmation the callers must not accept.
            $anywhere = Get-UiAcceptanceNamedNodes -Nodes $nodes -NativeType 'Button' -Name $chipName
            Assert-Equal 2 @($anywhere).Count
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
    },
    @{
        Name = 'a support payload is decoded as UTF-8 whatever the machine code page is'
        Body = {
            $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-wf-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($base)
            try {
                $support = Join-Path $script:uiRoot 'tests/fixtures/fake-support.ps1'
                $env:TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN = '1'
                $env:TRACEFOLD_FAKE_SUPPORT_SNAPSHOT_LEAD_BYTE = '1'
                # The payload carries a term whose last UTF-8 byte is a lead byte, so a capture that
                # let the machine code page decode this pipe would consume the closing quote and the
                # JSON around it would not survive.
                $term = [string][char]0x5408 + [char]0x5E76 + [char]0x6E90
                $result = Invoke-UiAcceptanceSupportCommand -SupportPath $support `
                    -Arguments @('snapshot','--data-root',$base,'--token','test-token')
                Assert-Equal 0 $result.ExitCode
                Assert-True ($result.Stdout.TrimEnd().EndsWith('}')) 'the whole payload must survive'
                $parsed = ConvertFrom-UiAcceptanceJson -Text $result.Stdout -Source 'snapshot'
                Assert-Equal $term ([string] $parsed.taxonomy.author[0])
            }
            finally {
                Remove-Item Env:TRACEFOLD_FAKE_SUPPORT_ANY_TOKEN -ErrorAction SilentlyContinue
                Remove-Item Env:TRACEFOLD_FAKE_SUPPORT_SNAPSHOT_LEAD_BYTE -ErrorAction SilentlyContinue
                Remove-UiTestTree -Path $base
            }
        }
    }
        @{
            Name = 'stdin: an exited reader is recorded exactly as the runtime reports it'
            Body = {
                # The reader is a real cmd.exe that has already exited with native code 0. Writing
                # to its input pipe is then a real broken-pipe error, not an injected one: the
                # production sender is the same function the support wrapper calls, and it must
                # record the stage, the exception type/HResult and finish inside its own budget.
                # A still-live child closing its read end is explicitly not covered here.
                $start = New-Object System.Diagnostics.ProcessStartInfo
                $start.FileName = $env:ComSpec
                $start.Arguments = '/d /c exit 0'
                $start.UseShellExecute = $false
                $start.CreateNoWindow = $true
                $start.RedirectStandardInput = $true
                $start.RedirectStandardOutput = $true
                $start.RedirectStandardError = $true
                $process = New-Object System.Diagnostics.Process
                $process.StartInfo = $start
                try {
                    [void] $process.Start()
                    $null = $process.StandardOutput.ReadToEndAsync()
                    $null = $process.StandardError.ReadToEndAsync()
                    Assert-True ($process.WaitForExit(15000)) 'the reader must exit before the write'
                    Assert-Equal 0 $process.ExitCode 'the reader native code must be 0'
                    $detail = [ordered] @{
                        stage = 'none'; writeCompleted = $null; writeFault = ''; writeHResult = $null
                        flushCompleted = $null; flushFault = ''; flushHResult = $null
                        closeState = 'not-attempted'
                    }
                    $budget = [System.Diagnostics.Stopwatch]::StartNew()
                    $payload = ('x' * 1048576)
                    $module = Get-Module -Name 'TraceFold.UiAcceptance.Workflows'
                    Assert-True ($null -ne $module) 'the workflows module must be loaded'
                    $clock = [System.Diagnostics.Stopwatch]::StartNew()
                    $state = & $module {
                        param($Process, $Payload, $TimeoutMs, $Budget, $Detail)
                        Send-UiAcceptanceStandardInput -Process $Process -StandardInput $Payload `
                            -TimeoutMs $TimeoutMs -Budget $Budget -Detail $Detail
                    } $process $payload 5000 $budget $detail
                    $clock.Stop()
                    # The two runtimes do not make the same observation, and this test records the
                    # real one instead of forcing a single story: .NET (PS 7) surfaces the broken
                    # pipe at the write stage, while .NET Framework (Windows PS 5.1) accepts the
                    # bytes after the reader exited - a measured platform difference, not an
                    # injected fault. Both hosts must stay inside the budget and keep a record
                    # that agrees with the state word.
                    Write-Host ('stdin reader observation: host=' + $PSVersionTable.PSEdition + ' state=' + [string] $state +
                        ' elapsedMs=' + $clock.ElapsedMilliseconds)
                    Assert-True ($clock.ElapsedMilliseconds -lt 5000) ('the send must stay inside its budget: ' + $clock.ElapsedMilliseconds)
                    if ($PSVersionTable.PSEdition -eq 'Core') {
                        Assert-Equal 'failed' ([string] $state) 'the broken pipe must be recorded as failed'
                        $fault = [string] $detail.writeFault + [string] $detail.flushFault
                        Assert-True ($fault.Length -gt 0) 'the real fault type and message must be recorded'
                        Assert-True ($null -ne $detail.writeHResult -or $null -ne $detail.flushHResult) 'the HResult must be recorded'
                        Assert-Equal 'released-raw' ([string] $detail.closeState) 'a failed send releases the raw stream'
                    }
                    else {
                        # This runtime keeps the write side usable after the reader exited; no fault
                        # is raised, so nothing may be reported as one.
                        Assert-Equal 'written' ([string] $state) 'this host must report what it accepted'
                        Assert-Equal $true ([bool] $detail.writeCompleted) 'the write stage must be recorded complete'
                        Assert-Equal $true ([bool] $detail.flushCompleted) 'the flush stage must be recorded complete'
                        Assert-Equal '' ([string] $detail.writeFault + [string] $detail.flushFault) 'no fault was raised, so none may be recorded'
                        Assert-Equal 'closed' ([string] $detail.closeState) 'a written send closes its side'
                    }
                }
                finally { $process.Dispose() }
            }
        }

    # --- stdin and wait budget counter-examples ------------------------------------------------
    # Each fixture is generated here, in ASCII, under the case's own temporary directory: no
    # shared fixture file grows, and every child is one this test started and still holds.
    @{
        Name = 'stdin: a consumer that reads the payload completes the write and reports what it read'
        Body = {
            $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-wf-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($base)
            try {
                $sink = Join-Path $base 'read-count.txt'
                $stub = Join-Path $base 'read-all.ps1'
                # A controlled consumer: its only input action is to read standard input to the
                # end and record how many characters arrived. The wrapper's write and the child's
                # consumption stay two separate facts.
                [IO.File]::WriteAllText($stub, @'
$sink = ''
for ($i = 0; $i -lt $args.Count - 1; $i++) { if ($args[$i] -eq '--sink') { $sink = [string] $args[$i + 1] } }
$text = [Console]::In.ReadToEnd()
if ($sink -ne '') { [IO.File]::WriteAllText($sink, [string] $text.Length) }
'@, [Text.UTF8Encoding]::new($false))
                $payload = (('C' * 64) + "`n")
                $clock = [Diagnostics.Stopwatch]::StartNew()
                $result = Invoke-UiAcceptanceSupportCommand -SupportPath $stub `
                    -Arguments @('--sink', $sink) -StandardInput $payload -TimeoutMs 8000
                $clock.Stop()
                $d = $result.StdinDetail
                Write-Host ('stdin-facts ' + ([ordered] @{
                    case = 'controlled-consumer'
                    state = [string] $result.StdinState
                    exitCode = $result.ExitCode
                    elapsedMs = $clock.ElapsedMilliseconds
                    stage = [string] $d.stage
                    writeCompleted = $d.writeCompleted
                    flushCompleted = $d.flushCompleted
                    closeState = [string] $d.closeState
                    sinkExists = [IO.File]::Exists($sink)
                } | ConvertTo-Json -Compress))
                Assert-Equal 0 $result.ExitCode
                Assert-Equal 'written' ([string] $result.StdinState)
                Assert-True ([IO.File]::Exists($sink)) 'the consumer reported what it read'
                $read = [int] ([IO.File]::ReadAllText($sink))
                Assert-Equal $payload.Length $read 'the consumer read exactly the payload that was written'
                Assert-True ($clock.ElapsedMilliseconds -lt 8000) 'the call returned inside its one budget'
            }
            finally { Remove-UiTestTree -Path $base }
        }
    },
    @{
        Name = 'stdin: a child that never reads makes the write stage itself time out'
        Body = {
            $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-wf-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($base)
            try {
                $support = Join-Path $env:SystemRoot 'System32\ping.exe'
                $clock = [Diagnostics.Stopwatch]::StartNew()
                $message = ''
                $carried = $null
                $termination = $null
                $threw = $false
                try {
                    # Far beyond a pipe buffer, so the write cannot complete into the buffer and
                    # the timeout is taken by the write/flush stage, not by the exit wait.
                    $null = Invoke-UiAcceptanceSupportCommand -SupportPath $support `
                        -Arguments @('-n', '60', '127.0.0.1') -StandardInput ('D' * 200000) -TimeoutMs 1200
                }
                catch {
                    $threw = $true
                    $message = [string] $_.Exception.Message
                    if ($null -ne $_.Exception.Data -and $_.Exception.Data.Contains('TracefoldStdinDetail')) {
                        $carried = $_.Exception.Data['TracefoldStdinDetail']
                    }
                    if ($null -ne $_.Exception.Data -and $_.Exception.Data.Contains('TracefoldTermination')) {
                        $termination = $_.Exception.Data['TracefoldTermination']
                    }
                }
                $clock.Stop()
                Assert-True $threw 'the write stage refused to wait forever'
                Assert-True ($message -match 'did not accept standard input') 'the refusal names the input stage'
                Assert-True ($null -ne $carried) 'the timeout did not carry the stdin record'
                Assert-True ($null -ne $termination) 'the timeout did not carry bounded termination facts'
                Assert-True ($null -ne $carried.termination) 'the stdin record did not retain the termination facts'
                Write-Host ('stdin-facts ' + ([ordered] @{
                    case = 'write-stage-timeout'
                    stage = [string] $carried.stage
                    writeCompleted = $carried.writeCompleted
                    writeFault = [string] $carried.writeFault
                    writeHResult = $carried.writeHResult
                    flushCompleted = $carried.flushCompleted
                    closeState = [string] $carried.closeState
                    killRequested = $termination.requested
                    killReturned = $termination.returned
                    killError = [string] $termination.error
                    scope = [string] $termination.scope
                    exitObserved = $termination.exitObserved
                    streamsCompleted = $termination.streamsCompleted
                    stdoutCompleted = $termination.stdoutCompleted
                    stderrCompleted = $termination.stderrCompleted
                    stdoutDrainObserved = $termination.stdoutDrainObserved
                    stderrDrainObserved = $termination.stderrDrainObserved
                    streamError = [string] $termination.streamError
                    terminationElapsedMs = $termination.elapsedMs
                    elapsedMs = $clock.ElapsedMilliseconds
                } | ConvertTo-Json -Compress))
                Assert-Equal $false ([bool] $carried.writeCompleted) 'the failed write must remain recorded as incomplete'
                Assert-Equal $true ([bool] $termination.requested) 'the wrapper did not request termination'
                Assert-Equal $true ([bool] $termination.returned) 'the wrapper termination request did not return'
                Assert-True ([object]::ReferenceEquals($termination, $carried.termination)) 'the stdin record must carry the same wrapper observation'
                Assert-True ([string] $termination.scope -in @('process-tree', 'process')) 'the termination scope was not recorded'
                Assert-Equal $true ([bool] $termination.exitObserved) 'the child exit was not observed within the finite wait'
                Assert-Equal $true ([bool] $termination.streamsCompleted) 'both output reads did not complete after termination'
                Assert-Equal $true ([bool] $termination.stdoutCompleted) 'stdout did not reach a completion state'
                Assert-Equal $true ([bool] $termination.stderrCompleted) 'stderr did not reach a completion state'
                Assert-Equal $true ([bool] $termination.stdoutDrainObserved) 'stdout completed without a successful drain'
                Assert-Equal $true ([bool] $termination.stderrDrainObserved) 'stderr completed without a successful drain'
                Assert-Equal '' ([string] $termination.streamError) 'stream closure produced an error'
                Assert-True ([int] $termination.elapsedMs -ge 0 -and [int] $termination.elapsedMs -lt 5000) 'termination stayed outside its finite budget'
                Assert-True ($clock.ElapsedMilliseconds -lt 8000) 'the refusal happened inside a bounded window'
            }
            finally { Remove-UiTestTree -Path $base }
        }
    },
    @{
        Name = 'stdin: a child that never exits is bounded on the exit wait, not on the write'
        Body = {
            $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-wf-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($base)
            try {
                $support = Join-Path $env:SystemRoot 'System32\ping.exe'
                $clock = [Diagnostics.Stopwatch]::StartNew()
                $message = ''
                $termination = $null
                $threw = $false
                try {
                    # No input at all: the stdin stage is skipped, so this can only fail on exit.
                    $null = Invoke-UiAcceptanceSupportCommand -SupportPath $support `
                        -Arguments @('-n', '60', '127.0.0.1') -TimeoutMs 1200
                }
                catch {
                    $threw = $true
                    $message = [string] $_.Exception.Message
                    if ($null -ne $_.Exception.Data -and $_.Exception.Data.Contains('TracefoldTermination')) {
                        $termination = $_.Exception.Data['TracefoldTermination']
                    }
                }
                $clock.Stop()
                Write-Host ('stdin-facts ' + ([ordered] @{
                    case = 'exit-wait-timeout'
                    termination = $termination
                    elapsedMs = $clock.ElapsedMilliseconds
                } | ConvertTo-Json -Compress -Depth 4))
                Assert-True $threw 'the exit wait refused to wait forever'
                Assert-True ($message -match 'did not finish') 'the refusal names the exit stage'
                Assert-True ($null -ne $termination) 'the exit timeout did not carry bounded termination facts'
                Assert-Equal $true ([bool] $termination.requested) 'the wrapper did not request termination'
                Assert-Equal $true ([bool] $termination.returned) 'the wrapper termination request did not return'
                Assert-True ([string] $termination.scope -in @('process-tree', 'process')) 'the termination scope was not recorded'
                Assert-Equal $true ([bool] $termination.exitObserved) 'the child exit was not observed'
                Assert-Equal $true ([bool] $termination.streamsCompleted) 'both output reads did not complete after termination'
                Assert-Equal $true ([bool] $termination.stdoutCompleted) 'stdout did not reach a completion state'
                Assert-Equal $true ([bool] $termination.stderrCompleted) 'stderr did not reach a completion state'
                Assert-Equal $true ([bool] $termination.stdoutDrainObserved) 'stdout completed without a successful drain'
                Assert-Equal $true ([bool] $termination.stderrDrainObserved) 'stderr completed without a successful drain'
                Assert-Equal '' ([string] $termination.streamError) 'stream closure produced an error'
                Assert-True ([int] $termination.elapsedMs -ge 0 -and [int] $termination.elapsedMs -lt 5000) 'termination stayed outside its finite budget'
                Assert-True ($clock.ElapsedMilliseconds -lt 8000) 'the refusal happened inside a bounded window'
            }
            finally { Remove-UiTestTree -Path $base }
        }
    },
    @{
        Name = 'stdin: a child that closes its input end records a failed write instead of hanging'
        Body = {
            $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-wf-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($base)
            try {
                $marker = Join-Path $base 'closed.txt'
                # A child that never reads: it records that it ran and exits, which closes the
                # read end while the parent is still writing. A script host launched through the
                # same interpreter is still a real process with a real pipe; the marker proves it
                # reached its own exit instead of being killed by the writer.
                $stub = Join-Path $base 'close-input.ps1'
                [IO.File]::WriteAllText($stub, @'
$marker = [string] $args[0]
[IO.File]::WriteAllText($marker, 'closed')
exit 0
'@, [Text.UTF8Encoding]::new($false))
                $clock = [Diagnostics.Stopwatch]::StartNew()
                $result = Invoke-UiAcceptanceSupportCommand -SupportPath $stub `
                    -Arguments @($marker) -StandardInput ('E' * 200000) -TimeoutMs 8000
                $clock.Stop()
                $d = $result.StdinDetail
                Write-Host ('stdin-facts ' + ([ordered] @{
                    case = 'controlled-close-input'
                    state = [string] $result.StdinState
                    exitCode = $result.ExitCode
                    elapsedMs = $clock.ElapsedMilliseconds
                    stage = [string] $d.stage
                    writeCompleted = $d.writeCompleted
                    writeFault = [string] $d.writeFault
                    writeHResult = $d.writeHResult
                    flushCompleted = $d.flushCompleted
                    closeState = [string] $d.closeState
                    markerExists = [IO.File]::Exists($marker)
                } | ConvertTo-Json -Compress))
                Assert-Equal 0 $result.ExitCode
                Assert-True ([IO.File]::Exists($marker)) 'the child reached its exit after closing the read end'
                Assert-True ($clock.ElapsedMilliseconds -lt 8000) 'the send did not consume the budget'
                # The two runtimes make different observations about the same closed pipe, and
                # this case records the real one instead of forcing a single story: .NET (PS 7)
                # raises the broken pipe at the write stage, .NET Framework accepts the bytes.
                if ($PSVersionTable.PSEdition -eq 'Core') {
                    Assert-Equal 'failed' ([string] $result.StdinState)
                    Assert-Equal 'write' ([string] $d.stage) 'the write stage is the one that failed'
                    Assert-Equal $false ([bool] $d.writeCompleted) 'the write did not report completion'
                    Assert-True (-not [string]::IsNullOrEmpty([string] $d.writeFault)) 'the fault is recorded with its type and message'
                    Assert-True ([int] $d.writeHResult -ne 0) 'the fault carries a native result'
                }
                else {
                    Assert-Equal 'written' ([string] $result.StdinState) 'this host must report what it accepted'
                    Assert-Equal '' ([string] $d.writeFault) 'no fault was raised, so none may be recorded'
                    Assert-Equal 'closed' ([string] $d.closeState) 'a written send closes its side'
                }
            }
            finally { Remove-UiTestTree -Path $base }
        }
    },
    @{
        Name = 'pipe: a child that leaves its output open is refused or bounded, and the holder is reaped by identity'
        Body = {
            $base = Join-Path ([IO.Path]::GetTempPath()) ('tracefold-wf-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($base)
            $facts = [ordered] @{
                case = 'workflows.hold-pipe'
                observed = $null
                coverage = 'pending'
                elapsedMs = $null
                bounded = $null
                exitCode = $null
                callError = ''
                identity = 'pending'
                queryError = ''
                holderAliveAtReturn = $null
                disposition = 'pending'
                reaped = $null
                killError = ''
                waitError = ''
                killRacedExit = $false
                cleanupErrorCount = 0
                cleanupErrors = ''
                cleanupQualification = 'pending'
                wrapperTermination = $null
                wrapperClosureObserved = $null
                closure = 'pending'
                terminationRequested = $null
                terminationReturned = $null
                terminationError = ''
                terminationExitObserved = $null
                safetyNet = [ordered] @{
                    attempted = $false
                    acted = $false
                    killError = ''
                    waitError = ''
                    exitedAfter = $null
                    failureCount = 0
                }
            }
            $holder = $null
            $carried = $null
            $closure = New-Object 'System.Collections.Generic.List[string]'
            try {
                $stub = Join-Path $base 'hold-pipe.ps1'
                $pidFile = Join-Path $base 'holder.pid'
                # The stub starts one holder process, records its pid, and exits. Redirecting the
                # holder's stdin is what makes CreateProcess inherit handles, so the redirected
                # stdout may stay open after the child is gone. The holder is short-lived on its
                # own, and this test kills it by the recorded pid after checking its identity.
                [IO.File]::WriteAllText($stub, @'
$out = [Console]::Out
$out.Write('{"held":true}')
$out.Flush()
$pidFile = ''
for ($i = 0; $i -lt $args.Count - 1; $i++) { if ($args[$i] -eq '--pid-file') { $pidFile = [string] $args[$i + 1] } }
$psi = [Diagnostics.ProcessStartInfo]::new((Join-Path $env:SystemRoot 'System32\ping.exe'))
$psi.Arguments = '-n 10 127.0.0.1'
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.CreateNoWindow = $true
$holder = [Diagnostics.Process]::Start($psi)
$holder.StandardInput.Close()
$record = [ordered] @{ pid = $holder.Id; startUtcTicks = $null; image = ''; imageState = 'unreadable' }
try { $record.startUtcTicks = $holder.StartTime.ToUniversalTime().Ticks } catch { }
try {
    $record.image = [string] $holder.MainModule.FileName
    $record.imageState = 'captured'
}
catch { }
if ($pidFile -ne '') { [IO.File]::WriteAllText($pidFile, ($record | ConvertTo-Json -Compress)) }
exit 0
'@, [Text.UTF8Encoding]::new($false))
                try {
                    $clock = [Diagnostics.Stopwatch]::StartNew()
                    $holdObserved = $false
                    $callError = ''
                    $result = $null
                    # Only the command call is inside this try: an assertion failure must never be
                    # mistaken for the open-pipe refusal this case is looking for.
                    try {
                        $result = Invoke-UiAcceptanceSupportCommand -SupportPath $stub `
                            -Arguments @('theme', 'restore', '--pid-file', $pidFile) -TimeoutMs 2000
                    }
                    catch {
                        $holdObserved = $true
                        $callError = [string] $_.Exception.Message
                        # The wrapper's own record of the termination it requested, if it attached
                        # one. This is an observation, not a conclusion: it says what was asked and
                        # what the call returned, not that a child of the child ended.
                        $carried = $null
                        if ($null -ne $_.Exception.Data -and $_.Exception.Data.Contains('TracefoldTermination')) {
                            $carried = $_.Exception.Data['TracefoldTermination']
                        }
                    }
                    $clock.Stop()
                    $facts.observed = $holdObserved
                    # A normal return with a live holder means the hold branch was not exercised on
                    # this host. That is a coverage fact, not a pass for the counter-example.
                    $facts.coverage = $(if ($holdObserved) { 'observed' } else { 'unobserved' })
                    $facts.elapsedMs = [long] $clock.ElapsedMilliseconds
                    $facts.bounded = ([long] $clock.ElapsedMilliseconds -lt 12000)
                    $facts.exitCode = $(if ($null -eq $result) { $null } else { [long] $result.ExitCode })
                    $facts.callError = $callError
                    if ($null -ne $carried) {
                        $facts.terminationRequested = [bool] $carried.requested
                        $facts.terminationReturned = $carried.returned
                        $facts.terminationError = [string] $carried.error
                        $facts.terminationExitObserved = $carried.exitObserved
                        # Freeze the wrapper's own observations before any test-owned cleanup.
                        # A later holder kill must never turn this into wrapper closure evidence.
                        $facts.wrapperTermination = $carried
                        $facts.wrapperClosureObserved = ($carried.exitObserved -eq $true -and
                            $carried.streamsCompleted -eq $true -and
                            $carried.stdoutCompleted -eq $true -and $carried.stderrCompleted -eq $true -and
                            $carried.stdoutDrainObserved -eq $true -and $carried.stderrDrainObserved -eq $true)
                    }

                    # The record names the exact object the fixture started. It is matched on birth
                    # ticks and image before anything is done to it, so a reused pid can never turn
                    # into a kill of a stranger, and a failed query is never read as absence.
                    $record = $null
                    if ([IO.File]::Exists($pidFile)) {
                        try { $record = Get-Content -LiteralPath $pidFile -Raw | ConvertFrom-Json }
                        catch { $record = $null }
                    }
                    if ($null -eq $record -or [int] $record.pid -le 0) {
                        $facts.identity = 'missing'
                        $closure.Add('identity: the fixture did not record a usable holder record')
                    }
                    elseif ($null -eq $record.startUtcTicks) {
                        # Without birth ticks the object cannot be identified, so it is not touched
                        # and no closure is claimed for it.
                        $facts.identity = 'record-incomplete'
                        $closure.Add('identity: the fixture record has no birth ticks')
                    }
                    else {
                        $candidate = $null
                        try { $candidate = [Diagnostics.Process]::GetProcessById([int] $record.pid) }
                        catch [ArgumentException] {
                            # .NET reports a process that is not running with ArgumentException. A
                            # vanished holder is a fact; a failed query is a different fact.
                            $facts.identity = 'gone'
                        }
                        catch {
                            $facts.identity = 'query-error'
                            $facts.queryError = $_.Exception.GetType().Name + ': ' + [string] $_.Exception.Message
                        }
                        if ($null -ne $candidate) {
                            $birthMatches = $false
                            try {
                                $birthMatches = ($candidate.StartTime.ToUniversalTime().Ticks -eq [long] $record.startUtcTicks)
                            }
                            catch { $birthMatches = $false }
                            $imageMatches = $false
                            if ([string] $record.imageState -eq 'captured') {
                                try {
                                    $imageMatches = [string]::Equals(
                                        [string] $candidate.MainModule.FileName, [string] $record.image,
                                        [StringComparison]::OrdinalIgnoreCase)
                                }
                                catch { $imageMatches = $false }
                            }
                            if (-not $birthMatches) {
                                $facts.identity = 'birth-mismatch'
                                $candidate.Dispose()
                            }
                            elseif ([string] $record.imageState -cne 'captured') {
                                # Birth ticks are the identity here; the image was unreadable when the
                                # holder was created, so it cannot be matched and is not invented.
                                $facts.identity = 'matched-birth-only'
                                $holder = $candidate
                            }
                            elseif (-not $imageMatches) {
                                $facts.identity = 'image-mismatch'
                                $candidate.Dispose()
                            }
                            else {
                                $facts.identity = 'matched'
                                $holder = $candidate
                            }
                        }
                    }
                    if ($null -ne $holder) {
                        try { $facts.holderAliveAtReturn = [bool] (-not $holder.HasExited) }
                        catch { $facts.holderAliveAtReturn = $null }
                    }

                    # Closure. Only an object whose identity matched is acted on; anything else is
                    # left untouched and this case does not claim to have closed it.
                    $identityMatched = ($facts.identity -eq 'matched' -or $facts.identity -eq 'matched-birth-only')
                    # The refusal text says the pipes stayed open; it does not report what the
                    # wrapper's tree kill did. Treating that text as proof of a closure would rest
                    # on a fixed message, so an absent pid is recorded as an inference and this
                    # case stays unqualified until a real termination result is observable.
                    if ($facts.identity -eq 'gone') {
                        $facts.disposition = 'already-gone'
                        $facts.reaped = $true
                        # Two independent facts have to line up: the exact pid is absent (verified
                        # by a query that did not fail), and the wrapper reports that it requested
                        # a tree termination and the call returned. The refusal text alone, or a
                        # new field alone, would not be enough.
                        $terminationProven = ($facts.terminationRequested -eq $true -and
                            $facts.terminationReturned -eq $true -and $facts.wrapperClosureObserved -eq $true -and
                            [string] $carried.scope -eq 'process-tree')
                        $facts.closure = $(if ($terminationProven) { 'wrapper-termination-then-absent' } else { 'tree-kill-inferred' })
                        if (-not $terminationProven) {
                            $closure.Add('closure: the pid is gone, but wrapper tree termination, child exit and both drains were not all observed')
                        }
                    }
                    elseif ($identityMatched) {
                        if ($facts.holderAliveAtReturn -eq $true) {
                            $facts.disposition = 'killed'
                            $facts.closure = 'this-window-kill'
                            try { $holder.Kill() } catch { $facts.killError = 'kill: ' + [string] $_.Exception.Message }
                            try { [void] $holder.WaitForExit(5000) } catch { $facts.waitError = 'wait: ' + [string] $_.Exception.Message }
                            $reapedNow = $false
                            try { $reapedNow = [bool] $holder.HasExited } catch { $reapedNow = $false }
                            $facts.reaped = $reapedNow
                            # Losing the kill to a process that was already exiting is a fact, not
                            # an error; it is recorded and not turned into a fabricated failure.
                            $facts.killRacedExit = ($facts.killError -ne '' -and $reapedNow)
                            if (-not $reapedNow) {
                                if ($facts.killError -ne '') { $closure.Add($facts.killError) }
                                if ($facts.waitError -ne '') { $closure.Add($facts.waitError) }
                                $closure.Add('the holder did not exit within 5000 ms')
                            }
                        }
                        elseif ($facts.holderAliveAtReturn -eq $false) {
                            $facts.disposition = 'already-exited'
                            $facts.closure = 'observed-exit'
                            $facts.reaped = $true
                        }
                        else {
                            # Liveness unreadable: no kill, and no claim about the exit.
                            $facts.disposition = 'unknown-not-touched'
                            $facts.reaped = $false
                            $closure.Add('liveness: the holder state could not be read, so nothing was done to it')
                        }
                    }
                    else {
                        $facts.disposition = 'unknown-not-touched'
                        $facts.reaped = $false
                        $facts.closure = 'not-claimed'
                        $closure.Add('identity: ' + $facts.identity + ' - the recorded holder was not touched')
                    }

                    # This qualification is only for test-owned holder cleanup. Wrapper closure
                    # remains the separate, pre-cleanup observation above, even if cleanup succeeds.
                    # An untouched object with an empty error list is not a cleanup qualification.
                    $goneProven = ($facts.identity -eq 'gone' -and $facts.closure -eq 'wrapper-termination-then-absent')
                    $qualifiedHere = (($identityMatched -or $goneProven) -and $facts.reaped -eq $true -and $closure.Count -eq 0)
                    $facts.cleanupQualification = $(if ($qualifiedHere) { 'qualified' } else { 'unqualified' })
                    $facts.cleanupErrorCount = $closure.Count
                    $facts.cleanupErrors = ($closure -join '; ')
                    $script:workflowHoldObserved = $holdObserved
                    $script:workflowHoldCoverage = $facts.coverage
                    $script:workflowHoldFacts = $facts

                    # The last-resort step also runs here, before the final assertions, so its own
                    # result is judged by this case instead of being buried after a failure.
                    $facts.safetyNet.attempted = $true
                    if ($null -ne $holder -and $facts.reaped -ne $true) {
                        $facts.safetyNet.acted = $true
                        try { $holder.Kill() } catch { $facts.safetyNet.killError = 'kill: ' + [string] $_.Exception.Message }
                        try { [void] $holder.WaitForExit(2000) } catch { $facts.safetyNet.waitError = 'wait: ' + [string] $_.Exception.Message }
                    }
                    if ($null -ne $holder) {
                        try { $facts.safetyNet.exitedAfter = [bool] $holder.HasExited }
                        catch { $facts.safetyNet.exitedAfter = $null }
                        if ($facts.safetyNet.exitedAfter -ne $true) { $facts.safetyNet.failureCount = 1 }
                    }

                    Assert-True ([bool] $facts.bounded) ('the open pipe did not extend the wait without a bound: ' + [string] $facts.elapsedMs + ' ms')
                    if ($holdObserved) {
                        Assert-True ($callError -match 'left its output streams open') ('the refusal names the open pipe: ' + $callError)
                        Assert-True ($null -ne $carried) 'the refusal must carry the wrapper termination observations'
                        Assert-Equal $true ([bool] $facts.terminationExitObserved) 'the wrapper did not observe its child exit'
                        if ($facts.holderAliveAtReturn -eq $true) {
                            Assert-Equal $false ([bool] $facts.wrapperClosureObserved) 'test cleanup must not qualify a live inherited pipe as wrapper closure'
                        }
                    }
                    else {
                        Assert-True ($facts.exitCode -eq 0) ('a normal return keeps exit code 0, saw ' + [string] $facts.exitCode)
                        Assert-True ($facts.holderAliveAtReturn -eq $true) 'an unobserved hold must still show a live holder at return'
                    }
                    Assert-Equal 'qualified' $facts.cleanupQualification ('test-owned holder cleanup completed in bounds; this is not wrapper closure: ' + $facts.cleanupErrors)
                    Assert-Equal 0 ([int] $facts.safetyNet.failureCount) ('the last-resort closure did not end: ' + $facts.safetyNet.killError + ' ' + $facts.safetyNet.waitError)
                }
                finally {
                    # Last resort, and only for the one object already matched above: never a
                    # lookup, never a name scan, never a second object. A failure here cannot
                    # replace an in-flight result, so it is recorded and printed instead.
                    if ($null -ne $holder -and $facts.safetyNet.attempted -eq $false) {
                        $facts.safetyNet.attempted = $true
                        $aliveNow = $null
                        try { $aliveNow = [bool] (-not $holder.HasExited) } catch { $aliveNow = $null }
                        if ($aliveNow -eq $true) {
                            $facts.safetyNet.acted = $true
                            try { $holder.Kill() } catch { $facts.safetyNet.killError = 'kill: ' + [string] $_.Exception.Message }
                            try { [void] $holder.WaitForExit(2000) } catch { $facts.safetyNet.waitError = 'wait: ' + [string] $_.Exception.Message }
                        }
                        try { $facts.safetyNet.exitedAfter = [bool] $holder.HasExited }
                        catch { $facts.safetyNet.exitedAfter = $null }
                        if ($facts.safetyNet.exitedAfter -ne $true) { $facts.safetyNet.failureCount = 1 }
                    }
                    if ($null -ne $holder) { try { $holder.Dispose() } catch { } }
                    # The suite prints only PASS/FAIL and does not read module memory, so the facts
                    # go to the attempt's own captured stream, on the failure path too.
                    try {
                        Write-Host ('holdpipe-facts ' + ($facts | ConvertTo-Json -Compress -Depth 5))
                    }
                    catch {
                        Write-Host 'holdpipe-facts {"case":"workflows.hold-pipe","serialisation":"failed"}'
                    }
                }
            }
            finally { Remove-UiTestTree -Path $base }
        }
    }
    )
}
