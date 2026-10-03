param(
 [string]$ResearchRoot='A:\dev\source\SPTAOReplacer',
 [string]$Game='D:\Tarkov-SPT-4.1',
 [string]$Unity='C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe'
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$source=Join-Path $ResearchRoot 'unity\CustomAmbientResearch'
$output=Join-Path $repo 'obj\SsrTimingFix'
$project=Join-Path $output 'Unity'
foreach($folder in @('Assets\Editor','Packages','ProjectSettings','Native')){New-Item -ItemType Directory -Force (Join-Path $project $folder) | Out-Null}
foreach($name in @('AmbientResearch.shader','ForwardAlphaFixture.shader','GlassXray.shader','ResearchTerms.shader','ComputeTimingFixture.compute')){Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $project 'Assets') -Force}
foreach($name in @('ResearchValidation.cs','CaptureExport.cs','GpuTiming.cs','GpuValidation.cs','ComputeTimingValidation.cs','SceneGpuProfiler.cs','DeepGpuProfiler.cs','GpuLedger.cs','SceneGpuValidation.cs','ForwardAlphaProbe.cs','ForwardAlphaValidation.cs','GlassDiscoveryRules.cs','GlassInspector.cs','GlassInspectionValidation.cs','GlassVisibilityHooks.cs','GlassVisibilityTrace.cs','GlassVisibilityValidation.cs','SceneAssociationDump.cs','SceneAssociationValidation.cs','PerformanceRules.cs','DetailHookSwitch.cs','HookValidation.cs')){Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $project 'Assets\Editor') -Force}
foreach($name in @('0Harmony.dll','Mono.Cecil.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Rocks.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll')){Copy-Item -LiteralPath (Join-Path $Game "BepInEx\core\$name") -Destination (Join-Path $project 'Assets\Editor') -Force}
Copy-Item -LiteralPath (Join-Path $Game 'EscapeFromTarkov_Data\Managed\Unity.Postprocessing.Runtime.dll') -Destination (Join-Path $project 'Assets\Editor') -Force
& (Join-Path $ResearchRoot 'scripts\Build-AmbientGpu.ps1') -Output (Join-Path $output 'Native')
Copy-Item -LiteralPath (Join-Path $output 'Native\CustomAmbientGpu.dll') -Destination (Join-Path $project 'Native') -Force
Set-Content -LiteralPath (Join-Path $project 'Packages\manifest.json') -Value '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
Set-Content -LiteralPath (Join-Path $project 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 2022.3.43f1'
$log=Join-Path $output 'validation-clean-project.log'
$started=[DateTime]::UtcNow
$process=Start-Process -FilePath $Unity -ArgumentList @('-batchmode','-quit','-force-d3d11','-projectPath',$project,'-executeMethod','ResearchValidation.Run','-logFile',$log) -WindowStyle Hidden -PassThru
while(-not $process.WaitForExit(1000)){}
$process.Refresh()
if($process.ExitCode -ne 0){throw "DX11 research fixture failed: $log"}
foreach($name in @('validation.txt','gpu-validation.txt','compute-timing-validation.txt','scene-gpu-validation.txt','forward-alpha-validation.txt','glass-inspection-validation.txt','glass-visibility-validation.txt')){
 $file=Get-Item -LiteralPath (Join-Path $project $name)
 if($file.LastWriteTimeUtc -lt $started){throw "Stale validation result: $name"}
}
Get-Content -LiteralPath (Join-Path $project 'compute-timing-validation.txt')
Add-Type -Path (Join-Path $Game 'BepInEx\core\Mono.Cecil.dll')
$core=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $Game 'EscapeFromTarkov_Data\Managed\UnityEngine.CoreModule.dll'))
$fx=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $Game 'BepInEx\plugins\FuckTheseFX\FuckTheseFX.dll'))
try{
 $type=$core.MainModule.Types | Where-Object FullName -eq 'UnityEngine.Rendering.RenderTargetIdentifier'
 if(@($type.Fields | Where-Object {$_.Name -eq 'm_InstanceID' -and $_.FieldType.FullName -eq 'System.Int32'}).Count -ne 1){throw 'Render-target identity ABI changed'}
 foreach($pair in @(@('SsrPipeline','Trace'),@('NativeReceiverFallback','Render'),@('NativeReflectionReconstruction','Render'))){
  $type=$fx.MainModule.Types | Where-Object FullName -eq ('FuckTheseFX.Source.StochasticSSR.'+$pair[0])
  $method=@($type.Methods | Where-Object Name -eq $pair[1]);if($method.Count -ne 1){throw "Unsupported FTFX method: $pair"}
  $calls=@($method[0].Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object Operand)
  if(@($calls | Where-Object {$_.Name -eq 'DispatchCompute' -and $_.Parameters.Count -eq 5}).Count -eq 0){throw "No compatible compute dispatch: $pair"}
  if(@($calls | Where-Object {$_.Name -eq 'SetComputeTextureParam' -and $_.Parameters.Count -eq 4 -and $_.Parameters[2].ParameterType.FullName -eq 'System.String'}).Count -eq 0){throw "No compatible output binding: $pair"}
 }
 'PASS: exact installed render-target identity and all three FTFX compute hook contracts.' | Set-Content -LiteralPath (Join-Path $output 'installed-contracts.txt')
}finally{$core.Dispose();$fx.Dispose()}
