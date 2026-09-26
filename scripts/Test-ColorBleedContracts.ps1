param([string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx\core\Mono.Cecil.dll')
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'))
$post = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath 'EscapeFromTarkov_Data\Managed\Unity.Postprocessing.Runtime.dll'))
try {
    $ambient = $game.MainModule.Types | Where-Object FullName -eq 'AmbientLight'
    $render = $ambient.Methods | Where-Object Name -eq 'ManualOnRenderObject'
    $ops = $render.Body.Instructions
    $found = $false
    for ($i = 1; $i -lt $ops.Count - 1; $i++) {
        if ($ops[$i].OpCode.Name -eq 'ldstr' -and $ops[$i].Operand -eq 'Custom Ambient' -and
            $ops[$i-1].OpCode.Name -eq 'ldc.i4.7' -and $ops[$i+1].Operand.Name -eq 'FindOrCreate') { $found = $true }
    }
    if (-not $found) { throw 'CustomAmbient is no longer attached at AfterLighting.' }
    $layer = $post.MainModule.Types | Where-Object FullName -eq 'UnityEngine.Rendering.PostProcessing.PostProcessLayer'
    $init = $layer.Methods | Where-Object Name -eq 'InitLegacy'
    $ops = $init.Body.Instructions
    $events = @{}
    for ($i = 3; $i -lt $ops.Count; $i++) {
        if ($ops[$i].Operand.Name -eq 'AddCommandBuffer' -and $ops[$i-1].Operand -is [Mono.Cecil.FieldReference]) {
            $load = $ops[$i-3]
            $value = if ($load.OpCode.Name -eq 'ldc.i4.s') { [int]$load.Operand } else { -1 }
            $events[$ops[$i-1].Operand.Name] = $value
        }
    }
    if ($events['m_LegacyCmdBufferOpaque'] -ne 12 -or $events['m_LegacyCmdBuffer'] -ne 18) { throw 'Post-processing attachment points changed.' }
    $builtins = $layer.Methods | Where-Object Name -eq 'RenderBuiltins'
    foreach ($effect in @('AutoExposure','Bloom','ColorGrading')) {
        if (-not ($builtins.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -match "RenderEffect<UnityEngine.Rendering.PostProcessing.$effect>" })) { throw "Missing downstream $effect contract." }
    }
    Write-Output 'Verified installed CustomAmbient AfterLighting, opaque processing event 12, later event 18 and exposure/bloom/color-grading contracts.'
} finally { $game.Dispose(); $post.Dispose() }
