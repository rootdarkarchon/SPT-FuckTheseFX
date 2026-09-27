param(
    [string]$SptInstallPath = 'D:\Tarkov-SPT-4.1',
    [string]$ResearchRepo = 'A:\dev\source\SPTAOReplacer',
    [string]$Unity = 'C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$assembly = Join-Path $SptInstallPath 'EscapeFromTarkov_Data\Managed\Unity.Postprocessing.Runtime.dll'
if ((Get-FileHash $assembly).Hash -ne 'B8055ECC0C1A6BA0A04427066A3DEF8C87DA90C1EAC071FA72D8193521441548') { throw 'Unsupported native post-processing assembly' }
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx\core\Mono.Cecil.dll')
$native = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($assembly)
try {
    $method = ($native.MainModule.Types | Where-Object Name -eq 'ScreenSpaceReflectionsRenderer').Methods | Where-Object Name -eq 'Render'
    $calls = @($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -in @('BlitFullscreenTriangle','CopyTexture','DispatchCompute') } | ForEach-Object { $_.Operand.Name })
    if (($calls -join ',') -ne 'BlitFullscreenTriangle,BlitFullscreenTriangle,BlitFullscreenTriangle,BlitFullscreenTriangle,BlitFullscreenTriangle,CopyTexture,DispatchCompute,CopyTexture,BlitFullscreenTriangle') { throw 'Native SSR pass layout changed' }
    if ($method.Body.ExceptionHandlers.Count) { throw 'Native SSR exception layout changed' }
} finally { $native.Dispose() }
$research = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath 'BepInEx\plugins\CustomAmbientResearch\CustomAmbientResearch.dll'))
try {
    $deep = $research.MainModule.Types | Where-Object FullName -eq 'CustomAmbientResearch.DeepGpuProfiler'
    foreach ($name in @('active','recording','camera','Settings','Window')) { if (-not ($deep.Fields | Where-Object Name -eq $name)) { throw "Missing research field $name" } }
    if (-not ($deep.Methods | Where-Object { $_.Name -eq 'BeginRecord' -and $_.Parameters.Count -eq 3 }) -or
        -not ($deep.Methods | Where-Object { $_.Name -eq 'EndRecord' -and $_.Parameters.Count -eq 1 })) { throw 'Unsupported research timestamp bridge' }
} finally { $research.Dispose() }
$project = Join-Path $repo 'obj\SsrProfilingValidation'
foreach ($folder in @('Assets\Editor','Packages','ProjectSettings','Native')) { New-Item -ItemType Directory -Force (Join-Path $project $folder) | Out-Null }
foreach ($file in @('SsrPassHooks.cs','ResearchBridge.cs','Validation.cs')) { Copy-Item (Join-Path $repo "unity\NativeSsrProfiling\$file") (Join-Path $project 'Assets\Editor') -Force }
foreach ($file in @('Fixture.shader','Fixture.compute')) { Copy-Item (Join-Path $repo "unity\NativeSsrProfiling\$file") (Join-Path $project 'Assets') -Force }
Copy-Item (Join-Path $ResearchRepo 'unity\CustomAmbientResearch\GpuTiming.cs') (Join-Path $project 'Assets\Editor') -Force
Copy-Item (Join-Path $SptInstallPath 'BepInEx\plugins\CustomAmbientResearch\CustomAmbientGpu.dll') (Join-Path $project 'Native') -Force
foreach ($file in @('0Harmony.dll','Mono.Cecil.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Rocks.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll')) { Copy-Item (Join-Path $SptInstallPath "BepInEx\core\$file") (Join-Path $project 'Assets\Editor') -Force }
Set-Content (Join-Path $project 'Packages\manifest.json') '{"dependencies":{}}'
Set-Content (Join-Path $project 'ProjectSettings\ProjectVersion.txt') 'm_EditorVersion: 2022.3.43f1'
$log = Join-Path $project 'validation.log'
$started = [DateTime]::UtcNow
$process = Start-Process $Unity -ArgumentList @('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','SsrProfilingValidation.Run','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru -Wait
$process.Refresh()
if ($process.ExitCode -ne 0) { throw "SSR profiling validation failed ($($process.ExitCode)): $log" }
$result = Get-Item (Join-Path $project 'validation.txt')
if ($result.LastWriteTimeUtc -lt $started) { throw 'Stale SSR validation result' }
Get-Content $result.FullName
