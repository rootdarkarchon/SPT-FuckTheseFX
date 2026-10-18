param(
    [Parameter(Mandatory)][string[]]$BaselineCapture,
    [string[]]$CandidateCapture = @(),
    [string]$OutputCsv
)
$ErrorActionPreference = 'Stop'
$invariant = [Globalization.CultureInfo]::InvariantCulture
function Number([string]$value) { [double]::Parse($value, $invariant) }
function Read-Capture([string]$path, [string]$condition) {
    $capture = Get-Content -LiteralPath (Join-Path $path 'capture.txt') -Raw
    if ($capture -notmatch '(?m)^Outcome: menu-arrived\r?$' -or $capture -notmatch '(?m)^Epoch source: early preloader Initialize\r?$') { throw "Missing successful early-to-menu coverage: $path" }
    if ($capture -notmatch 'Process age at observer ms \(UTC estimate\): ([0-9.]+)') { throw "Missing process-age estimate: $path" }
    $offset = Number $Matches[1]
    if ($capture -notmatch 'Dropped spans \(global/per-stage cap\): 0\r?\n' -or $capture -notmatch 'Item observer errors: 0\r?\n') { throw "Incomplete/error capture: $path" }
    $stages = @(Import-Csv -LiteralPath (Join-Path $path 'stages.csv'))
    $markers = @(Import-Csv -LiteralPath (Join-Path $path 'markers.csv'))
    $early = @(Import-Csv -LiteralPath (Join-Path $path 'early-stages.csv'))
    $awake = @($stages | Where-Object { $_.stage -eq 'application.awake' -and $_.outcome -eq 'completed' })
    $menu = @($markers | Where-Object marker -eq 'menu.show.returned')
    $loaded = @($markers | Where-Object marker -eq 'application.loaded.returned')
    $ftfx = @($early | Where-Object { $_.stage -eq 'chainloader.plugin-log-interval' -and $_.detail -match '^Fuck These FX ' -and $_.outcome -eq 'completed' })
    if ($awake.Count -ne 1 -or $menu.Count -ne 1 -or $loaded.Count -ne 1 -or $ftfx.Count -ne 1) { throw "Ambiguous/missing startup boundaries: $path" }
    $preAwake = ($offset + (Number $awake[0].start_ms)) / 1000
    $menuTime = ($offset + (Number $menu[0].observer_ms)) / 1000
    $loadedTime = ($offset + (Number $loaded[0].observer_ms)) / 1000
    if ($preAwake -lt 0 -or $menuTime -lt $preAwake -or $loadedTime -lt $preAwake) { throw "Inconsistent startup clock: $path" }
    [pscustomobject]@{
        Condition = $condition
        Capture = Split-Path $path.TrimEnd('\','/') -Leaf
        FtfxVersion = $ftfx[0].detail.Substring('Fuck These FX '.Length)
        PreAwakeSeconds = $preAwake
        MenuShowSeconds = $menuTime
        ApplicationLoadedSeconds = $loadedTime
        AwakeToMenuSeconds = $menuTime - $preAwake
        FtfxIntervalSeconds = (Number $ftfx[0].duration_ms) / 1000
    }
}
function Median($values) {
    $sorted = @($values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}
$rows = @(
    foreach ($path in $BaselineCapture) { Read-Capture $path 'baseline' }
    foreach ($path in $CandidateCapture) { Read-Capture $path 'candidate' }
)
if ($OutputCsv) { $rows | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation }
$rows | Format-Table Condition,Capture,FtfxVersion,PreAwakeSeconds,MenuShowSeconds,ApplicationLoadedSeconds,AwakeToMenuSeconds,FtfxIntervalSeconds -AutoSize
$summary = @(
    foreach ($group in ($rows | Group-Object Condition)) {
        if ($group.Count -lt 3) { Write-Warning "$($group.Name): only $($group.Count) capture(s); repeatability is not established." }
        [pscustomobject]@{
            Condition = $group.Name
            Runs = $group.Count
            PreAwakeSeconds = Median $group.Group.PreAwakeSeconds
            MenuShowSeconds = Median $group.Group.MenuShowSeconds
            ApplicationLoadedSeconds = Median $group.Group.ApplicationLoadedSeconds
            AwakeToMenuSeconds = Median $group.Group.AwakeToMenuSeconds
            FtfxIntervalSeconds = Median $group.Group.FtfxIntervalSeconds
        }
    }
)
$summary | Format-Table -AutoSize
Write-Output 'Times are process-age estimates and inclusive wall intervals. Compare matching profile/mods/settings and launch warmth; menu boundaries do not prove frame presentation.'
