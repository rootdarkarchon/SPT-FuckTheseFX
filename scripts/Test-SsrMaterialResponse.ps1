param([string]$SptInstallPath='D:\Tarkov-SPT-4.1', [string]$Unity='C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if((Get-FileHash (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Unity.Postprocessing.Runtime.dll')).Hash -ne 'B8055ECC0C1A6BA0A04427066A3DEF8C87DA90C1EAC071FA72D8193521441548') { throw 'Native SSR ABI changed' }
if((Get-FileHash (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/StreamingAssets/Windows/shaders')).Hash -ne 'D41092507FC8CADCDC3DB1D53B9F1E0CB9FC7061AD07FF3C980284731AE2091C') { throw 'Native SSR shader changed' }
$project=Join-Path $repo 'obj/SsrMaterialResponseAudit'
foreach($folder in @('Assets/Editor','Assets/Plugins','Packages','ProjectSettings')) { New-Item -ItemType Directory -Force (Join-Path $project $folder)|Out-Null }
Copy-Item (Join-Path $repo 'unity/StochasticSsrValidation/MaterialResponseAudit.cs') (Join-Path $project 'Assets/Editor') -Force
Copy-Item (Join-Path $repo 'unity/StochasticSsrValidation/MaterialResponseAudit.shader') (Join-Path $project 'Assets') -Force
Copy-Item (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Unity.Postprocessing.Runtime.dll') (Join-Path $project 'Assets/Plugins') -Force
Set-Content (Join-Path $project 'Packages/manifest.json') '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0"}}'
Set-Content (Join-Path $project 'ProjectSettings/ProjectVersion.txt') 'm_EditorVersion: 2022.3.43f1'
Set-Content (Join-Path $project 'game-path.txt') $SptInstallPath
$started=[DateTime]::UtcNow
$log=Join-Path $project 'audit.log'
$process=Start-Process $Unity -ArgumentList @('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','MaterialResponseAudit.Run','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(180000)) { $process.Kill();throw "SSR material audit timeout: $log" }
$process.Refresh()
if($process.ExitCode -ne 0) { throw "SSR material audit failed ($($process.ExitCode)): $log" }
$result=Get-Item (Join-Path $project 'material-response-audit.txt')
if($result.LastWriteTimeUtc -lt $started) { throw 'Stale SSR material audit' }
Get-Content $result.FullName
