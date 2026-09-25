param(
 [string]$UnityEditorPath = 'C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe',
 [string]$SptInstallPath = 'D:\Tarkov-SPT-4.1'
)
$ErrorActionPreference='Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'obj\CustomAmbientValidation'
foreach($folder in @('Assets\Editor','Packages','ProjectSettings','AssetBundles')) {
 New-Item -ItemType Directory -Force (Join-Path $project $folder) | Out-Null
}
Copy-Item (Join-Path $repo 'unity\Assets\CustomAmbient.shader') (Join-Path $project 'Assets') -Force
Copy-Item (Join-Path $repo 'unity\CustomAmbientValidation\*.cs') (Join-Path $project 'Assets\Editor') -Force
Set-Content (Join-Path $project 'Packages\manifest.json') '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.imgui":"1.0.0"}}'
Set-Content (Join-Path $project 'ProjectSettings\ProjectVersion.txt') 'm_EditorVersion: 2022.3.43f1'
Set-Content (Join-Path $project 'game-path.txt') $SptInstallPath
$log = Join-Path $project 'shader-validation.log'
$argsList=@('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','AmbientValidation.Run','-logFile',('"'+$log+'"'))
$p=Start-Process $UnityEditorPath -ArgumentList $argsList -WindowStyle Hidden -PassThru
$p.WaitForExit()
if($p.ExitCode -ne 0){throw "Unity validation failed: $log"}
New-Item -ItemType Directory -Force (Join-Path $repo 'Assets') | Out-Null
Get-Content (Join-Path $project 'validation.txt')

