param([string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx\core\Mono.Cecil.dll')
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'))
$post = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath 'EscapeFromTarkov_Data\Managed\Unity.Postprocessing.Runtime.dll'))
try {
    $bloom = $game.MainModule.Types | Where-Object FullName -eq 'UltimateBloom'
    $methods = @($bloom.Methods | Where-Object Name -eq 'OnRenderImageInternal')
    if ($methods.Count -ne 1 -or $methods[0].IsStatic -or $methods[0].ReturnType.FullName -ne 'System.Void' -or
        $methods[0].Parameters.Count -ne 2 -or @($methods[0].Parameters | Where-Object { $_.ParameterType.FullName -ne 'UnityEngine.RenderTexture' }).Count) {
        throw 'Unsupported UltimateBloom internal render signature'
    }
    $instructions = $methods[0].Body.Instructions
    $combine = @()
    for ($i = 0; $i -lt $instructions.Count - 2; $i++) {
        $a = $instructions[$i]; $b = $instructions[$i+1]; $c = $instructions[$i+2]
        if ($a.OpCode.Name -eq 'ldsfld' -and $a.Operand.Name -eq '_colorBuffer' -and
            $b.OpCode.Name -eq 'ldarg.1' -and $c.Operand -is [Mono.Cecil.MethodReference] -and $c.Operand.Name -eq 'SetTexture') {
            $combine += $c.Operand.DeclaringType.FullName
        }
    }
    if (($combine | Sort-Object) -join '|' -ne 'UnityEngine.Material|UnityEngine.MaterialPropertyBlock') {
        throw "Native bloom composition source contract changed: $combine"
    }
    $execute = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq 'System.Void UnityEngine.Graphics::ExecuteCommandBuffer(UnityEngine.Rendering.CommandBuffer)' })
    $release = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'ReleaseTemporary' })
    if ($execute.Count -ne 1 -or $release.Count -eq 0 -or $execute[0].Offset -gt $release[0].Offset) {
        throw 'Native bloom no longer executes its buffer before releasing temporaries'
    }
    $wrapper = $bloom.Methods | Where-Object Name -eq 'OnRenderImage'
    $redirect = @($wrapper.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'GetSourceDestination' })
    $render = @($wrapper.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'OnRenderImageInternal' })
    if ($redirect.Count -ne 1 -or $render.Count -ne 1 -or $redirect[0].Offset -gt $render[0].Offset) { throw 'Bloom source redirection order changed' }
    $ssaa = $post.MainModule.Types | Where-Object FullName -eq 'SSAA'
    foreach ($name in @('GetInputWidth', 'GetInputHeight')) {
        $method = @($ssaa.Methods | Where-Object { $_.Name -eq $name -and $_.Parameters.Count -eq 0 -and $_.ReturnType.FullName -eq 'System.Int32' -and -not $_.IsStatic })
        if ($method.Count -ne 1) { throw "Missing SSAA.$name contract" }
    }
    Write-Output 'Installed bloom contracts verified: post-redirection hook, both original-scene bindings, deferred execution lifetime and SSAA input dimensions.'
} finally { $game.Dispose(); $post.Dispose() }
