param([string]$SptInstallPath='D:\Tarkov-SPT-4.1', [string]$Unity='C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe', [string]$ReplayPython='python')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$project=Join-Path $repo 'obj/NrdStandardValidation'
foreach($folder in @('Assets/Editor','Assets/Plugins','Assets/BeforeNrdStandard','Assets/AMD','Packages','ProjectSettings','Output','Native','Replay')) {New-Item -ItemType Directory -Force (Join-Path $project $folder)|Out-Null}
Copy-Item (Join-Path $repo 'Source/StochasticSSR/*.cs') (Join-Path $project 'Assets/Editor') -Force
Copy-Item (Join-Path $repo 'ConfigurationManagerAttributes.cs') (Join-Path $project 'Assets/Editor') -Force
Copy-Item (Join-Path $repo 'unity/StochasticSsrValidation/NrdStandardValidation.cs') (Join-Path $project 'Assets/Editor') -Force
foreach($source in Get-ChildItem (Join-Path $repo 'unity/StochasticSsrValidation/Baseline/BeforeNrdStandard/Source') -Filter '*.cs') {
    $text=(Get-Content -LiteralPath $source.FullName -Raw).Replace('namespace FuckTheseFX.Source.StochasticSSR','namespace FuckTheseFX.Source.StochasticSSR.PreNrdStandard')
    [IO.File]::WriteAllText((Join-Path $project ('Assets/Editor/Before-'+$source.Name)),$text)
}
Copy-Item (Join-Path $repo 'unity/StochasticSsrValidation/Baseline/BeforeNrdStandard/Shaders/*') (Join-Path $project 'Assets/BeforeNrdStandard') -Force
Copy-Item (Join-Path $repo 'unity/StochasticSSR/AMD') (Join-Path $project 'Assets') -Recurse -Force
Copy-Item (Join-Path $repo 'unity/StochasticSSR/AMD') (Join-Path $project 'Assets/BeforeNrdStandard') -Recurse -Force
foreach($name in @('HiZ.compute','Support.shader','Reflection.shader','TodReflection.hlsl','WaterCapture.shader','ReuseBrdf.hlsl','RasterState.hlsl','NrdInputs.shader')) {Copy-Item (Join-Path $repo "unity/StochasticSSR/$name") (Join-Path $project 'Assets') -Force}
Copy-Item (Join-Path $repo 'obj/NRD/bridge/Release/FTFXNrd.dll') (Join-Path $project 'Native/FTFXNrd.dll') -Force
Copy-Item (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Unity.Postprocessing.Runtime.dll') (Join-Path $project 'Assets/Plugins') -Force
foreach($name in @('BepInEx.dll','0Harmony.dll','Mono.Cecil.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Rocks.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll')) {Copy-Item (Join-Path $SptInstallPath "BepInEx/core/$name") (Join-Path $project 'Assets/Plugins') -Force}
Set-Content (Join-Path $project 'Packages/manifest.json') '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
Set-Content (Join-Path $project 'ProjectSettings/ProjectVersion.txt') 'm_EditorVersion: 2022.3.43f1'
Set-Content (Join-Path $project 'game-path.txt') $SptInstallPath
& $ReplayPython (Join-Path $repo 'scripts/Prepare-NrdStandardReplay.py') --captures (Join-Path $SptInstallPath 'BepInEx/FTFX-SSR-Captures') --output (Join-Path $project 'Replay')
if($LASTEXITCODE -ne 0){throw 'Lossless NRD regression capture preparation failed'}
$started=[DateTime]::UtcNow
$log=Join-Path $project 'validation.log'
$process=Start-Process $Unity -ArgumentList @('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','NrdStandardValidation.Run','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(600000)){$process.Kill();throw "NRD standard validation timeout: $log"}
$process.Refresh();if($process.ExitCode -ne 0){throw "NRD standard validation failed ($($process.ExitCode)): $log"}
$result=Get-Item (Join-Path $project 'validation.txt');if($result.LastWriteTimeUtc -lt $started){throw 'Stale NRD standard validation receipt'}
foreach($asset in @('stochasticssr','nrdssr')) {Copy-Item (Join-Path $project "Output/$asset") (Join-Path $repo "Assets/$asset") -Force}
Write-Output "Passed $(@(Get-Content $result.FullName).Count) NRD standard checks: $($result.FullName)"
