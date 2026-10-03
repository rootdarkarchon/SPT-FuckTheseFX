param([string]$Game='D:\Tarkov-SPT-4.1',[string]$ResearchRoot='A:\dev\source\SPTAOReplacer',[string]$Unity='C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe',[string]$PluginPath='')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$project=Join-Path $repo 'obj\SsrProfilerInterop\Unity'
foreach($folder in @('Assets\Editor','Assets\Plugins','Packages','ProjectSettings','Native')){New-Item -ItemType Directory -Force (Join-Path $project $folder) | Out-Null}
Copy-Item (Join-Path $repo 'Source\StochasticSSR\*.cs') (Join-Path $project 'Assets\Editor') -Force
Copy-Item (Join-Path $repo 'ConfigurationManagerAttributes.cs') (Join-Path $project 'Assets\Editor') -Force
Get-ChildItem (Join-Path $repo 'unity\StochasticSsrValidation') -Filter '*.cs' | Where-Object { $_.Name -notin @('GpuProfile.cs','ContinuationGpuProfile.cs','BlockContinuationProfile.cs','ReducedRateFeasibility.cs','CurrentSsrTiming.cs') } | Copy-Item -Destination (Join-Path $project 'Assets\Editor') -Force
Remove-Item -LiteralPath (Join-Path $project 'Assets\Editor\ContinuationGpuProfile.cs') -Force -ErrorAction SilentlyContinue
foreach($baseline in @('BeforeCleanup','BeforePerformance','BeforeTraceOptimization','BeforeContinuationOptimization')){
 New-Item -ItemType Directory -Force (Join-Path $project "Assets\$baseline") | Out-Null
 Copy-Item (Join-Path $repo "unity\StochasticSsrValidation\Baseline\$baseline\Shaders\*") (Join-Path $project "Assets\$baseline") -Recurse -Force
 Get-ChildItem (Join-Path $repo "unity\StochasticSsrValidation\Baseline\$baseline\Source") -Filter '*.cs' | ForEach-Object {Copy-Item $_.FullName (Join-Path $project ("Assets\Editor\$baseline-"+$_.Name)) -Force}
}
Copy-Item (Join-Path $repo 'unity\StochasticSSR\*') (Join-Path $project 'Assets') -Recurse -Force
Copy-Item (Join-Path $repo 'unity\StochasticSsrValidation\Experiments\ReceiverProjection.shader') (Join-Path $project 'Assets\ReceiverProjection.shader') -Force
Get-ChildItem (Join-Path $repo 'unity\StochasticSsrValidation') -Filter '*.shader' | Copy-Item -Destination (Join-Path $project 'Assets') -Force
Copy-Item (Join-Path $repo 'unity\Assets\CustomAmbient.shader') (Join-Path $project 'Assets') -Force
New-Item -ItemType Directory -Force (Join-Path $project 'Assets\XeGTAO') | Out-Null
Copy-Item (Join-Path $repo 'unity\Assets\XeGTAO\BentNormals.hlsl') (Join-Path $project 'Assets\XeGTAO') -Force
foreach($name in @('GpuTiming.cs','SceneGpuProfiler.cs','DeepGpuProfiler.cs','GpuLedger.cs')){Copy-Item (Join-Path $ResearchRoot "unity\CustomAmbientResearch\$name") (Join-Path $project 'Assets\Editor') -Force}
foreach($name in @('BepInEx.dll','0Harmony.dll','Mono.Cecil.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Rocks.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll')){Copy-Item (Join-Path $Game "BepInEx\core\$name") (Join-Path $project 'Assets\Plugins') -Force}
Copy-Item (Join-Path $Game 'EscapeFromTarkov_Data\Managed\Unity.Postprocessing.Runtime.dll') (Join-Path $project 'Assets\Plugins') -Force
Copy-Item (Join-Path $Game 'BepInEx\plugins\CustomAmbientResearch\CustomAmbientGpu.dll') (Join-Path $project 'Native') -Force
Set-Content (Join-Path $project 'Packages\manifest.json') '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}'
Set-Content (Join-Path $project 'ProjectSettings\ProjectVersion.txt') 'm_EditorVersion: 2022.3.43f1'
Set-Content (Join-Path $project 'game-path.txt') $Game
if([string]::IsNullOrWhiteSpace($PluginPath)){$PluginPath=Join-Path $Game 'BepInEx/plugins/FuckTheseFX/FuckTheseFX.dll'}
Set-Content (Join-Path $project 'ftfx-dll-path.txt') (Resolve-Path -LiteralPath $PluginPath).Path
$started=[DateTime]::UtcNow
$log=Join-Path $project 'profiler-interop.log'
$process=Start-Process $Unity -ArgumentList @('-batchmode','-quit','-force-d3d11','-projectPath',$project,'-executeMethod','StochasticSsrValidation.RunProfilerInterop','-logFile',$log) -WindowStyle Hidden -PassThru
while(-not $process.WaitForExit(1000)){}
$process.Refresh()
if($process.ExitCode -ne 0){throw "SSR/profiler integration failed: $log"}
$result=Get-Item (Join-Path $project 'profiler-interop-validation.txt')
if($result.LastWriteTimeUtc -lt $started){throw 'Stale SSR/profiler integration result'}
"PASS: $(@(Get-Content $result.FullName).Count) DLL signal/resource/interval checks against $PluginPath. $($result.FullName)"


