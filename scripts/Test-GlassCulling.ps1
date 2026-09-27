param(
    [string]$SptInstallPath = 'D:\Tarkov-SPT-4.1',
    [string]$Unity = 'C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Test-GlassCullingContracts.ps1') -SptInstallPath $SptInstallPath
$project = Join-Path $repo 'obj\GlassCullingValidation'
foreach ($folder in @('Assets\Editor','Packages','ProjectSettings')) { New-Item -ItemType Directory -Force (Join-Path $project $folder) | Out-Null }
Copy-Item (Join-Path $repo 'Source\GlassCulling\*.cs') (Join-Path $project 'Assets\Editor') -Force
Copy-Item (Join-Path $repo 'unity\GlassCullingValidation\GlassCullingValidation.cs') (Join-Path $project 'Assets\Editor') -Force
Copy-Item (Join-Path $repo 'unity\GlassCullingValidation\GlassFixture.shader') (Join-Path $project 'Assets') -Force
foreach ($dependency in @('0Harmony.dll','Mono.Cecil.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Rocks.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll')) {
    Copy-Item (Join-Path $SptInstallPath "BepInEx\core\$dependency") (Join-Path $project 'Assets\Editor') -Force
}
Set-Content (Join-Path $project 'Packages\manifest.json') '{"dependencies":{}}'
Set-Content (Join-Path $project 'ProjectSettings\ProjectVersion.txt') 'm_EditorVersion: 2022.3.43f1'
$log = Join-Path $project 'validation.log'
$started = [DateTime]::UtcNow
$process = Start-Process $Unity -ArgumentList @('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','GlassCullingValidation.Run','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru -Wait
$process.Refresh()
if ($process.ExitCode -ne 0) { throw "Glass culling validation failed ($($process.ExitCode)): $log" }
$result = Get-Item -LiteralPath (Join-Path $project 'validation.txt')
if ($result.LastWriteTimeUtc -lt $started) { throw 'Stale glass culling result' }
Get-Content -LiteralPath $result.FullName
