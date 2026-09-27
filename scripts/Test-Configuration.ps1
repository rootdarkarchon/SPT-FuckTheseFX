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
    'Stochastic SSR/Enabled' = 'True'
    'Stochastic SSR/Sampling mode' = 'Native'
    'Stochastic SSR/Maximum iterations' = '128'
    'Stochastic SSR/Thickness' = '0.2'
    'Stochastic SSR/Limit reflection range' = 'False'
    'Stochastic SSR/Maximum distance' = '931.9296'
    'Stochastic SSR/Temporal stabilization' = 'True'
    'Stochastic SSR/Spatial filter' = 'True'
    'Stochastic SSR/History frames' = '8'
    'Stochastic SSR/Adaptive rays' = 'True' 
    'Glass Culling Fix/Enabled' = 'True'
    'Screen Space Contact GI/Enabled' = 'False'
    'Screen Space Contact GI/Strength' = '3'
    'Screen Space Contact GI/Resolution' = 'Quarter'
    'Screen Space Contact GI/Spatial filter' = 'True'
    'Screen Space Contact GI/Temporal accumulation' = 'True'
    'Screen Space Contact GI/History frames' = '16'
    'Screen Space Contact GI/Samples' = '8'
    'Screen Space Contact GI/Radius' = '0.75'
    'Screen Space Contact GI/Depth tolerance' = '0.02'
    'Screen Space Contact GI/Donor luminance limit' = '2'
    'Screen Space Contact GI/Maximum bounce fraction' = '0.2'
    'Screen Space Contact GI/Distance cutoff' = '25'
    'XeGTAO/Enabled' = 'True'
    'XeGTAO/Quality' = '3'
    'XeGTAO/FinalPower' = '1.5'
    'XeGTAO/Distance fade start' = '100'
    'XeGTAO/Distance fade end' = '250'
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
    'XeGTAO/Custom Ambient Fix Enabled' = 'True'
    'XeGTAO/Custom Ambient Fix Bent normals' = 'False'
    'XeGTAO/Custom Ambient Fix Strength' = '0.6'
    'XeGTAO/Custom Ambient Fix Foliage AO strength' = '0.65'
    'XeGTAO/Second Pass Enabled' = 'True'
    'XeGTAO/Second Pass Radius' = '0.3'
    'XeGTAO/Second Pass Strength' = '1.5'
    'XeGTAO/Second Pass Response' = '0.6'
    'XeGTAO/Second Pass Distance cutoff' = '10'
    'Debug/View' = 'Normal'
    'Debug/Bent difference gain' = '32'
    'Debug/Ambient contribution gain' = '1'
    'Debug/Depth range' = '5'
    'Debug/Depth start' = '0'
    'Debug/Screen Space Contact GI gain' = '16'
    'Debug/Foliage live preview' = 'Scene'
    'Debug/Foliage live display gain' = '1'
    'Debug/Foliage live bypass Contact GI' = 'False'
    'Debug/Foliage live bypass opaque colour' = 'False'
    'XeGTAO/Foliage extra AO strength' = '0.2'
    'Debug/Foliage live split comparison' = 'True'
    'Debug/Foliage live divider' = '0.5'
    'Ambient Highlight/Enabled' = 'False'
    'Vintage Filter/Enabled' = 'False'
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
if ($config.Count -ne 71) { throw 'Unexpected setting count' }
foreach ($definition in $config.Keys) {
    $entry = $config[$definition]
    $name = "$($definition.Section)/$($definition.Key)"
    if (-not $expected.ContainsKey($name) -or $entry.DefaultValue.ToString() -ne $expected[$name]) { throw "Wrong default: $name" }
    $advanced = @($entry.Description.Tags | Where-Object { $_.GetType().GetField('IsAdvanced').GetValue($_) -eq $true }).Count -eq 1
    if ($advanced -ne ($definition.Key -match '^(Adjust filter samples|(Quarter|Half|Full) filter samples)$' -or ($definition.Section -eq 'Stochastic SSR' -and $definition.Key -notin @('Enabled','Sampling mode')) -or $definition.Section -in @('Debug') -or ($definition.Section -in @('XeGTAO','Screen Space Contact GI') -and $definition.Key -notin @('Enabled','Quality','Second Pass Enabled')))) { throw "Incorrect advanced visibility: $name" }
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
$fadeStart = Entry 'XeGTAO' 'Distance fade start'
$fadeEnd = Entry 'XeGTAO' 'Distance fade end'
if ($fadeStart.Description.AcceptableValues.Clamp([single]-1) -ne 0 -or $fadeStart.Description.AcceptableValues.Clamp([single]1000) -ne 999 -or
    $fadeEnd.Description.AcceptableValues.Clamp([single]0) -ne 1 -or $fadeEnd.Description.AcceptableValues.Clamp([single]1001) -ne 1000) { throw 'AO fade bounds changed' }
$fadeStart.SetSerializedValue('60')
$fadeEnd.SetSerializedValue('150')
$enabledEntry = Entry 'XeGTAO' 'Enabled'
$enabledEntry.SetSerializedValue('false')
$bentEntry = Entry 'XeGTAO' 'Custom Ambient Fix Bent normals'
$bentEntry.SetSerializedValue('true')
$vintageEntry = Entry 'Vintage Filter' 'Enabled'
$vintageEntry.SetSerializedValue('true')
$qualityEntry = Entry 'XeGTAO' 'Quality'
if ($qualityEntry.Description.AcceptableValues.Clamp([int]-1) -ne 0 -or $qualityEntry.Description.AcceptableValues.Clamp([int]4) -ne 3) { throw 'Quality bounds changed' }
$qualityEntry.SetSerializedValue('1')
$bleedEnabled = Entry 'Screen Space Contact GI' 'Enabled'
$bleedEnabled.SetSerializedValue('true')
(Entry 'Screen Space Contact GI' 'Spatial filter').SetSerializedValue('false')
(Entry 'Screen Space Contact GI' 'Temporal accumulation').SetSerializedValue('false')
$historyFrames = Entry 'Screen Space Contact GI' 'History frames'
if ($historyFrames.Description.AcceptableValues.Clamp([int]1) -ne 4 -or $historyFrames.Description.AcceptableValues.Clamp([int]32) -ne 30) { throw 'Screen Space Contact GI history bounds changed' }
$historyFrames.SetSerializedValue('30')
$bleedStrength = Entry 'Screen Space Contact GI' 'Strength'
if ($bleedStrength.Description.AcceptableValues.Clamp([single]-1) -ne 0 -or $bleedStrength.Description.AcceptableValues.Clamp([single]5) -ne 4) { throw 'Screen Space Contact GI strength bounds changed' }
$bleedStrength.SetSerializedValue('2')
if ($bleedStrength.BoxedValue -ne 2) { throw 'Screen Space Contact GI strength above one was clamped' }
$bleedResolution = Entry 'Screen Space Contact GI' 'Resolution'
$bleedResolution.SetSerializedValue('Quarter')
$bleedSamples = Entry 'Screen Space Contact GI' 'Samples'
foreach ($sampleCount in @(4,6,8,16,32)) { if (-not $bleedSamples.Description.AcceptableValues.IsValid([int]$sampleCount)) { throw "Missing GI sample choice: $sampleCount" } }
if (-not $bleedSamples.Description.AcceptableValues.IsValid([int]4) -or -not $bleedSamples.Description.AcceptableValues.IsValid([int]8) -or $bleedSamples.Description.AcceptableValues.IsValid([int]5)) { throw 'Screen Space Contact GI sample choices changed' }
$bleedSamples.SetSerializedValue('32')
$bleedGain = Entry 'Debug' 'Screen Space Contact GI gain'
if ($bleedGain.Description.AcceptableValues.Clamp([single]0) -ne 1 -or $bleedGain.Description.AcceptableValues.Clamp([single]200) -ne 128) { throw 'Screen Space Contact GI debug gain bounds changed' }
$bleedGain.SetSerializedValue('32')
(Entry 'Debug' 'View').SetSerializedValue('ScreenSpaceContactGIHistory')
(Entry 'Debug' 'View').SetSerializedValue('FoliageLightingCapture')
if ((Entry 'Debug' 'View').Value.ToString() -ne 'FoliageLightingCapture' -or -not (Entry 'Debug' 'View').Description.Description.Contains('FoliageDiagnostics')) { throw 'Missing foliage capture action or instructions' }
(Entry 'Debug' 'View').SetSerializedValue('FoliageLive')
(Entry 'Debug' 'Foliage live preview').SetSerializedValue('OpaqueDifference')
(Entry 'Debug' 'Foliage live bypass Contact GI').SetSerializedValue('true')
(Entry 'Debug' 'Foliage live bypass opaque colour').SetSerializedValue('true')
(Entry 'XeGTAO' 'Foliage extra AO strength').SetSerializedValue('0.3')
$extra = Entry 'XeGTAO' 'Foliage extra AO strength'
if ($extra.Description.AcceptableValues.Clamp([single]-1) -ne 0 -or $extra.Description.AcceptableValues.Clamp([single]2) -ne 1) { throw 'Live extra AO must remain bounded 0..1' }
(Entry 'Stochastic SSR' 'Enabled').SetSerializedValue('false')
(Entry 'Stochastic SSR' 'Sampling mode').SetSerializedValue('Custom')
(Entry 'Stochastic SSR' 'Adaptive rays').SetSerializedValue('false')
$range = Entry 'Stochastic SSR' 'Maximum distance'
if ($range.Description.AcceptableValues.Clamp([single]20000) -ne 10000) { throw 'SSR distance range changed' }
$range.SetSerializedValue('500')
$config.Save()
$saved = [BepInEx.Configuration.ConfigFile]::new($path,$false)
$saved.SaveOnConfigSet = $false
$bindArgs[0] = $saved; $bind.Invoke($null,$bindArgs)
foreach ($definition in $config.Keys) { if ($saved[$definition].GetSerializedValue() -ne $config[$definition].GetSerializedValue()) { throw 'Saved preference did not survive reload' } }
Write-Output 'Verified 71 settings, defaults, bounds and saved preferences. No AOReplacer import.'

$foliagePath = Join-Path $testRoot 'foliage-promoted.cfg'
[IO.File]::WriteAllText($foliagePath, "[Debug]`nFoliage live extra AO = 0`n[XeGTAO]`nDistance fade start = 100`nDistance fade end = 250")
$foliageConfig = [BepInEx.Configuration.ConfigFile]::new($foliagePath,$false)
$foliageConfig.SaveOnConfigSet = $false
$bindArgs[0] = $foliageConfig; $bind.Invoke($null,$bindArgs); $bind.Invoke($null,$bindArgs)
$foliageConfig.Save()
if ((Get-Content $foliagePath -Raw) -match 'Foliage live extra AO' -or $foliageConfig.Count -ne 71 -or
    $foliageConfig[[BepInEx.Configuration.ConfigDefinition]::new('XeGTAO','Foliage extra AO strength')].Value -ne [single]0.2) { throw 'Retired temporary foliage setting overrode the persistent default' }
Write-Output 'Verified retired live slider removal and persistent 20% foliage default.'

$enum = $assembly.GetType('FuckTheseFX.Source.DistantShadows.DistantShadowMaskResolution', $true)
if ([Enum]::GetNames($enum) -join ',' -ne 'Quarter,Half,Full') { throw 'Unexpected resolution option' }
$performance = @{
    'Ambient Highlight' = 'no measurable impact'
    'Vintage Filter' = 'no measurable impact'
    'Ultimate Bloom' = 'positive impact when reducing input scale'
    'Distant Shadows' = 'small impact at defaults'
    'XeGTAO' = 'no impact or an improvement over vanilla HBAO'
    'Screen Space Contact GI' = 'small-medium impact at defaults'
}
foreach ($section in $performance.Keys) {
    if (-not (Entry $section 'Enabled').Description.Description.Contains($performance[$section])) { throw "Missing performance tooltip: $section" }
}
Write-Output 'Verified performance tooltips for all six feature enable toggles.'

$description = (Entry 'Glass Culling Fix' 'Enabled').Description.Description
if ($description -match 'experimental' -or $description -notmatch 'Primarily.*Streets') { throw 'Glass culling release description incorrect' }
foreach ($case in @(
    @{ Text = "[Glass Culling]`nEnabled = false"; Expected = $false },
    @{ Text = "[Glass Culling]`nEnabled = true"; Expected = $true },
    @{ Text = "[Glass Culling]`nEnabled = true`n[Glass Culling Fix]`nEnabled = false"; Expected = $false }
)) {
    $migrationPath = Join-Path $testRoot 'glass-migration.cfg'
    [IO.File]::WriteAllText($migrationPath, $case.Text)
    $migration = [BepInEx.Configuration.ConfigFile]::new($migrationPath,$false)
    $migration.SaveOnConfigSet = $false
    $bindArgs[0] = $migration; $bind.Invoke($null,$bindArgs)
    $glassEntry = $migration[[BepInEx.Configuration.ConfigDefinition]::new('Glass Culling Fix','Enabled')]
    if ($glassEntry.Value -ne $case.Expected -or $glassEntry.DefaultValue -ne $true) { throw 'Glass preference migration failed' }
    $bind.Invoke($null,$bindArgs)
    $migration.Save()
    if ((Get-Content $migrationPath -Raw) -match '\[Glass Culling\]') { throw 'Legacy glass setting was not removed' }
    if ($migration.Count -ne 71) { throw 'Migration introduced duplicate settings' }
}
Write-Output 'Verified default-on Glass Culling Fix, Streets description, rename migration, saved preference priority and repeated binding.'

$retiredPath = Join-Path $testRoot 'retired-scene-culling.cfg'
[IO.File]::WriteAllText($retiredPath, "[Scene Culling]`nStationary geometry and decals = true`nExperimental shadow culling = true`n[Glass Culling Fix]`nEnabled = false`n[XeGTAO]`nEffectRadius = 0.55")
$retired = [BepInEx.Configuration.ConfigFile]::new($retiredPath,$false)
$retired.SaveOnConfigSet = $false
$bindArgs[0] = $retired; $bind.Invoke($null,$bindArgs); $bind.Invoke($null,$bindArgs)
$retired.Save()
if ($retired.Count -ne 71 -or (Get-Content $retiredPath -Raw) -match '\[Scene Culling\]') { throw 'Retired scene culling settings survived cleanup' }
if ($retired[[BepInEx.Configuration.ConfigDefinition]::new('Glass Culling Fix','Enabled')].Value -ne $false -or
    $retired[[BepInEx.Configuration.ConfigDefinition]::new('XeGTAO','EffectRadius')].Value -ne [single]0.55) { throw 'Retired setting cleanup changed unrelated preferences' }
Write-Output 'Verified removal of saved scene culling toggles while preserving glass and AO preferences.'
