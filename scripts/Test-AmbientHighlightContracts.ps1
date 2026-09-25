param([string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx\core\Mono.Cecil.dll')
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'))
try {
    $type = $game.MainModule.Types | Where-Object FullName -eq 'AmbientHighlight'
    foreach ($spec in @(@('ManualOnRenderObject','UnityEngine.Camera'), @('UpdateAmbientBuffer','UnityEngine.Rendering.CommandBuffer,UnityEngine.Camera'), @('SetSH','UnityEngine.Rendering.SphericalHarmonicsL2'))) {
        $method = $type.Methods | Where-Object Name -eq $spec[0]
        if (-not $method -or $method.IsStatic -or $method.ReturnType.FullName -ne 'System.Void' -or ($method.Parameters.ParameterType.FullName -join ',') -ne $spec[1]) { throw "Highlight hook changed: $($spec[0])" }
    }
    $manager = $type.Fields | Where-Object Name -eq '_ambientBufferManager'
    if ($manager.FieldType.FullName -ne 'CommandBufferManager') { throw 'Highlight manager field changed' }
    $destroy = ($game.MainModule.Types | Where-Object FullName -eq 'CommandBufferManager').Methods | Where-Object Name -eq 'DestroyBuffers'
    if (-not $destroy.IsPublic -or -not ($destroy.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'RemoveCommandBuffer' })) { throw 'Native buffer detach contract changed' }
    $render = $type.Methods | Where-Object Name -eq 'ManualOnRenderObject'
    if (-not ($render.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'UpdateAmbientBuffer' })) { throw 'Highlight recording path changed' }
    Write-Output 'Ambient Highlight contracts verified: lighting and draw hooks, buffer manager ownership and native camera buffer detachment.'
} finally { $game.Dispose() }
