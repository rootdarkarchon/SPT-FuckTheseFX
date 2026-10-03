param([string]$Unity='C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe',[string]$SptInstallPath='D:\Tarkov-SPT-4.1')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$project=Join-Path $repo 'obj/WaterValidation/Project'
foreach($folder in @('Assets/Editor','Assets/Plugins','Packages','ProjectSettings','Output')) { New-Item -ItemType Directory -Force (Join-Path $project $folder)|Out-Null }
Copy-Item (Join-Path $repo 'unity/Water/*') (Join-Path $project 'Assets') -Force
$parity=[IO.File]::ReadAllText((Join-Path $repo 'unity/Water/WaterParity.shader'))
if(([regex]::Matches($parity,'#define FTFX_WATER_MULTISCALE 1')).Count -ne 1) {throw 'Missing production normal model'}
$parity=$parity.Replace('Hidden/FTFX/WaterParity','Hidden/FTFX/WaterParityBaseline').Replace('#define FTFX_WATER_MULTISCALE 1','// Local parity oracle: native normals')
[IO.File]::WriteAllText((Join-Path $project 'Assets/WaterParityBaseline.shader'),$parity)
Copy-Item (Join-Path $repo 'unity/WaterValidation/Validation.cs') (Join-Path $project 'Assets/Editor') -Force
Copy-Item (Join-Path $repo 'unity/WaterValidation/IntegrationValidation.cs') (Join-Path $project 'Assets/Editor') -Force
Copy-Item (Join-Path $repo 'Source/Water/WaterSurfaceIntegration.cs') (Join-Path $project 'Assets/Editor') -Force
foreach($name in @('BepInEx.dll','0Harmony.dll','Mono.Cecil.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Rocks.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll')) {
    Copy-Item (Join-Path $SptInstallPath "BepInEx/core/$name") (Join-Path $project 'Assets/Plugins') -Force
}
Copy-Item (Join-Path $repo 'unity/WaterValidation/DepthProbe.shader') (Join-Path $project 'Assets') -Force
Copy-Item (Join-Path $repo 'obj/WaterValidation/native-reference') (Join-Path $project 'native-reference') -Force
Copy-Item (Join-Path $repo 'obj/WaterValidation/native-reference-*') $project -Force
Set-Content (Join-Path $project 'Packages/manifest.json') '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}'
Set-Content (Join-Path $project 'ProjectSettings/ProjectVersion.txt') 'm_EditorVersion: 2022.3.43f1'
Set-Content (Join-Path $project 'game-assembly.txt') (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')
Set-Content (Join-Path $project 'ProjectSettings/TagManager.asset') "%YAML 1.1`n%TAG !u! tag:unity3d.com,2011:`n--- !u!78 &1`nTagManager:`n  serializedVersion: 2`n  tags:`n  - OpticCamera`n  layers: []`n  m_SortingLayers:`n  - name: Default`n    uniqueID: 0`n    locked: 0"
$started=[DateTime]::UtcNow
$log=Join-Path $project 'validation.log'
$process=Start-Process $Unity -ArgumentList @('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','WaterValidation.Run','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(240000)) { $process.Kill();throw "Water validation timeout: $log" }
$process.Refresh()
if($process.ExitCode -ne 0) { throw "Water validation failed ($($process.ExitCode)): $log" }
$result=Get-Item (Join-Path $project 'validation.txt')
if($result.LastWriteTimeUtc -lt $started) { throw 'Stale water validation result' }
Copy-Item (Join-Path $project 'Output/water') (Join-Path $repo 'Assets/water') -Force
$hashes=@{}
foreach($path in @('Assets/water','Source/Water/WaterSurfaceIntegration.cs','unity/Water/WaterParity.shader','unity/Water/WaterParity.cginc','unity/Water/WaterNormals.cginc','unity/WaterValidation/Validation.cs','unity/WaterValidation/IntegrationValidation.cs','unity/WaterValidation/DepthProbe.shader')) {
    $hashes[$path]=(Get-FileHash (Join-Path $repo $path)).Hash
}
$hashes|ConvertTo-Json|Set-Content (Join-Path $repo 'obj/WaterValidation/validated-hashes.json')
Get-Content $result.FullName -Tail 2
