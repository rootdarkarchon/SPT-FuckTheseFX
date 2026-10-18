param([string]$Unity = 'C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'obj\FilterSceneValidation\UnityFixture'
foreach ($folder in @('Assets\Editor', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Force (Join-Path $project $folder) | Out-Null
}
foreach ($file in @('Source\Vintage\VintageFilterIntegration.cs', 'Source\Sharpen\SharpenFilterIntegration.cs', 'unity\FilterSceneValidation\FilterSceneValidation.cs')) {
    Copy-Item -LiteralPath (Join-Path $repo $file) -Destination (Join-Path $project 'Assets\Editor') -Force
}
Set-Content -LiteralPath (Join-Path $project 'Packages\manifest.json') -Value '{"dependencies":{}}'
Set-Content -LiteralPath (Join-Path $project 'ProjectSettings\ProjectVersion.txt') -Value 'm_EditorVersion: 2022.3.43f1'
$log = Join-Path $project 'validation.log'
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $Unity -ArgumentList @('-batchmode','-nographics','-quit','-projectPath',('"'+$project+'"'),'-executeMethod','FilterSceneValidation.Run','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
while (-not $process.WaitForExit(1000)) {
    if (([DateTime]::UtcNow - $started).TotalMinutes -gt 4) { $process.Kill(); throw "Unity validation timed out: $log" }
}
$process.Refresh()
$result = Join-Path $project 'result.txt'
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result) -or (Get-Item -LiteralPath $result).LastWriteTimeUtc -lt $started) {
    Get-Content -LiteralPath $log -Tail 45
    throw "Filter scene validation failed ($($process.ExitCode)): $log"
}
Get-Content -LiteralPath $result
