param([string]$SptInstallPath='D:\Tarkov-SPT-4.1', [string]$Unity='C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$project=Join-Path $repo 'obj\StochasticSsrValidation'
foreach($folder in @('Assets/Editor','Assets/Plugins','Packages','ProjectSettings','Output','Baseline')) { New-Item -ItemType Directory -Force (Join-Path $project $folder)|Out-Null }
Copy-Item (Join-Path $repo 'Source/StochasticSSR/*.cs') (Join-Path $project 'Assets/Editor') -Force
Copy-Item (Join-Path $repo 'ConfigurationManagerAttributes.cs') (Join-Path $project 'Assets/Editor') -Force
Copy-Item (Join-Path $repo 'unity/StochasticSsrValidation/Validation.cs') (Join-Path $project 'Assets/Editor/Validation.cs') -Force
foreach($name in @('HiZ.compute','Support.shader','FidelityDenoiser.compute')) { Copy-Item (Join-Path $repo "unity/StochasticSSR/$name") (Join-Path $project 'Assets') -Force }
Copy-Item (Join-Path $repo 'unity/StochasticSSR/AMD') (Join-Path $project 'Assets') -Recurse -Force
Copy-Item (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Unity.Postprocessing.Runtime.dll') (Join-Path $project 'Assets/Plugins') -Force
foreach($name in @('BepInEx.dll','0Harmony.dll','Mono.Cecil.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Rocks.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll')) {
    Copy-Item (Join-Path $SptInstallPath "BepInEx/core/$name") (Join-Path $project 'Assets/Plugins') -Force
}
Set-Content (Join-Path $project 'Packages/manifest.json') '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}'
Set-Content (Join-Path $project 'ProjectSettings/ProjectVersion.txt') 'm_EditorVersion: 2022.3.43f1'
Set-Content (Join-Path $project 'game-path.txt') $SptInstallPath
$baseline=Join-Path $repo 'bin/Research/FTFX-SsrPrototype-0.10.0-SPT4.1.zip'
if((Get-FileHash $baseline).Hash -ne 'F450E91CF284DD63C0BDAF10228A64653A70D50EA17DF1C91D52ED68FE81E82B') { throw 'Accepted prototype package changed' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($baseline)
try { foreach($name in @('FTFX.SsrPrototype.dll','ssrprototype')) {
    [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry("BepInEx/plugins/FTFX.SsrPrototype/$name"),(Join-Path $project "Baseline/$name"),$true)
} } finally { $zip.Dispose() }
$started=[DateTime]::UtcNow
$log=Join-Path $project 'validation.log'
$process=Start-Process $Unity -ArgumentList @('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','StochasticSsrValidation.Run','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(180000)) { $process.Kill();throw "SSR release validation timeout: $log" }
$process.Refresh()
if($process.ExitCode -ne 0) { throw "SSR release validation failed ($($process.ExitCode)): $log" }
$result=Get-Item (Join-Path $project 'validation.txt')
if($result.LastWriteTimeUtc -lt $started) { throw 'Stale SSR release validation result' }
Copy-Item (Join-Path $project 'Output/stochasticssr') (Join-Path $repo 'Assets/stochasticssr') -Force
Get-Content $result.FullName
