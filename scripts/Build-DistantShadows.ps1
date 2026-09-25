param([string]$UnityEditorPath = 'C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe',
      [string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'obj\DistantShadowValidation'
if (-not (Test-Path (Join-Path $project 'native-reference'))) { throw 'Missing local installed-shader reference. See docs/DistantShadows.md.' }
foreach ($folder in @('Assets\Editor','Assets\Plugins','Packages','ProjectSettings','AssetBundles')) { New-Item -ItemType Directory -Force (Join-Path $project $folder) | Out-Null }
Copy-Item (Join-Path $repo 'unity\Assets\DistantShadowSampling.shader') (Join-Path $project 'Assets') -Force
Copy-Item (Join-Path $repo 'Source\DistantShadows\DistantShadowSamples.cs') (Join-Path $project 'Assets\Editor') -Force
Copy-Item (Join-Path $repo 'Source\DistantShadows\DistantShadowSamplingIntegration.cs') (Join-Path $project 'Assets\Editor') -Force
Copy-Item (Join-Path $repo 'Source\DistantShadows\DistantShadowResolutionIntegration.cs') (Join-Path $project 'Assets\Editor') -Force
Copy-Item (Join-Path $repo 'Source\Highlight\AmbientHighlightIntegration.cs') (Join-Path $project 'Assets\Editor') -Force
foreach ($source in @('DistantShadowValidation.cs','HookFixture.cs','AmbientHighlightFixture.cs')) {
    Copy-Item (Join-Path $repo "unity\DistantShadowValidation\$source") (Join-Path $project 'Assets\Editor') -Force
}
Copy-Item (Join-Path $repo 'unity\DistantShadowValidation\DistantShadow.cs') (Join-Path $project 'Assets') -Force
Copy-Item (Join-Path $repo 'unity\DistantShadowValidation\AmbientHighlight.cs') (Join-Path $project 'Assets') -Force
foreach ($library in @('0Harmony.dll','Mono.Cecil.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Rocks.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll')) {
    Copy-Item (Join-Path (Join-Path $SptInstallPath 'BepInEx\core') $library) (Join-Path $project 'Assets\Plugins') -Force
}
Set-Content (Join-Path $project 'Packages\manifest.json') '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
Set-Content (Join-Path $project 'ProjectSettings\ProjectVersion.txt') 'm_EditorVersion: 2022.3.43f1'
$log = Join-Path $project 'validation.log'
$p = Start-Process $UnityEditorPath -ArgumentList @('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','DistantShadowValidation.Run','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
$p.WaitForExit()
if ($p.ExitCode -ne 0) { throw "Distant shadow validation failed: $log" }
Copy-Item (Join-Path $project 'AssetBundles\distantshadows') (Join-Path $repo 'Assets\distantshadows') -Force
Get-Content (Join-Path $project 'validation.txt')
Get-Content (Join-Path $project 'highlight-validation.txt')
Get-Content (Join-Path $project 'hook-validation.txt')
Get-Content (Join-Path $project 'experiment.txt')
