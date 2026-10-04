param([Parameter(Mandatory=$true)][string]$CaptureFolder,[string]$Unity='C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$project=Join-Path $repo 'obj/OcclusionValidation'
if(-not (Test-Path (Join-Path $project 'Output/FTFXNrd.dll'))){throw 'Run Test-Occlusion.ps1 to stage and validate the actual diffuse backend first.'}
Copy-Item (Join-Path $repo 'unity/OcclusionValidation/ContactGIReplay.cs') (Join-Path $project 'Assets/Editor') -Force
Set-Content (Join-Path $project 'contactgi-replay.txt') (Resolve-Path -LiteralPath $CaptureFolder).Path
$started=[DateTime]::UtcNow
$log=Join-Path $project 'contactgi-replay.log'
$process=Start-Process $Unity -ArgumentList @('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','OcclusionValidation.ReplayContactGI','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(600000)){$process.Kill();throw "Replay timeout: $log"}
$process.Refresh()
if($process.ExitCode -ne 0){throw "Diffuse replay failed: $log"}
$result=Get-Item (Join-Path $project 'contactgi-replay-result.txt')
if($result.LastWriteTimeUtc -lt $started){throw 'Stale replay receipt'}
Get-Content $result.FullName
