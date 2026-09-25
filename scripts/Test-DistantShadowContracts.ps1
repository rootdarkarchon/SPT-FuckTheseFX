param([string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx\core\Mono.Cecil.dll')
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'))
$built = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $repo 'bin\Release\netstandard2.1\FuckTheseFX.dll'))
try {
    $type = $game.MainModule.Types | Where-Object FullName -eq 'DistantShadow'
    $method = $type.Methods | Where-Object Name -eq 'SetupComputeShadowMaskCommandBuffer'
    if ($method.Parameters.Count -ne 13 -or $method.Parameters[7].ParameterType.FullName -ne 'DistantShadow/ResolutionState') { throw 'Mask hook signature changed' }
    $field = $type.Fields | Where-Object Name -eq '_computeMaskCmdBuf'
    $update = $type.Methods | Where-Object Name -eq 'Update'
    if ($update.Parameters.Count -ne 0 -or $update.ReturnType.FullName -ne 'System.Void' -or
        -not ($update.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldfld' -and $_.Operand.Name -eq 'CurrentMaskResolution' })) { throw 'Resolution update contract changed' }
    if ($field.FieldType.FullName -ne 'UnityEngine.Rendering.CommandBuffer') { throw 'Main buffer field changed' }
    $optic = $type.Fields | Where-Object Name -eq '_computeMaskOpticCmdBuf'
    $halton = $type.Fields | Where-Object Name -eq '_haltonOffsetsBuffer'
    if ($optic.FieldType.FullName -ne 'UnityEngine.Rendering.CommandBuffer' -or $halton.FieldType.FullName -ne 'UnityEngine.ComputeBuffer') { throw 'Optic mask/offset buffer contract changed' }
    $recordings = @($update.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SetupComputeShadowMaskCommandBuffer' })
    if ($recordings.Count -ne 2 -or -not ($update.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq '_computeMaskOpticCmdBuf' })) { throw 'Main/optic recording path changed' }
    if (-not ($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SetGlobalBuffer' })) { throw 'Native scope offset binding missing' }
    $matches = 0; $draws = 0
    $il = $method.Body.Instructions
    for ($i = 3; $i -lt $il.Count; $i++) {
        $instruction = $il[$i]
        if ($instruction.Operand -isnot [Mono.Cecil.MethodReference] -or $instruction.Operand.Name -ne 'DrawMesh') { continue }
        $draws++
        if ($instruction.Operand.Parameters.Count -eq 5 -and $il[$i-1].OpCode.Name -eq 'ldc.i4.0' -and
            $il[$i-2].OpCode.Name -eq 'ldc.i4.0' -and $il[$i-3].Operand.Name -eq 'ComputeShadowMaskMaterial') { $matches++ }
    }
    if ($matches -ne 1 -or $draws -ne 6) { throw "Native draw layout changed: $matches primary / $draws total" }
    $integration = $built.MainModule.Types | Where-Object Name -eq 'DistantShadowSamplingIntegration'
    $selector = $integration.Methods | Where-Object Name -eq 'SelectMaterial'
    if ($selector.ReturnType.FullName -ne 'UnityEngine.Material' -or $selector.Parameters.Count -ne 4) { throw 'Material selector contract failed' }
    $postfix = $integration.Methods | Where-Object Name -eq 'AfterRecording'
    if ($postfix.Parameters.Count -ne 14 -or $postfix.Parameters[8].ParameterType.FullName -ne 'DistantShadow/ResolutionState') { throw 'Recording state argument mismatch' }
    foreach ($call in @('SetGlobalInt','SetGlobalVectorArray')) {
        if (-not ($selector.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $call -and $_.Operand.DeclaringType.FullName -eq 'UnityEngine.Rendering.CommandBuffer' })) { throw "Missing command-ordered $call" }
    }
    $resolution = $type.NestedTypes | Where-Object Name -eq ResolutionState
    foreach ($pair in @(@('QUARTER',0),@('HALF',1),@('FULL',2))) {
        if (($resolution.Fields | Where-Object Name -eq $pair[0]).Constant -ne $pair[1]) { throw 'Resolution enum changed' }
    }
    $resourceHash = (Get-FileHash (Join-Path $SptInstallPath 'EscapeFromTarkov_Data\resources.assets')).Hash
    if ($resourceHash -ne '78F26AFBB461338ADC1DBE13F9E5006EE5D35ED5AE55E4BEE624EF62AF4E5FE0') { throw 'Unsupported installed distant-shadow assets' }
    Write-Output 'Distant shadow contracts verified: native pass-0 call site, unchanged six-draw layout, argument mapping, main/optic buffers, scoped offset binding, command-ordered sampling, resolution enum and installed shader hash.'
} finally { $game.Dispose(); $built.Dispose() }
