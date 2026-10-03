param([string]$SptInstallPath='D:\Tarkov-SPT-4.1',[string]$Unity='C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Test-PostEffectsContracts.ps1') -SptInstallPath $SptInstallPath
$project=Join-Path $repo 'obj/InactiveEffectsValidation'
foreach($folder in @('Assets/Editor','Packages','ProjectSettings')) { New-Item -ItemType Directory -Force (Join-Path $project $folder)|Out-Null }
Copy-Item (Join-Path $repo 'Source/PostEffects/InactiveEffectsIntegration.cs') (Join-Path $project 'Assets/Editor') -Force
Copy-Item (Join-Path $repo 'unity/InactiveEffectsValidation/Validation.cs') (Join-Path $project 'Assets/Editor') -Force
Copy-Item (Join-Path $repo 'unity/InactiveEffectsValidation/Status.shader') (Join-Path $project 'Assets') -Force
foreach($dependency in @('0Harmony.dll','Mono.Cecil.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Rocks.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll')) {
    Copy-Item (Join-Path $SptInstallPath "BepInEx/core/$dependency") (Join-Path $project 'Assets/Editor') -Force
}
Copy-Item (Join-Path $repo 'obj/InactiveEffectsAudit/native-dof-reference') $project -Force
Set-Content (Join-Path $project 'runtime-assembly.txt') (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Unity.Postprocessing.Runtime.dll')
Set-Content (Join-Path $project 'game-assembly.txt') (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')
Set-Content (Join-Path $project 'firstpass-assembly.txt') (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp-firstpass.dll')
Set-Content (Join-Path $project 'Packages/manifest.json') '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}'
Set-Content (Join-Path $project 'ProjectSettings/ProjectVersion.txt') 'm_EditorVersion: 2022.3.43f1'
$started=[DateTime]::UtcNow
$log=Join-Path $project 'validation.log'
$process=Start-Process $Unity -ArgumentList @('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','InactiveEffectsValidation.Run','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru -Wait
$process.Refresh()
if($process.ExitCode -ne 0){throw "Inactive-effects validation failed ($($process.ExitCode)): $log"}
$result=Get-Item (Join-Path $project 'validation.txt')
if($result.LastWriteTimeUtc -lt $started){throw 'Stale inactive-effects validation result'}
if(Select-String -LiteralPath $log -Pattern "OnRenderImage.*(write|destination)|possibly didn't write|NullReferenceException|InvalidProgramException"){throw "Image-chain diagnostics in $log"}
Get-Content $result.FullName
