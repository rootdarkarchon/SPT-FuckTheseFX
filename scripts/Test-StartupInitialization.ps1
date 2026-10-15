param([string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx/core/Mono.Cecil.dll')
$built = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $repo 'bin/Release/netstandard2.1/FuckTheseFX.dll'))
try {
    $modules = @{
        'Assembly-CSharp' = 'cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7'
        'Unity.Postprocessing.Runtime' = 'bb1716ca-81b5-4288-9a04-95ef431a6ddf'
    }
    foreach ($name in $modules.Keys) {
        $installed = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath "EscapeFromTarkov_Data/Managed/$name.dll"))
        try {
            if ($installed.MainModule.Mvid.ToString() -ne $modules[$name]) { throw "Unsupported startup module: $name" }
        } finally { $installed.Dispose() }
    }
    $guards = @{
        'DistantShadowSamplingIntegration' = $modules['Assembly-CSharp']
        'WaterSurfaceIntegration' = $modules['Assembly-CSharp']
        'AmbientOcclusionBridge' = $modules['Assembly-CSharp']
        'StochasticSsrIntegration' = $modules['Unity.Postprocessing.Runtime']
    }
    foreach ($name in $guards.Keys) {
        $type = $built.MainModule.Types | Where-Object Name -eq $name
        $init = $type.Methods | Where-Object Name -eq 'Initialize'
        $code = @($init.Body.Instructions)
        $pin = @($code | Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq $guards[$name] })
        $read = @($code | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'get_ModuleVersionId' })
        $compare = @($code | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'System.Guid' -and $_.Operand.Name -eq 'op_Inequality' })
        $firstPatch = $code | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'HarmonyLib.Harmony' -and $_.Operand.Name -eq 'Patch' } | Select-Object -First 1
        if ($pin.Count -ne 1 -or $read.Count -ne 1 -or $compare.Count -ne 1 -or -not $firstPatch -or $compare[0].Offset -ge $firstPatch.Offset) { throw "Missing pre-patch MVID guard: $name" }
        $branch = $compare[0].Next
        if ($branch.OpCode.Name -notmatch '^brfalse' -or $branch.Next.OpCode.Name -ne 'ldstr' -or $branch.Next.Next.OpCode.Name -ne 'newobj' -or $branch.Next.Next.Next.OpCode.Name -ne 'throw') { throw "MVID mismatch does not fail before patching: $name" }
    }
    # Check the actual release IL, including generated/nested types. No runtime
    # hash provider, validation file reads, worker or delayed equivalent may ship.
    foreach ($type in $built.MainModule.GetTypes()) {
        if ($type.Name -in @('AssetVerification','NativeSha256')) { throw 'Unneeded hashing helper remains' }
        foreach ($method in $type.Methods | Where-Object HasBody) {
            if ($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and ($_.Operand.DeclaringType.Namespace -eq 'System.Security.Cryptography' -or ($_.Operand.DeclaringType.FullName -eq 'System.IO.File' -and $_.Operand.Name -eq 'OpenRead')) }) { throw "Runtime content hashing/file verification remains: $($method.FullName)" }
        }
    }
    $plugin = $built.MainModule.Types | Where-Object FullName -eq 'FuckTheseFX.Plugin'
    $awake = $plugin.Methods | Where-Object Name -eq 'Awake'
    $features = @($awake.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'FeatureStartup' -and $_.Operand.Name -eq 'TryInitialize' })
    if ($features.Count -ne 12 -or ($plugin.Methods | Where-Object Name -eq 'Start')) { throw 'Early feature installation moved or omitted' }
    Write-Output 'Startup contracts passed: four exact module gates fail before Harmony patching; all 12 features remain in Awake; no runtime hash providers or verification file reads.'
} finally { $built.Dispose() }
