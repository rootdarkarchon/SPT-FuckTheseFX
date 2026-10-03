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
    'Stochastic SSR/NRD anti-firefly' = 'False'
    'Prism Exposure/Resolution percent' = '100'
    'Sharpen Filter/Enabled' = 'True'
    'Inactive Effects/Skip neutral passes' = 'True'
    'Water Surface/Mode' = 'Native'
    'Water Surface/Show replacement tint' = 'False'
    'Water Surface/Surface size' = '1'
    'Water Surface/Surface strength' = '1'
    'Water Surface/Fine ripple strength' = '1'
    'Water Surface/Animation speed' = '1'
    'Stochastic SSR/Minimum receiver smoothness' = '0.4'
    'Stochastic SSR/Cutoff environment blend' = '0.25'
    'Stochastic SSR/Experimental zero-smoothness probe lighting' = 'True'
    'Stochastic SSR/Zero-smoothness probe contribution' = '0.3'
    'Stochastic SSR/Zero-smoothness probe contribution close' = '0.1'
    'Stochastic SSR/Zero-smoothness probe fade start' = '5'
    'Stochastic SSR/Zero-smoothness probe fade end' = '15'
    'Stochastic SSR/Enabled' = 'False'
    'Stochastic SSR/SSR resolution' = 'R768'
    'Stochastic SSR/Maximum iterations' = '350'
    'Stochastic SSR/Thickness' = '1'
    'Stochastic SSR/Thickness hit confidence floor' = '1'
    'Stochastic SSR/Bypass hit normal checks' = 'True'
    'Stochastic SSR/Occluder continuation samples' = '350'
    'Stochastic SSR/Occluder continuation pixels' = '500'
    'Stochastic SSR/Occluder continuation depth separation' = '200'
    'Stochastic SSR/Capture continuation diagnostics' = 'False'
    'Stochastic SSR/Capture traversal work' = 'False'
    'Stochastic SSR/Fallback strength' = '0.65'
    'Stochastic SSR/Allow continued sky on all receivers' = 'True'
    'Stochastic SSR/Water reflection surface colour influence' = '0.2'
    'Stochastic SSR/Limit reflection range' = 'False'
    'Stochastic SSR/Maximum distance' = '250'
    'Stochastic SSR/Temporal stabilization' = 'True'
    'Stochastic SSR/Reconstruction backend' = 'Custom'
    'Stochastic SSR/History frames' = '16'
    'Stochastic SSR/Debug view' = 'Normal'
    'Stochastic SSR/Freeze ray sequence' = 'False'
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
    'XeGTAO/Reflection occlusion fix' = 'True'
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
if ($config.Count -ne 100) { throw 'Unexpected setting count' }
foreach ($definition in $config.Keys) {
    $entry = $config[$definition]
    $name = "$($definition.Section)/$($definition.Key)"
    if (-not $expected.ContainsKey($name) -or $entry.DefaultValue.ToString() -ne $expected[$name]) { throw "Wrong default: $name" }
    $advanced = @($entry.Description.Tags | Where-Object { $_.GetType().GetField('IsAdvanced').GetValue($_) -eq $true }).Count -eq 1
    if ($advanced -ne ($definition.Key -match '^(Adjust filter samples|(Quarter|Half|Full) filter samples)$' -or ($definition.Section -eq 'Stochastic SSR' -and $definition.Key -notin @('Enabled')) -or $definition.Section -in @('Debug') -or ($definition.Section -in @('XeGTAO','Screen Space Contact GI') -and $definition.Key -notin @('Enabled','Quality','Second Pass Enabled')))) { throw "Incorrect advanced visibility: $name" }
}
function Entry($section,$key) { $config[[BepInEx.Configuration.ConfigDefinition]::new($section,$key)] }
foreach($spec in @(@('Surface size',.25,4,2),@('Surface strength',0,2,.5),@('Fine ripple strength',0,4,3),@('Animation speed',0,2,0))) {
    $entry=Entry 'Water Surface' $spec[0]
    if($entry.Description.AcceptableValues.Clamp([single]-10) -ne $spec[1] -or $entry.Description.AcceptableValues.Clamp([single]10) -ne $spec[2]) {throw 'Water control bounds changed'}
    $entry.BoxedValue=[single]$spec[3]
}
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
(Entry 'XeGTAO' 'Reflection occlusion fix').SetSerializedValue('false')
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
(Entry 'Stochastic SSR' 'Experimental zero-smoothness probe lighting').SetSerializedValue('true')
$probeStrength = Entry 'Stochastic SSR' 'Zero-smoothness probe contribution'
if ($probeStrength.Description.AcceptableValues.Clamp([single]-1) -ne 0 -or $probeStrength.Description.AcceptableValues.Clamp([single]2) -ne 1) { throw 'Experimental probe contribution must be bounded 0..1' }
$probeStrength.SetSerializedValue('0.37')
foreach($spec in @(@('Zero-smoothness probe contribution close',1,'0.17'),@('Zero-smoothness probe fade start',1000,'9'),@('Zero-smoothness probe fade end',1000,'31'))) {
    $entry=Entry 'Stochastic SSR' $spec[0]
    if($entry.Description.AcceptableValues.Clamp([single]-1) -ne 0 -or $entry.Description.AcceptableValues.Clamp([single]1001) -ne $spec[1]) {throw "Probe fade bounds changed: $($spec[0])"}
    $entry.SetSerializedValue($spec[2])
}
(Entry 'Stochastic SSR' 'Reconstruction backend').SetSerializedValue('NativeGaussian')
(Entry 'Stochastic SSR' 'Freeze ray sequence').SetSerializedValue('true')
(Entry 'Stochastic SSR' 'Bypass hit normal checks').SetSerializedValue('true')
(Entry 'Stochastic SSR' 'Occluder continuation samples').SetSerializedValue('4096')
(Entry 'Stochastic SSR' 'Occluder continuation pixels').SetSerializedValue('8192')
$iterations = Entry 'Stochastic SSR' 'Maximum iterations'
if ($iterations.Description.AcceptableValues.Clamp([int]0) -ne 32 -or $iterations.Description.AcceptableValues.Clamp([int]2048) -ne 1024) { throw 'Experimental iteration bounds must be 32..1024' }
$iterations.SetSerializedValue('1024')
if ($iterations.Value -ne 1024) { throw '1024 iterations did not reach live settings' }
(Entry 'Stochastic SSR' 'Occluder continuation depth separation').SetSerializedValue('20')
$depthFloor = Entry 'Stochastic SSR' 'Thickness hit confidence floor'
if ($depthFloor.Description.AcceptableValues.Clamp([single]-1) -ne 0 -or $depthFloor.Description.AcceptableValues.Clamp([single]2) -ne 1) { throw 'Thickness confidence floor must remain bounded 0..1' }
$depthFloor.SetSerializedValue('0.5')
(Entry 'Stochastic SSR' 'Capture continuation diagnostics').SetSerializedValue('true')
foreach($name in @('Occluder continuation samples','Occluder continuation pixels','Occluder continuation depth separation')) {
    $entry=Entry 'Stochastic SSR' $name
    $limit=if($name -eq 'Occluder continuation depth separation'){1000}elseif($name -eq 'Occluder continuation samples'){4096}else{8192}
    $low=if($name -eq 'Occluder continuation samples'){[int]-1}else{[single]-1}
    $high=if($name -eq 'Occluder continuation samples'){[int]20000}else{[single]20000}
    if($entry.Description.AcceptableValues.Clamp($low) -ne 0 -or $entry.Description.AcceptableValues.Clamp($high) -ne $limit){throw "Continuation bounds changed: $name"}
}
$fallbackStrength = Entry 'Stochastic SSR' 'Fallback strength'
if ($fallbackStrength.Description.AcceptableValues.Clamp([single]-1) -ne 0 -or $fallbackStrength.Description.AcceptableValues.Clamp([single]2) -ne 1) { throw 'SSR fallback strength must be bounded 0..1' }
$fallbackStrength.SetSerializedValue('0.4')
$ssrResolution = Entry 'Stochastic SSR' 'SSR resolution'
if (([Enum]::GetValues($ssrResolution.SettingType) | ForEach-Object { [int]$_ }) -join ',' -ne '256,512,768,1024,1536,2048') { throw 'Unexpected independent SSR resolutions' }
$ssrResolution.SetSerializedValue('R768')
$surfaceInfluence = Entry 'Stochastic SSR' 'Water reflection surface colour influence'
if ($surfaceInfluence.Description.AcceptableValues.Clamp([single]-1) -ne 0 -or $surfaceInfluence.Description.AcceptableValues.Clamp([single]2) -ne 1) { throw 'Water surface colour influence must be bounded 0..1' }
$surfaceInfluence.SetSerializedValue('0.6')
$range = Entry 'Stochastic SSR' 'Maximum distance'
if ($range.Description.AcceptableValues.Clamp([single]20000) -ne 10000) { throw 'SSR distance range changed' }
$range.SetSerializedValue('500')
$exposure = Entry 'Prism Exposure' 'Resolution percent'
if ($exposure.Description.AcceptableValues.Clamp([single]0) -ne 1 -or $exposure.Description.AcceptableValues.Clamp([single]200) -ne 100) { throw 'Prism exposure percentage must be bounded 1..100' }
$exposure.SetSerializedValue('50')
(Entry 'Sharpen Filter' 'Enabled').SetSerializedValue('false')
(Entry 'Inactive Effects' 'Skip neutral passes').SetSerializedValue('false')
(Entry 'Stochastic SSR' 'Allow continued sky on all receivers').SetSerializedValue('false')

$config.Save()
$saved = [BepInEx.Configuration.ConfigFile]::new($path,$false)
$saved.SaveOnConfigSet = $false
$bindArgs[0] = $saved; $bind.Invoke($null,$bindArgs)
foreach ($definition in $config.Keys) { if ($saved[$definition].GetSerializedValue() -ne $config[$definition].GetSerializedValue()) { throw 'Saved preference did not survive reload' } }
Write-Output 'Verified 100 settings, defaults, bounds and saved preferences. No AOReplacer import.'
foreach ($ddaSetting in @('true','false')) {
    $retiredDdaPath=Join-Path $testRoot 'retired-dda.cfg'
    [IO.File]::WriteAllText($retiredDdaPath,"[Unrelated]`nPreference = untouched`n[Stochastic SSR]`nExperimental foreground DDA continuation = $ddaSetting`nMaximum iterations = 350`nOccluder continuation samples = 350`nZero-smoothness probe contribution = 0.3016432`nZero-smoothness probe contribution close = 0.1`nZero-smoothness probe fade start = 5`nZero-smoothness probe fade end = 15`n")
    $retiredDda=[BepInEx.Configuration.ConfigFile]::new($retiredDdaPath,$false);$retiredDda.SaveOnConfigSet=$false
    $bindArgs[0]=$retiredDda;$bind.Invoke($null,$bindArgs);$bind.Invoke($null,$bindArgs);$retiredDda.Save()
    $retiredDdaText=[IO.File]::ReadAllText($retiredDdaPath)
    if($retiredDda.Count -ne 100 -or $retiredDdaText.Contains('Experimental foreground DDA continuation') -or -not $retiredDdaText.Contains('Preference = untouched')) {throw 'DDA retirement did not preserve unrelated settings'}
    foreach($pair in @(@('Maximum iterations','350'),@('Occluder continuation samples','350'),@('Zero-smoothness probe contribution','0.3016432'),@('Zero-smoothness probe contribution close','0.1'),@('Zero-smoothness probe fade start','5'),@('Zero-smoothness probe fade end','15'))) {
        if($retiredDda[[BepInEx.Configuration.ConfigDefinition]::new('Stochastic SSR',$pair[0])].GetSerializedValue() -ne $pair[1]) {throw "DDA retirement changed preference: $($pair[0])"}
    }
}
Write-Output 'Verified removal of enabled/disabled DDA settings while preserving saved 350 budgets and probe fades.'

# Exercise the exact installed preferences in a workspace copy. Never rewrite
# the user's configuration merely to test migration or new reset defaults.
$installedPath = Join-Path $SptInstallPath 'BepInEx/config/com.rootdarkarchon.fuckthesefx.cfg'
if (Test-Path -LiteralPath $installedPath) {
    $installedHash = (Get-FileHash -LiteralPath $installedPath).Hash
    $copyPath = Join-Path $testRoot 'installed-ssr-cleanup.cfg'
    Copy-Item -LiteralPath $installedPath -Destination $copyPath -Force
    $originalPreferences = @{}
    $section = ''
    foreach ($line in Get-Content -LiteralPath $installedPath) {
        if ($line -match '^\[(.+)\]$') { $section = $Matches[1] }
        elseif ($line -match '^([^#;][^=]*?)\s*=\s*(.*)$') { $originalPreferences["$section/$($Matches[1].Trim())"] = $Matches[2].Trim() }
    }
    $liveCopy = [BepInEx.Configuration.ConfigFile]::new($copyPath,$false)
    $liveCopy.SaveOnConfigSet = $false
    $bindArgs[0] = $liveCopy; $bind.Invoke($null,$bindArgs); $bind.Invoke($null,$bindArgs)
    foreach ($definition in $liveCopy.Keys) {
        $key = "$($definition.Section)/$($definition.Key)"
        if ($originalPreferences.ContainsKey($key)) {
            $entry = $liveCopy[$definition]
            $value = [BepInEx.Configuration.TomlTypeConverter]::ConvertToValue($originalPreferences[$key],$entry.SettingType)
            if (-not $entry.BoxedValue.Equals($value)) { throw "Installed saved preference changed: $key" }
        }
    }
    $liveCopy.Save()
    $retiredPattern = '(?m)^(Denoiser backend|AMD .*|Spatial filter|Normal variance filtering|Hit-aware reconstruction|Ray direction|Match camera aspect ratio|Missing depth coverage|Source colour blend|Relax occluder return traversal|Sky after completed continuation|Reflection gap fill .*|Foreground gap fill permissiveness|Shallow water .*|Constrain hit sample to depth cell|Hit normal confidence only|Sampling mode|Adaptive rays|Experimental foreground DDA continuation) ='
    $ssrSection = ((Get-Content -LiteralPath $copyPath -Raw) -split '\[Stochastic SSR\]')[1] -split '(?m)^\[' | Select-Object -First 1
    if ($ssrSection -match $retiredPattern) { throw 'Retired SSR preferences survived migration' }
    if ((Get-FileHash -LiteralPath $installedPath).Hash -ne $installedHash) { throw 'Validation modified installed preferences' }
    Write-Output 'Verified exact installed saved preferences, repeated binding and retired-key migration in a copy; installed configuration unchanged.'
}

$foliagePath = Join-Path $testRoot 'foliage-promoted.cfg'
[IO.File]::WriteAllText($foliagePath, "[Debug]`nFoliage live extra AO = 0`n[XeGTAO]`nDistance fade start = 100`nDistance fade end = 250")
$foliageConfig = [BepInEx.Configuration.ConfigFile]::new($foliagePath,$false)
$foliageConfig.SaveOnConfigSet = $false
$bindArgs[0] = $foliageConfig; $bind.Invoke($null,$bindArgs); $bind.Invoke($null,$bindArgs)
$foliageConfig.Save()
if ((Get-Content $foliagePath -Raw) -match 'Foliage live extra AO' -or $foliageConfig.Count -ne 100 -or
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
if ($description -match 'experimental' -or $description -notmatch 'Only active on Streets of Tarkov' -or
    $description -notmatch 'Inactive on all other maps') { throw 'Glass culling release description incorrect' }
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
    if ($migration.Count -ne 100) { throw 'Migration introduced duplicate settings' }
}
Write-Output 'Verified default-on Glass Culling Fix, Streets description, rename migration, saved preference priority and repeated binding.'

$retiredPath = Join-Path $testRoot 'retired-scene-culling.cfg'
[IO.File]::WriteAllText($retiredPath, "[Scene Culling]`nStationary geometry and decals = true`nExperimental shadow culling = true`n[Glass Culling Fix]`nEnabled = false`n[XeGTAO]`nEffectRadius = 0.55")
$retired = [BepInEx.Configuration.ConfigFile]::new($retiredPath,$false)
$retired.SaveOnConfigSet = $false
$bindArgs[0] = $retired; $bind.Invoke($null,$bindArgs); $bind.Invoke($null,$bindArgs)
$retired.Save()
if ($retired.Count -ne 100 -or (Get-Content $retiredPath -Raw) -match '\[Scene Culling\]') { throw 'Retired scene culling settings survived cleanup' }
if ($retired[[BepInEx.Configuration.ConfigDefinition]::new('Glass Culling Fix','Enabled')].Value -ne $false -or
    $retired[[BepInEx.Configuration.ConfigDefinition]::new('XeGTAO','EffectRadius')].Value -ne [single]0.55) { throw 'Retired setting cleanup changed unrelated preferences' }
Write-Output 'Verified removal of saved scene culling toggles while preserving glass and AO preferences.'
