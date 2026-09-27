param([string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $SptInstallPath 'BepInEx\core\Mono.Cecil.dll')))
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'))
try {
    $abstractGame = $game.MainModule.Types | Where-Object FullName -eq 'EFT.AbstractGame'
    $localGame = $game.MainModule.Types | Where-Object FullName -eq 'EFT.BaseLocalGame`1'
    foreach ($contract in @(
        @('EFT.BaseLocalGame`1', 'get_PlayerOwner', 'TPlayerOwner'),
        @('EFT.PlayerOwner', 'get_Player', 'EFT.Player'),
        @('EFT.Player', 'get_GameWorld', 'EFT.GameWorld')
    )) {
        $type = $game.MainModule.Types | Where-Object FullName -eq $contract[0]
        $getter = @($type.Methods | Where-Object Name -eq $contract[1])
        if ($getter.Count -ne 1 -or -not $getter[0].IsPublic -or $getter[0].IsStatic -or
            $getter[0].Parameters.Count -ne 0 -or $getter[0].ReturnType.FullName -ne $contract[2]) {
            throw "Native loading readiness contract changed: $($contract[0]).$($contract[1])"
        }
    }
    $nativeRun = $localGame.NestedTypes | Where-Object Name -eq 'CG_Run'
    $runIl = @((($nativeRun.Methods | Where-Object Name -eq 'MoveNext').Body.Instructions))
    $ownerWrite = @($runIl | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq '_playerOwner' })
    $lootCall = @($runIl | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SpawnLoot' })
    if ($ownerWrite.Count -ne 1 -or $lootCall.Count -ne 1 -or $ownerWrite[0].Offset -ge $lootCall[0].Offset) { throw 'Native player-owner startup order changed' }
    foreach ($property in @('Status','GameType')) {
        $getter = @($abstractGame.Methods | Where-Object Name -eq "get_$property")
        if ($getter.Count -ne 1 -or -not $getter[0].IsPublic -or $getter[0].IsStatic -or $getter[0].Parameters.Count -ne 0) { throw "Missing raid gate getter: $property" }
    }
    $status = $game.MainModule.Types | Where-Object FullName -eq 'EFT.GameStatus'
    if (($status.Fields | Where-Object Name -eq 'Started').Constant -ne 4) { throw 'Started state contract changed' }
    $gameType = $game.MainModule.Types | Where-Object FullName -eq 'EFT.EGameType'
    if (-not ($gameType.Fields | Where-Object Name -eq 'Hideout')) { throw 'Hideout discriminator missing' }
    $comfort = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath 'EscapeFromTarkov_Data\Managed\Comfort.dll'))
    try {
        $singleton = $comfort.MainModule.Types | Where-Object FullName -eq 'Comfort.Common.Singleton`1'
        foreach ($name in @('get_Instance','get_Instantiated')) {
            if (-not ($singleton.Methods | Where-Object { $_.Name -eq $name -and $_.IsPublic -and $_.IsStatic })) { throw "Missing singleton getter: $name" }
        }
    } finally { $comfort.Dispose() }
    $group = $game.MainModule.Types | Where-Object FullName -eq 'Koenigz.PerfectCulling.PerfectCullingBakeGroup'
    foreach ($pair in @(@('Toggle','set_enabled'),@('RuntimeSetForceRenderingOff','set_forceRenderingOff'))) {
        $method = @($group.Methods | Where-Object Name -eq $pair[0])
        if ($method.Count -ne 1 -or $method[0].IsStatic -or $method[0].Parameters.Count -ne 1 -or $method[0].Parameters[0].ParameterType.FullName -ne 'System.Boolean') { throw "Unsupported $($pair[0])" }
        $il = $method[0].Body.Instructions
        foreach ($field in @('_runtimeGroupData','runtimeGroupDataSize')) {
            if (-not ($il | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq $field })) { throw "Runtime list contract changed: $field" }
        }
        if (-not ($il | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'UnityEngine.Renderer' -and $_.Operand.Name -eq $pair[1] })) { throw "Renderer setter missing: $($pair[1])" }
    }
    $init = @($group.Methods | Where-Object Name -eq 'Init')
    if ($init.Count -ne 1 -or $init[0].Parameters.Count -ne 0 -or $init[0].ReturnType.FullName -ne 'System.Int32') { throw 'Init contract changed' }
    if (-not ($init[0].Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.Name -eq '_runtimeGroupData' })) { throw 'Init no longer rebuilds runtime data' }
    $content = $game.MainModule.Types | Where-Object FullName -eq 'Koenigz.PerfectCulling.EFT.PerfectCullingCrossSceneContent'
    $distance = $content.Methods | Where-Object Name -eq 'OnBakedLODVisbilityChanged'
    if (-not ($distance.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'RuntimeSetForceRenderingOff' })) { throw 'Baked LOD route changed' }
    foreach ($name in @('cullingLightObjects','analyticSources','screenDistanceSwitcher')) {
        $toggle = $group.Methods | Where-Object Name -eq 'Toggle'
        if (-not ($toggle.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq $name })) { throw "Independent native path changed: $name" }
    }
    Write-Output 'Glass contracts verified: native player-owner readiness before loot setup, raid gate/singletons, runtime renderer lists, enabled/force-off routes, Init rebuild, baked LOD route and separate light/analytic paths.'
} finally { $game.Dispose() }
