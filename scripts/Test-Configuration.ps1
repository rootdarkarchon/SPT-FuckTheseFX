param([string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
foreach ($name in @('UnityEngine.CoreModule.dll','UnityEngine.dll','UnityEngine.IMGUIModule.dll')) { Add-Type -Path (Join-Path $SptInstallPath "EscapeFromTarkov_Data/Managed/$name") }
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx/core/BepInEx.dll')
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'bin/Release/netstandard2.1/FuckTheseFX.dll'))
$flags = [Reflection.BindingFlags]'Instance,Static,Public,NonPublic'
$plugin = $assembly.GetType('FuckTheseFX.Plugin', $true)
$bind = $plugin.GetMethod('BindSettings', $flags)
$testRoot = Join-Path $repo 'obj/ConfigurationChecks'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$path = Join-Path $testRoot 'new-layout.cfg'
[IO.File]::WriteAllText($path, '')
$config = [BepInEx.Configuration.ConfigFile]::new($path, $false)
$config.SaveOnConfigSet = $false
$bindArgs = [object[]]::new(1); $bindArgs[0] = $config
$null = $bind.Invoke($null, $bindArgs)
$catalog = $plugin.GetField('Settings', $flags).GetValue($null)
$entries = @($catalog.GetType().GetProperty('Settings', $flags).GetValue($catalog))
$expected = Get-Content (Join-Path $PSScriptRoot 'ConfigurationExpectations.json') -Raw | ConvertFrom-Json
if ($config.Count -ne 104 -or $entries.Count -ne 103 -or $expected.Count -ne 103) { throw 'Expected 103 real settings plus one UI row' }
function Entry($section, $key) { $config[[BepInEx.Configuration.ConfigDefinition]::new($section, $key)] }
function Tag($entry, $name) { $entry.Description.Tags[0].GetType().GetField($name).GetValue($entry.Description.Tags[0]) }
foreach ($spec in $expected) {
    $entry = Entry $spec.section $spec.key
    if ($null -eq $entry -or $entry.DefaultValue.ToString() -ne $spec.default -or $entry.SettingType.Name -ne $spec.type) { throw "Wrong name/type/default: $($spec.section)/$($spec.key)" }
    if ((Tag $entry 'IsAdvanced') -ne $spec.advanced -or (Tag $entry 'ShowRangeAsPercent') -ne $false) { throw "Wrong display metadata: $($spec.key)" }
    $range = $entry.Description.AcceptableValues
    if ($null -ne $spec.min -and ($range.MinValue.ToString() -ne $spec.min -or $range.MaxValue.ToString() -ne $spec.max)) { throw "Wrong range: $($spec.key)" }
    if ($spec.list -and (($range.AcceptableValues -join ',') -ne ($spec.list -join ','))) { throw "Wrong choices: $($spec.key)" }
    if ($entry.SettingType.IsEnum -and -not [Enum]::IsDefined($entry.SettingType, $entry.DefaultValue)) { throw 'Invalid enum default' }
}
$expectedGroupOrder = '0. Presets|1. Vanilla Optimizations|2. XeGTAO|3. Screen Space Contact Global Illumination|4. Stochastic GGX SSR|5. Custom Water|9. Debug'
$ui = Entry '0. Presets' 'Presets'
if ((Tag $ui 'IsAdvanced') -ne $false -or (Tag $ui 'HideDefaultButton') -ne $true -or (Tag $ui 'HideSettingName') -ne $true -or $null -eq (Tag $ui 'CustomDrawer') -or $entries.Contains($ui)) { throw 'Incorrect preset drawer' }
$manager = [Reflection.Assembly]::LoadFrom((Join-Path $SptInstallPath 'BepInEx/plugins/spt/ConfigurationManager/ConfigurationManager.dll'))
$managerEntryType = $manager.GetType('ConfigurationManager.ConfigSettingEntry', $true)
$managerEntry = [Activator]::CreateInstance($managerEntryType, $flags, $null, [object[]]@($ui, $null), $null)
foreach ($property in @('CustomDrawer','HideDefaultButton','HideSettingName','IsAdvanced')) {
    $actual = $managerEntryType.GetProperty($property, $flags).GetValue($managerEntry)
    if ($actual -ne (Tag $ui $property)) { throw "Installed ConfigurationManager rejected $property" }
}
$bounce = Entry '3. Screen Space Contact Global Illumination' 'Maximum bounce fraction (%)'
$managerBounce = [Activator]::CreateInstance($managerEntryType, $flags, $null, [object[]]@($bounce, $null), $null)
if ($managerEntryType.GetProperty('ShowRangeAsPercent',$flags).GetValue($managerBounce) -ne $false) { throw 'Bounce percentage is range-relative' }
# Exercise the installed manager's actual category-building path. Sorting section
# names in this test would miss its first-seen registration-order behavior.
$managerType = $manager.GetType('ConfigurationManager.ConfigurationManager', $true)
$managerFilters = [BepInEx.Configuration.ConfigFile]::new((Join-Path $testRoot 'manager-filters.cfg'), $false)
$managerFilters.SaveOnConfigSet = $false
function TestManagerGroupOrder($boundConfig) {
    $instance = [Runtime.Serialization.FormatterServices]::GetUninitializedObject($managerType)
    $allField = $managerType.GetField('_allSettings', $flags)
    $all = [Activator]::CreateInstance($allField.FieldType)
    $info = [BepInEx.BepInPlugin]::new('com.rootdarkarchon.fuckthesefx', 'Fuck These FX', '1.8.5')
    foreach ($pair in $boundConfig) {
        $setting = [Activator]::CreateInstance($managerEntryType, $flags, $null, [object[]]@($pair.Value, $null), $null)
        $managerEntryType.GetProperty('PluginInfo', $flags).SetValue($setting, $info)
        $all.Add($setting)
    }
    $allField.SetValue($instance, $all)
    $filteredField = $managerType.GetField('_filteredSetings', $flags)
    $filteredField.SetValue($instance, [Activator]::CreateInstance($filteredField.FieldType))
    $managerType.GetField('_searchString', $flags).SetValue($instance, '')
    foreach ($name in @('_showAdvanced', '_showKeybinds', '_showSettings', '_pluginConfigCollapsedDefault')) {
        $filter = $managerFilters.Bind[bool]('Filters', $name, $true, '')
        $managerType.GetField($name, $flags).SetValue($instance, $filter)
    }
    foreach ($advanced in @($true, $false)) {
        $managerFilters[[BepInEx.Configuration.ConfigDefinition]::new('Filters', '_showAdvanced')].Value = $advanced
        $null = $managerType.GetMethod('BuildFilteredSettingList', $flags).Invoke($instance, @())
        $result = @($filteredField.GetValue($instance))[0]
        $categories = $result.GetType().GetField('Categories', $flags).GetValue($result)
        $actual = @($categories | ForEach-Object { $_.GetType().GetField('Name', $flags).GetValue($_) }) -join '|'
        $visible = @($boundConfig | Where-Object { $advanced -or (Tag $_.Value 'IsAdvanced') -ne $true } | ForEach-Object { $_.Key.Section })
        $wanted = ($expectedGroupOrder.Split('|') | Where-Object { $_ -in $visible }) -join '|'
        if ($actual -ne $wanted) { throw "Wrong installed ConfigurationManager group order (advanced=$advanced): $actual" }
    }
}
TestManagerGroupOrder $config
function Field($owner, $name) { $owner.GetType().GetField($name,$flags).GetValue($owner) }
function Near($value, $wanted) { if ([Math]::Abs($value-$wanted) -gt 0.000001) { throw "Wrong runtime fraction: $value, expected $wanted" } }
$ao = $plugin.GetField('Occlusion',$flags).GetValue($null)
$ssr = $plugin.GetField('Ssr',$flags).GetValue($null)
Near (Field ($ao.GetType().GetProperty('Primary',$flags).GetValue($ao)) 'Falloff') .62
Near ($ao.GetType().GetProperty('AmbientStrengthFraction',$flags).GetValue($ao)) .6
Near ($ao.GetType().GetProperty('FoliageSplitPositionFraction',$flags).GetValue($ao)) .5
$bleed = Field $ao 'Bleed'
foreach ($percent in @(1,20,50)) { $bounce.BoxedValue=$percent; Near ($bleed.GetType().GetProperty('BounceFraction',$flags).GetValue($bleed)) ($percent/100.0) }
foreach ($spec in $expected | Where-Object key -like '* (%)') {
    $entry=Entry $spec.section $spec.key
    foreach ($percent in @([int]$spec.min,[int]$spec.default,[int]$spec.max)) { $entry.BoxedValue=$percent; if ($entry.BoxedValue -ne $percent) { throw 'Integer percentage endpoint failed' } }
}
(Entry '4. Stochastic GGX SSR' 'Minimum receiver smoothness (%)').BoxedValue=40
Near (Field ($ssr.GetType().GetMethod('Read',$flags).Invoke($ssr,@())) 'ReceiverCutoff') .4
if ((Field ($ssr.GetType().GetMethod('Read',$flags).Invoke($ssr,@())) 'HistoryFrames') -ne 8) { throw 'SSR history default did not reach runtime' }
$endpointSky=Entry '4. Stochastic GGX SSR' 'Endpoint-validated sky fallback'
if ($endpointSky.BoxedValue -ne $true -or (Field ($ssr.GetType().GetMethod('Read',$flags).Invoke($ssr,@())) 'EndpointSkyFallback') -ne $true) { throw 'Endpoint sky fallback must default on' }
$endpointSky.BoxedValue=$false
if ((Field ($ssr.GetType().GetMethod('Read',$flags).Invoke($ssr,@())) 'EndpointSkyFallback') -ne $false) { throw 'Endpoint sky fallback disable did not reach runtime' }
$endpointSky.BoxedValue=$true
function SetNonDefaultValues {
    foreach ($entry in $entries) {
        $range=$entry.Description.AcceptableValues
        if ($entry.SettingType -eq [bool]) { $entry.BoxedValue=-not $entry.DefaultValue }
        elseif ($entry.SettingType.IsEnum) { $entry.BoxedValue=@([Enum]::GetValues($entry.SettingType) | Where-Object { $_ -ne $entry.DefaultValue })[0] }
        elseif ($range.GetType().Name -like 'AcceptableValueList*') { $entry.BoxedValue=@($range.AcceptableValues | Where-Object { $_ -ne $entry.DefaultValue })[0] }
        else { $entry.BoxedValue=if ($range.MinValue -ne $entry.DefaultValue) { $range.MinValue } else { $range.MaxValue } }
    }
}
$apply=$catalog.GetType().GetMethod('ApplyPreset',$flags)
$presetType=$catalog.GetType().GetNestedType('Preset',$flags)
$vanilla=@{
    '1. Vanilla Optimizations/Additional Ambient Highlight'=$true
    '1. Vanilla Optimizations/Streets of Tarkov Glass Culling Fix'=$false
    '1. Vanilla Optimizations/Skip Neutral Effects Passes'=$false
    '1. Vanilla Optimizations/Prism Auto Exposure Resolution (%)'=100
    '1. Vanilla Optimizations/Sharpen Filter'=$true
    '1. Vanilla Optimizations/Vintage Filter'=$true
    '1. Vanilla Optimizations/Ultimate Bloom Input Scale (%)'=100
    '1. Vanilla Optimizations/Distant Shadows Resolution Fix'=$false
    '2. XeGTAO/Enabled'=$false
    '3. Screen Space Contact Global Illumination/Enabled'=$false
    '4. Stochastic GGX SSR/Enabled'=$false
}
$vanillaPlus=@{
    '1. Vanilla Optimizations/Additional Ambient Highlight'=$true
    '1. Vanilla Optimizations/Streets of Tarkov Glass Culling Fix'=$true
    '1. Vanilla Optimizations/Skip Neutral Effects Passes'=$true
    '1. Vanilla Optimizations/Prism Auto Exposure Resolution (%)'=66
    '1. Vanilla Optimizations/Sharpen Filter'=$true
    '1. Vanilla Optimizations/Vintage Filter'=$true
    '1. Vanilla Optimizations/Ultimate Bloom Input Scale (%)'=50
    '1. Vanilla Optimizations/Distant Shadows Resolution Fix'=$true
    '2. XeGTAO/Enabled'=$false
    '3. Screen Space Contact Global Illumination/Enabled'=$false
    '4. Stochastic GGX SSR/Enabled'=$false
}
$highQuality=$vanillaPlus.Clone()
foreach ($key in @('Additional Ambient Highlight','Sharpen Filter','Vintage Filter')) { $highQuality["1. Vanilla Optimizations/$key"]=$false }
$highQuality['2. XeGTAO/Enabled']=$true
$highQuality['4. Stochastic GGX SSR/Enabled']=$true
$highQuality['4. Stochastic GGX SSR/Optic SSR enabled']=$false
$highQualityPlus=$highQuality.Clone()
$highQualityPlus['4. Stochastic GGX SSR/Optic SSR enabled']=$true
$highQualityPlus['3. Screen Space Contact Global Illumination/Enabled']=$true
$presets=@{ Vanilla=$vanilla; VanillaPlus=$vanillaPlus; HighQualityVisuals=$highQuality; HighQualityPlusVisuals=$highQualityPlus }
$helpText=$catalog.GetType().GetField('PresetHelpText',$flags).GetRawConstantValue()
if ($helpText -ne 'Hold Shift while pressing on a preset button to restore full defaults. FTFX provides more settings than shown when enabling "Advanced Settings". The current advanced settings, presets and defaults are tuned and authored for visuals and performance.') { throw 'Wrong visible preset help text' }
foreach ($spec in @(
    @('VanillaPresetButton','Vanilla','Disables FTFX fixes and changes, restoring Vanilla appearance. Useful for direct image and performance comparison.'),
    @('VanillaPlusPresetButton','FTFX Vanilla+ (Default)','Retains Vanilla baseline appearance and fixes minor performance issues.'),
    @('HighQualityPresetButton','FTFX HQ Visuals','Applies targeted FTFX visuals as designed by the author.'),
    @('HighQualityPlusPresetButton','FTFX HQ+ Visuals','FTFX HQ Visuals, but with all effects enabled.')
)) {
    $content=$catalog.GetType().GetField($spec[0],$flags).GetValue($null)
    if ($content.text -ne $spec[1] -or $content.tooltip -ne $spec[2]) { throw "Wrong preset label/tooltip: $($spec[0])" }
}
foreach ($savePolicy in @($false,$true)) {
    $config.SaveOnConfigSet=$savePolicy
    foreach ($restoreDefaults in @($false,$true)) {
    SetNonDefaultValues
    foreach ($presetName in @('VanillaPlus','HighQualityVisuals','HighQualityPlusVisuals','HighQualityPlusVisuals','HighQualityVisuals','HighQualityPlusVisuals','Vanilla','Vanilla','VanillaPlus','VanillaPlus')) {
        $before=@{}
        foreach ($entry in $entries) { $before["$($entry.Definition.Section)/$($entry.Definition.Key)"]=$entry.BoxedValue }
        $preset=[Enum]::Parse($presetType,$presetName)
        $null=$apply.Invoke($catalog,[object[]]@($preset,$restoreDefaults))
        if ($config.SaveOnConfigSet -ne $savePolicy) { throw 'Preset changed save policy' }
        foreach ($entry in $entries) {
            $key="$($entry.Definition.Section)/$($entry.Definition.Key)"
            $wanted=if ($presets[$presetName].ContainsKey($key)) { $presets[$presetName][$key] } elseif ($restoreDefaults) { $entry.DefaultValue } else { $before[$key] }
            if (-not $entry.BoxedValue.Equals($wanted)) { throw "Wrong $presetName preset value (restoreDefaults=$restoreDefaults): $key" }
        }
        $saved=[BepInEx.Configuration.ConfigFile]::new($path,$false);$saved.SaveOnConfigSet=$false
        $bindArgs[0]=$saved;$null=$bind.Invoke($null,$bindArgs)
        TestManagerGroupOrder $saved
        foreach ($entry in $entries) { if ($saved[$entry.Definition].GetSerializedValue() -ne $entry.GetSerializedValue()) { throw 'Preset did not persist' } }
        if ($ui.Value -ne '') { throw 'Preset backing entry changed' }
    }
    }
}
$bindArgs[0]=$config;$null=$bind.Invoke($null,$bindArgs)
if ($config.Count -ne 104) { throw 'Repeated binding duplicated settings' }
Write-Output 'Verified 103 settings, numerically ordered CM 18.4 groups, integer percentages, eight-frame SSR default, default-on endpoint sky fallback, normal optic toggle, R512 optic default and preserved preset resolution, four preset labels/tooltips and help text, selective and Shift-reset presets, save policy and persistence.'
