param([string]$UnityEditorPath = 'C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe', [string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'obj\OcclusionValidation'
foreach ($folder in @('Assets\Editor','Assets\Plugins','Assets\XeGTAO','Packages','ProjectSettings','Output\Assets')) { New-Item -ItemType Directory -Force (Join-Path $project $folder) | Out-Null }
Copy-Item (Join-Path $repo 'unity\Assets\XeGTAO\*') (Join-Path $project 'Assets\XeGTAO') -Recurse -Force
Copy-Item (Join-Path $repo 'unity\Assets\CustomAmbient.shader') (Join-Path $project 'Assets') -Force
Copy-Item (Join-Path $repo 'Source\Occlusion\*.cs') (Join-Path $project 'Assets\Editor') -Force
Copy-Item (Join-Path $repo 'ConfigurationManagerAttributes.cs') (Join-Path $project 'Assets\Editor') -Force
Copy-Item (Join-Path $repo 'unity\OcclusionValidation\*.cs') (Join-Path $project 'Assets\Editor') -Force
Copy-Item (Join-Path $repo 'unity\OcclusionValidation\*.shader') (Join-Path $project 'Assets\Editor') -Force
foreach ($library in @('BepInEx.dll','0Harmony.dll','Mono.Cecil.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Rocks.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll')) {
 Copy-Item (Join-Path $SptInstallPath "BepInEx\core\$library") (Join-Path $project 'Assets\Plugins') -Force
}
Set-Content (Join-Path $project 'Packages\manifest.json') '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0"}}'
Set-Content (Join-Path $project 'ProjectSettings\ProjectVersion.txt') 'm_EditorVersion: 2022.3.43f1'
$log = Join-Path $project 'validation.log'
$started = [DateTime]::UtcNow
$p = Start-Process $UnityEditorPath -ArgumentList @('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','OcclusionValidation.Run','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
$p.WaitForExit()
if ($p.ExitCode -ne 0) { throw "Occlusion validation failed (exit $($p.ExitCode)): $log. Check the log timestamp before interpreting it." }
$result = Join-Path $project 'validation.txt'
if (-not (Test-Path $result) -or (Get-Item $result).LastWriteTimeUtc -lt $started) { throw 'Unity did not produce a fresh validation result.' }
Copy-Item (Join-Path $project 'Output\Assets\occlusion') (Join-Path $repo 'Assets\occlusion') -Force
Get-Content (Join-Path $project 'validation.txt')
