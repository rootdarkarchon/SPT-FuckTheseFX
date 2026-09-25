param([string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
foreach ($name in @('UnityEngine.CoreModule.dll','UnityEngine.dll')) { Add-Type -Path (Join-Path $SptInstallPath "EscapeFromTarkov_Data\Managed\$name") }
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx\core\BepInEx.dll')
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'bin\Release\netstandard2.1\FuckTheseFX.dll'))
$plugin = $assembly.GetType('FuckTheseFX.Plugin', $true)
$bind = $plugin.GetMethod('BindSettings', [Reflection.BindingFlags]'Static,NonPublic')
$import = $plugin.GetMethod('ImportLegacySettings', [Reflection.BindingFlags]'Static,NonPublic')
$testRoot = Join-Path $repo 'obj\ConfigurationChecks'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$path = Join-Path $testRoot 'fresh.cfg'
[IO.File]::WriteAllText($path, '')
$config = [BepInEx.Configuration.ConfigFile]::new($path,$false)
$config.SaveOnConfigSet = $false
$bindArgs = [object[]]::new(1); $bindArgs[0] = $config; $bind.Invoke($null, $bindArgs)
$expected = @{
    'Ambient Highlight/Enabled' = 'False'
    'Ultimate Bloom/Bloom input scale' = '0.67'
    'Ultimate Bloom/Enabled' = 'True'
    'Ultimate Bloom/Use native resolution' = 'False'
    'Distant Shadows/Resolution' = 'Half'
    'Distant Shadows/Enabled' = 'True'
    'Distant Shadows/Adjust filter samples' = 'True'
    'Distant Shadows/Quarter filter samples' = '25'
    'Distant Shadows/Half filter samples' = '12'
    'Distant Shadows/Full filter samples' = '8'
}
if ($config.Count -ne 10) { throw 'Unexpected setting count' }
foreach ($definition in $config.Keys) {
    $entry = $config[$definition]
    $name = "$($definition.Section)/$($definition.Key)"
    if (-not $expected.ContainsKey($name) -or $entry.DefaultValue.ToString() -ne $expected[$name]) { throw "Wrong default: $name" }
    $advanced = @($entry.Description.Tags | Where-Object { $_.GetType().GetField('IsAdvanced').GetValue($_) -eq $true }).Count -eq 1
    if ($advanced -ne ($definition.Key -match '^(Adjust filter samples|(Quarter|Half|Full) filter samples)$')) { throw "Incorrect advanced visibility: $name" }
}
function Entry($section,$key) { $config[[BepInEx.Configuration.ConfigDefinition]::new($section,$key)] }
foreach ($key in @('Quarter filter samples','Half filter samples','Full filter samples')) {
    $entry = Entry 'Distant Shadows' $key
    if ($entry.Description.AcceptableValues.Clamp([int]0) -ne 1 -or $entry.Description.AcceptableValues.Clamp([int]26) -ne 25) { throw 'Sample bounds changed' }
}
$scale = Entry 'Ultimate Bloom' 'Bloom input scale'
if ($scale.Description.AcceptableValues.Clamp([single].1) -ne .25 -or $scale.Description.AcceptableValues.Clamp([single]2) -ne 1) { throw 'Bloom bounds changed' }
$legacyPath = Join-Path $testRoot 'legacy.cfg'
[IO.File]::WriteAllText($legacyPath, "[Ultimate Bloom]`nInput resolution scale = 0.7`nUse native resolution = true`n[Distant Shadows]`nMask resolution = Half`nAdjust filter samples = false`nQuarter filter samples = 19`nHalf filter samples = 11`nFull filter samples = 6`n[Ambient Highlight]`nEnabled = false`n[Ambient Occlusion]`nBackend = Vanilla`n")
$before = (Get-FileHash -LiteralPath $legacyPath).Hash
$importArgs = [object[]]::new(2); $importArgs[0] = $config; $importArgs[1] = [string]$legacyPath; $import.Invoke($null, $importArgs)
if ((Get-FileHash -LiteralPath $legacyPath).Hash -ne $before) { throw 'Migration modified the old config' }
if ((Entry 'Ambient Highlight' 'Enabled').BoxedValue -or -not (Entry 'Ultimate Bloom' 'Use native resolution').BoxedValue -or [Math]::Abs($scale.BoxedValue - .7) -gt .00001) { throw 'Highlight/bloom migration failed' }
foreach ($pair in @(@('Resolution','Half'),@('Adjust filter samples','False'),@('Quarter filter samples','19'),@('Half filter samples','11'),@('Full filter samples','6'))) {
    if ((Entry 'Distant Shadows' $pair[0]).BoxedValue.ToString() -ne $pair[1]) { throw "Shadow migration failed: $($pair[0])" }
}
$config.Save()
$saved = [BepInEx.Configuration.ConfigFile]::new($path,$false)
$saved.SaveOnConfigSet = $false
$bindArgs[0] = $saved; $bind.Invoke($null,$bindArgs)
foreach ($definition in $config.Keys) { if ($saved[$definition].GetSerializedValue() -ne $config[$definition].GetSerializedValue()) { throw 'Saved preference did not survive reload' } }
Write-Output 'Verified ten FX settings, six normal controls, defaults, bounds, saved preferences and read-only legacy import.'

$enum = $assembly.GetType('FuckTheseFX.Source.DistantShadows.DistantShadowMaskResolution', $true)
if ([Enum]::GetNames($enum) -join ',' -ne 'Quarter,Half,Full') { throw 'Unexpected resolution option' }
# Renamed keys in existing FX configs migrate, but explicit new keys always win.
$renamePath = Join-Path $testRoot 'renamed.cfg'
[IO.File]::WriteAllText($renamePath, "[Ultimate Bloom]`nInput resolution scale = 0.8`n[Distant Shadows]`nMask resolution = Full`n")
$renamed = [BepInEx.Configuration.ConfigFile]::new($renamePath,$false)
$renamed.SaveOnConfigSet = $false
$bindArgs[0] = $renamed; $bind.Invoke($null,$bindArgs)
if ([Math]::Abs($renamed[[BepInEx.Configuration.ConfigDefinition]::new('Ultimate Bloom','Bloom input scale')].BoxedValue - .8) -gt .00001 -or $renamed[[BepInEx.Configuration.ConfigDefinition]::new('Distant Shadows','Resolution')].BoxedValue.ToString() -ne 'Full') { throw 'Renamed settings migration failed' }
[IO.File]::WriteAllText($renamePath, "[Ultimate Bloom]`nInput resolution scale = 0.8`nBloom input scale = 0.3`n[Distant Shadows]`nMask resolution = Full`nResolution = Quarter`n")
$renamed = [BepInEx.Configuration.ConfigFile]::new($renamePath,$false)
$renamed.SaveOnConfigSet = $false
$bindArgs[0] = $renamed; $bind.Invoke($null,$bindArgs)
if ([Math]::Abs($renamed[[BepInEx.Configuration.ConfigDefinition]::new('Ultimate Bloom','Bloom input scale')].BoxedValue - .3) -gt .00001 -or $renamed[[BepInEx.Configuration.ConfigDefinition]::new('Distant Shadows','Resolution')].BoxedValue.ToString() -ne 'Quarter') { throw 'Old keys overrode explicit new settings' }
Write-Output 'Verified Quarter/Half/Full enum and renamed key migration with new-key precedence.'