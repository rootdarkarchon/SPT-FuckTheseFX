param([string]$SptInstallPath='D:\Tarkov-SPT-4.1')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
foreach($name in @('Mono.Cecil.dll','0Harmony.dll','BepInEx.dll')) { Add-Type -Path (Join-Path $SptInstallPath "BepInEx/core/$name") }
Add-Type -Path (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/UnityEngine.CoreModule.dll')
$gamePath=Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($gamePath)
$builtPath=Join-Path $repo 'bin/Release/netstandard2.1/FuckTheseFX.dll'
$built=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($builtPath)
try {
    if($game.MainModule.Mvid.ToString() -ne 'cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7') {throw 'Unsupported water ABI'}
    $type=$game.MainModule.Types|Where-Object FullName -eq 'WaterSSR.WaterRendererv3'
    $render=$type.Methods|Where-Object Name -eq method_2
    if($render.IsStatic -or $render.ReturnType.FullName -ne 'System.Void' -or $render.Parameters.Count -ne 1 -or $render.Parameters[0].ParameterType.FullName -ne 'UnityEngine.Camera') {throw 'Water hook signature changed'}
    $manager=$type.Fields|Where-Object Name -eq commandBufferManager
    if($manager.FieldType.FullName -ne 'CommandBufferManager') {throw 'Water manager field changed'}
    $cleanup=($game.MainModule.Types|Where-Object Name -eq CommandBufferManager).Methods|Where-Object Name -eq DestroyBuffers
    if(-not $cleanup.IsPublic -or -not ($cleanup.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'RemoveCommandBuffer'})) {throw 'Native detach contract changed'}
    $disable=$type.Methods|Where-Object Name -eq OnDisable
    $detach=@($disable.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'DestroyBuffers'})
    $destroy=@($disable.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Destroy'})
    if($detach.Count -ne 1 -or $destroy.Count -ne 1 -or $detach[0].Offset -ge $destroy[0].Offset){throw 'Native material/buffer teardown order changed'}

    $draws=@($render.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'DrawMesh'})
    if($draws.Count -ne 2 -or $draws[0].Operand.Parameters.Count -ne 6 -or $draws[1].Operand.Parameters.Count -ne 5){throw 'Native two-pass water draw layout changed'}
    if(-not ($render.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'GetSettingsBlock'})){throw 'Native settings block path changed'}
    $water=$built.MainModule.Types|Where-Object Name -eq WaterSurfaceIntegration
    foreach($method in $water.Methods|Where-Object HasBody) {
        if($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -match '^FindObjects|^set_mipMapBias$|^GetTemporaryRT$|^Blit$'}){throw 'Water introduced a scene scan, texture mutation or extra copy/allocation'}
    }
    if((Get-FileHash (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/sharedassets15.assets')).Hash -ne '266A9AD217731AC3953E6603BDD5D8648F143DCEE976C76A4783C74891876550') {throw 'Water reference assets changed'}
    $hashes=Get-Content (Join-Path $repo 'obj/WaterValidation/validated-hashes.json') -Raw|ConvertFrom-Json
    foreach($p in $hashes.PSObject.Properties){if((Get-FileHash (Join-Path $repo $p.Name)).Hash -ne $p.Value){throw "Water GPU/lifecycle validation is stale: $($p.Name)"}}
    Write-Output 'Water contracts passed: installed two-pass draw and settings layout matches; native detach order and pinned assets verified; GPU/lifecycle source and bundle hashes match.'
} finally {$game.Dispose();$built.Dispose()}
