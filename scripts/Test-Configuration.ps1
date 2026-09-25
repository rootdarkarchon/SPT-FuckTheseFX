param([string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
foreach ($name in @('UnityEngine.CoreModule.dll','UnityEngine.dll')) { Add-Type -Path (Join-Path $SptInstallPath "EscapeFromTarkov_Data\Managed\$name") }
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx\core\BepInEx.dll')
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'bin\Release\netstandard2.1\FuckTheseFX.dll'))
$plugin = $assembly.GetType('FuckTheseFX.Plugin', $true)
$bind = $plugin.GetMethod('BindSettings', [Reflection.BindingFlags]'Static,NonPublic')
$testRoot = Join-Path $repo 'obj\ConfigurationChecks'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$path = Join-Path $testRoot 'fresh.cfg'
[IO.File]::WriteAllText($path, '')
$config = [BepInEx.Configuration.ConfigFile]::new($path,$false)
$config.SaveOnConfigSet = $false
$bindArgs = [object[]]::new(1); $bindArgs[0] = $config; $bind.Invoke($null, $bindArgs)
$expected = @{
    'XeGTAO/Enabled' = 'True'
    'XeGTAO/FinalPower' = '1.5'
    'XeGTAO/EffectRadius' = '1'
    'XeGTAO/RadiusMultiplier' = '1.457'
    'XeGTAO/FalloffRange' = '0.615'
    'XeGTAO/SampleDistributionPower' = '2.5'
    'XeGTAO/SliceCount' = '0'
    'XeGTAO/SamplesPerSlice' = '0'
    'XeGTAO/ThicknessHeuristic' = '0'
    'XeGTAO/Denoise passes' = '2'
    'XeGTAO/DenoiseSharpness' = '0.4'
    'XeGTAO/UseNormals' = 'True'
    'CustomAmbient fix/Enabled' = 'True'
    'CustomAmbient fix/Strength' = '0.6'
    'CustomAmbient fix/Foliage AO strength' = '0.65'
    'Actor second AO pass/Enabled' = 'True'
    'Actor second AO pass/Radius' = '0.3'
    'Actor second AO pass/Strength' = '1.5'
    'Actor second AO pass/Response' = '0.6'
    'Actor second AO pass/Distance cutoff' = '10'
    'Debug/View' = 'Normal'
    'Debug/Depth range' = '5'
    'Debug/Depth start' = '0'
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
if ($config.Count -ne 33) { throw 'Unexpected setting count' }
foreach ($definition in $config.Keys) {
    $entry = $config[$definition]
    $name = "$($definition.Section)/$($definition.Key)"
    if (-not $expected.ContainsKey($name) -or $entry.DefaultValue.ToString() -ne $expected[$name]) { throw "Wrong default: $name" }
    $advanced = @($entry.Description.Tags | Where-Object { $_.GetType().GetField('IsAdvanced').GetValue($_) -eq $true }).Count -eq 1
    if ($advanced -ne ($definition.Key -match '^(Adjust filter samples|(Quarter|Half|Full) filter samples)$' -or $definition.Section -in @('CustomAmbient fix','Debug') -or ($definition.Section -in @('XeGTAO','Actor second AO pass') -and $definition.Key -ne 'Enabled'))) { throw "Incorrect advanced visibility: $name" }
}
function Entry($section,$key) { $config[[BepInEx.Configuration.ConfigDefinition]::new($section,$key)] }
foreach ($key in @('Quarter filter samples','Half filter samples','Full filter samples')) {
    $entry = Entry 'Distant Shadows' $key
    if ($entry.Description.AcceptableValues.Clamp([int]0) -ne 1 -or $entry.Description.AcceptableValues.Clamp([int]26) -ne 25) { throw 'Sample bounds changed' }
}
$scale = Entry 'Ultimate Bloom' 'Bloom input scale'
if ($scale.Description.AcceptableValues.Clamp([single].1) -ne .25 -or $scale.Description.AcceptableValues.Clamp([single]2) -ne 1) { throw 'Bloom bounds changed' }
$radiusEntry = Entry 'XeGTAO' 'EffectRadius'
$radiusEntry.SetSerializedValue('0.55')
$enabledEntry = Entry 'XeGTAO' 'Enabled'
$enabledEntry.SetSerializedValue('false')
$config.Save()
$saved = [BepInEx.Configuration.ConfigFile]::new($path,$false)
$saved.SaveOnConfigSet = $false
$bindArgs[0] = $saved; $bind.Invoke($null,$bindArgs)
foreach ($definition in $config.Keys) { if ($saved[$definition].GetSerializedValue() -ne $config[$definition].GetSerializedValue()) { throw 'Saved preference did not survive reload' } }
Write-Output 'Verified 33 settings, eight normal controls, defaults, bounds and saved preferences. No AOReplacer import.'

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
