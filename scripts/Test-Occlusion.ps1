param([string]$UnityEditorPath = 'C:\Program Files\Unity 2022.3.43f1\Editor\Unity.exe', [string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$diffuseInputs=@('native/NRD/Bridge.cpp','obj/NRD/bridge/Release/FTFXNrd.dll','native/NRD/UPSTREAM.json','native/NRD/vendor/NRD/Shaders/NRD.hlsli','native/NRD/vendor/NRD/Shaders/NRDConfig.hlsli','Source/Occlusion/ColorBleedPipeline.cs','Source/Occlusion/ColorBleedSettings.cs','Source/Occlusion/NrdDiffuseBackend.cs','Source/Occlusion/ContactGICapture.cs','Source/Occlusion/GpuAssets.cs','Source/Occlusion/CameraOcclusion.cs','Source/Occlusion/XeGTAOIntegration.cs','SettingsCatalog.cs','unity/Assets/XeGTAO/ContactGI.compute','unity/Assets/XeGTAO/ContactGICommon.hlsl','unity/Assets/XeGTAO/ColorBleed.shader','unity/StochasticSSR/AMD/BlueNoise.bytes','unity/OcclusionValidation/NrdDiffuseValidation.cs','unity/OcclusionValidation/ContactGICameraValidation.cs','unity/OcclusionValidation/ContactGIPerformanceValidation.cs','unity/OcclusionValidation/ContactGIProbe.compute','unity/OcclusionValidation/ContactGIReplay.cs')
$inputHashes=@{}
foreach($file in $diffuseInputs){$inputHashes[$file]=(Get-FileHash -LiteralPath (Join-Path $repo $file)).Hash}
$project = Join-Path $repo 'obj\OcclusionValidation'
$editorStage=[IO.Path]::GetFullPath((Join-Path $project 'Assets/Editor'))
$stageRoot=[IO.Path]::GetFullPath($project)+[IO.Path]::DirectorySeparatorChar
if (-not $editorStage.StartsWith($stageRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Validation source stage escaped project' }
if (Test-Path -LiteralPath $editorStage) { Remove-Item -LiteralPath $editorStage -Recurse -Force }
foreach ($folder in @('Assets\Editor','Assets\Plugins','Assets\XeGTAO','Packages','ProjectSettings','Output\Assets','Assets/AMD','Assets/XeGTAO/NRD')) { New-Item -ItemType Directory -Force (Join-Path $project $folder) | Out-Null }
Copy-Item (Join-Path $repo 'unity\Assets\XeGTAO\*') (Join-Path $project 'Assets\XeGTAO') -Recurse -Force
Copy-Item (Join-Path $repo 'native/NRD/vendor/NRD/Shaders/NRD.hlsli') (Join-Path $project 'Assets/XeGTAO/NRD') -Force
Copy-Item (Join-Path $repo 'native/NRD/vendor/NRD/Shaders/NRDConfig.hlsli') (Join-Path $project 'Assets/XeGTAO/NRD') -Force
Copy-Item (Join-Path $repo 'unity/StochasticSSR/AMD/BlueNoise.bytes') (Join-Path $project 'Assets/AMD') -Force
Copy-Item (Join-Path $repo 'obj/NRD/bridge/Release/FTFXNrd.dll') (Join-Path $project 'Output') -Force
Copy-Item (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Unity.Postprocessing.Runtime.dll') (Join-Path $project 'Assets/Plugins') -Force
Copy-Item (Join-Path $repo 'unity\Assets\CustomAmbient.shader') (Join-Path $project 'Assets') -Force
Copy-Item (Join-Path $repo 'Source\Occlusion\*.cs') (Join-Path $project 'Assets\Editor') -Force
Remove-Item -LiteralPath (Join-Path $project 'Assets/Editor/AmbientReflectionCapture.cs') -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $repo 'Source\StochasticSSR\AmbientSurfaceCapture.cs') (Join-Path $project 'Assets\Editor') -Force
Copy-Item (Join-Path $repo 'SettingsCatalog.cs') (Join-Path $project 'Assets/Editor') -Force
Copy-Item (Join-Path $repo 'ConfigurationManagerAttributes.cs') (Join-Path $project 'Assets\Editor') -Force
Copy-Item (Join-Path $repo 'unity\OcclusionValidation\*.cs') (Join-Path $project 'Assets\Editor') -Force
Copy-Item (Join-Path $repo 'unity/OcclusionValidation/*.compute') (Join-Path $project 'Assets/Editor') -Force
Copy-Item (Join-Path $repo 'unity\OcclusionValidation\*.shader') (Join-Path $project 'Assets\Editor') -Force
foreach ($library in @('BepInEx.dll','0Harmony.dll','Mono.Cecil.dll','Mono.Cecil.Mdb.dll','Mono.Cecil.Pdb.dll','Mono.Cecil.Rocks.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll')) {
 Copy-Item (Join-Path $SptInstallPath "BepInEx\core\$library") (Join-Path $project 'Assets\Plugins') -Force
}
Set-Content (Join-Path $project 'Packages\manifest.json') '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}'
Set-Content (Join-Path $project 'ProjectSettings\ProjectVersion.txt') 'm_EditorVersion: 2022.3.43f1'
$log = Join-Path $project 'validation.log'
$started = [DateTime]::UtcNow
$p = Start-Process $UnityEditorPath -ArgumentList @('-batchmode','-quit','-force-d3d11','-projectPath',('"'+$project+'"'),'-executeMethod','OcclusionValidation.Run','-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru
$p.WaitForExit()
$p.Refresh()
if ($p.ExitCode -ne 0) { throw "Occlusion validation failed (exit $($p.ExitCode)): $log. Check the log timestamp before interpreting it." }
$result = Join-Path $project 'validation.txt'
if (-not (Test-Path $result) -or (Get-Item $result).LastWriteTimeUtc -lt $started) { throw 'Unity did not produce a fresh validation result.' }
Copy-Item (Join-Path $project 'Output\Assets\occlusion') (Join-Path $repo 'Assets\occlusion') -Force
foreach($file in $diffuseInputs){if((Get-FileHash -LiteralPath (Join-Path $repo $file)).Hash -ne $inputHashes[$file]){throw "Diffuse input changed during validation: $file"}}
$inputHashes['Assets/occlusion']=(Get-FileHash -LiteralPath (Join-Path $repo 'Assets/occlusion')).Hash
$inputHashes|ConvertTo-Json|Set-Content (Join-Path $project 'diffuse-inputs.json')
Get-Content (Join-Path $project 'validation.txt')
