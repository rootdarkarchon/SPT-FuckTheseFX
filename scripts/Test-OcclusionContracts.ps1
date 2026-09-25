param([string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx\core\Mono.Cecil.dll')
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'))
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory((Join-Path $SptInstallPath 'BepInEx\core'))
$resolver.AddSearchDirectory((Join-Path $SptInstallPath 'EscapeFromTarkov_Data\Managed'))
$reader=[Mono.Cecil.ReaderParameters]::new();$reader.AssemblyResolver=$resolver
$plugin=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $repo 'bin\Release\netstandard2.1\FuckTheseFX.dll'),$reader)
try {
 $manager=$game.MainModule.Types | Where-Object FullName -eq 'EFT.CameraControl.CameraManager'
 $ssao=@($manager.Methods | Where-Object { $_.Name -eq 'SetSSAO' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'EFT.Settings.Graphics.ESSAOMode' })
 if($ssao.Count -ne 1 -or $ssao[0].ReturnType.FullName -ne 'System.Void') { throw 'Unsupported CameraManager.SetSSAO' }
 foreach($name in @('_hbao','_ambientOcclusion')) { if(-not ($manager.Fields | Where-Object Name -eq $name)) { throw "Missing native AO field: $name" } }
 $ambient=$game.MainModule.Types | Where-Object FullName -eq 'AmbientLight'
 if(-not ($ambient.Fields | Where-Object { $_.Name -eq '_screenAmbientMaterial' -and $_.FieldType.FullName -eq 'UnityEngine.Material' })) { throw 'Missing ambient material' }
 if(-not ($ambient.Methods | Where-Object { $_.Name -eq 'InitAmbientBuffer' -and $_.Parameters.Count -eq 2 -and $_.Parameters[0].ParameterType.FullName -eq 'AmbientLight/CamSettings' -and $_.Parameters[1].ParameterType.FullName -eq 'UnityEngine.Camera' })) { throw 'Unsupported ambient hook' }
 $init=$ambient.Methods | Where-Object Name -eq 'InitAmbientBuffer'
 $draws=@($init.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'callvirt' -and $_.Operand.Name -eq 'DrawMesh' -and $_.Operand.DeclaringType.FullName -eq 'UnityEngine.Rendering.CommandBuffer' -and $_.Operand.Parameters.Count -eq 6 })
 if($draws.Count -ne 1) { throw 'Unsupported CustomAmbient diagnostic draw layout' }
 $highlight=$game.MainModule.Types | Where-Object FullName -eq 'AmbientHighlight'
 $stencil=$highlight.NestedTypes | Where-Object Name -eq 'StencilType'
 foreach($pair in @(@('Static',0),@('Characters',1),@('Hands',2))) {
  $field=$stencil.Fields | Where-Object Name -eq $pair[0]
  if($null -eq $field -or $field.Constant -ne $pair[1]) { throw 'Unsupported actor stencil classes' }
 }
 $type=$plugin.MainModule.Types | Where-Object FullName -eq 'FuckTheseFX.Plugin'
 if($type.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'BepInIncompatibility' -and $_.ConstructorArguments[0].Value -eq 'com.matsix.sptaoreplacer' }) { throw 'General AOReplacer incompatibility is forbidden' }
 $soft=$type.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'BepInDependency' -and $_.ConstructorArguments[0].Value -eq 'com.matsix.sptaoreplacer' }
 if(-not $soft -or $soft.ConstructorArguments[1].Value -ne 2) { throw 'Missing AOReplacer soft load ordering' }
 $awake=$type.Methods | Where-Object Name -eq 'Awake'
 $ops=$awake.Body.Instructions
 $guard=@($ops | Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq 'com.matsix.sptaoreplacer' })
 if($guard.Count -ne 1) { throw 'Missing installed-plugin guard' }
 $i=$ops.IndexOf($guard[0])
 if($ops[$i+1].Operand.Name -ne 'ContainsKey' -or $ops[$i+2].OpCode.Name -notmatch '^brfalse') { throw 'AOReplacer presence does not guard AO initialization' }
 $disabledStart=$i+3
 $enabledStart=$ops.IndexOf($ops[$i+2].Operand)
 if($enabledStart -le $disabledStart) { throw 'Unexpected guard flow' }
 if(@($ops | Select-Object -Skip $disabledStart -First ($enabledStart-$disabledStart) | Where-Object { $_.OpCode.Name -eq 'newobj' }).Count) { throw 'AO resources initialized despite AOReplacer' }
 if($ops[$enabledStart-1].OpCode.Name -ne 'ret') { throw 'AOReplacer guard does not return before AO initialization' }
 if($plugin.MainModule.Types | Where-Object FullName -match 'AmplifyGTAO|AmplifyOcclusion') { throw 'Amplify implementation leaked into FTFX' }
 if($type.Methods | Where-Object Name -eq 'ImportLegacySettings') { throw 'Legacy import remains' }
 Write-Output 'Verified installed AO/ambient/stencil ABI, soft load ordering, complete AO bypass when AOReplacer is present, and no settings import.'
} finally { $game.Dispose();$plugin.Dispose();$resolver.Dispose() }
$hash=(Get-FileHash (Join-Path $SptInstallPath 'EscapeFromTarkov_Data\StreamingAssets\Windows\shaders')).Hash
if($hash -ne 'D41092507FC8CADCDC3DB1D53B9F1E0CB9FC7061AD07FF3C980284731AE2091C') { throw 'Unsupported native ambient shader bundle' }
