param([string]$SptInstallPath='D:\Tarkov-SPT-4.1')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx/core/Mono.Cecil.dll')
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory((Join-Path $SptInstallPath 'BepInEx/core'))
$resolver.AddSearchDirectory((Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed'))
$reader=[Mono.Cecil.ReaderParameters]::new();$reader.AssemblyResolver=$resolver
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $repo 'bin/Release/netstandard2.1/FuckTheseFX.dll'),$reader)
try {
    $types=$assembly.MainModule.Types
    $plugin=$types | Where-Object FullName -eq 'FuckTheseFX.Plugin'
    $deps=@($plugin.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'BepInDependency' -and $_.ConstructorArguments[0].Value -eq 'com.matsix.ssrsix' })
    if($deps.Count -ne 1 -or $deps[0].ConstructorArguments[1].Value -ne 2) { throw 'SSRSix soft dependency missing' }
    if($plugin.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'BepInDependency' -and $_.ConstructorArguments[0].Value -match 'ssrprototype' }) { throw 'Retired prototype detection must not ship' }
    if($types | Where-Object { $_.Namespace -eq 'FuckTheseFX.SsrPrototype' -or $_.Name -in @('PrototypePlugin','PrototypeHooks','ComparisonCapture') }) { throw 'Prototype diagnostic/plugin code leaked into release' }
    $integration=$types | Where-Object Name -eq 'StochasticSsrIntegration'
    $conflict=$integration.Methods | Where-Object Name -eq 'HasConflict'
    if(-not ($conflict.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq 'com.matsix.ssrsix' })) { throw 'SSRSix identity changed' }
    $init=$integration.Methods | Where-Object Name -eq 'Initialize'
    $guard=@($init.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'HasConflict' })
    $load=@($init.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'LoadFromFile' })
    if($guard.Count -ne 1 -or $load.Count -ne 1 -or $guard[0].Offset -ge $load[0].Offset) { throw 'SSR compatibility gate must precede asset loading' }
    $pipeline=$types | Where-Object Name -eq 'SsrPipeline'
    if($pipeline.Methods | Where-Object Name -in @('CaptureDiagnostic','SnapshotNativeTrace','NativeCompose','Resolve','Filter','Finish','BuildReflectionMips')) { throw 'Research-only paths leaked into release pipeline' }
    $markers=$types | Where-Object { $_.Namespace -eq 'FuckTheseFX.Source.StochasticSSR' -and $_.Name -eq 'SsrGpuSample' }
    if($markers.Methods | Where-Object Name -in @('Discover','Snapshot')) { throw 'Research profiler integration leaked into release' }
    $hooks=$types | Where-Object Name -eq 'SsrHooks'
    foreach($name in @('Before','After','TracePass','Suspend','Prune')) { if(-not ($hooks.Methods | Where-Object Name -eq $name)) {throw "SSR hook missing: $name"} }
    foreach($name in @('Update','OnDisable','OnEnable','OnDestroy')) {
        $method=$plugin.Methods | Where-Object Name -eq $name
        if(-not ($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'StochasticSsrIntegration' })) { throw "SSR lifecycle missing: $name" }
    }
    if($assembly.MainModule.AssemblyReferences | Where-Object Name -match 'SsrPrototype|SSRSix') { throw 'Release SSR must not depend on another plugin assembly' }
} finally { $assembly.Dispose();$resolver.Dispose() }
if((Get-FileHash (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Unity.Postprocessing.Runtime.dll')).Hash -ne 'B8055ECC0C1A6BA0A04427066A3DEF8C87DA90C1EAC071FA72D8193521441548') { throw 'Native SSR ABI changed' }
$project=Join-Path $repo 'obj/StochasticSsrValidation'
$result=Get-Item (Join-Path $project 'validation.txt')
foreach($source in Get-ChildItem (Join-Path $repo 'Source/StochasticSSR') -Filter '*.cs') {
    $tested=Join-Path $project ('Assets/Editor/'+$source.Name)
    if((Get-FileHash $source.FullName).Hash -ne (Get-FileHash $tested).Hash -or $source.LastWriteTimeUtc -gt $result.LastWriteTimeUtc) {throw "Untested release SSR source: $($source.Name)"}
}
foreach($source in @(Get-ChildItem (Join-Path $repo 'unity/StochasticSSR/AMD') -File) + @(Get-Item (Join-Path $repo 'unity/StochasticSSR/HiZ.compute'),(Join-Path $repo 'unity/StochasticSSR/Support.shader'),(Join-Path $repo 'unity/StochasticSSR/FidelityDenoiser.compute'))) {
    $folder=if($source.Directory.Name -eq 'AMD') {'Assets/AMD'}else{'Assets'}
    if((Get-FileHash $source.FullName).Hash -ne (Get-FileHash (Join-Path $project "$folder/$($source.Name)")).Hash -or $source.LastWriteTimeUtc -gt $result.LastWriteTimeUtc) {throw "Untested release SSR asset: $($source.Name)"}
}
if((Get-FileHash (Join-Path $repo 'Assets/stochasticssr')).Hash -ne (Get-FileHash (Join-Path $project 'Output/stochasticssr')).Hash) { throw 'Release SSR bundle differs from validated bundle' }
Write-Output 'Verified SSR compatibility, lifecycle, release-only code, pinned ABI and validated source/bundle hashes.'
