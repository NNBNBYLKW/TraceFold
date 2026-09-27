$items = [string[]]$args
if ($items.Count -eq 6 -and $items[0] -eq 'root' -and $items[1] -in @('validate','delete') -and
    $items[2] -eq '--root' -and $items[4] -eq '--token' -and $items[5] -eq 'test-token') {
    if (-not [string]::IsNullOrWhiteSpace($env:TRACEFOLD_FAKE_SUPPORT_LOG)) {
        Add-Content -LiteralPath $env:TRACEFOLD_FAKE_SUPPORT_LOG -Value $items[1]
    }
    if ($items[1] -eq 'validate') { Write-Output '{"owned":true}' }
    else { Write-Output '{"deleted":true}' }
    exit 0
}
exit 3
