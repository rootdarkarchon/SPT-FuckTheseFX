param([string]$SptInstallPath='D:\Tarkov-SPT-4.1', [string]$AssemblyPath='')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx/core/Mono.Cecil.dll')
$resolver=[Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory((Join-Path $SptInstallPath 'BepInEx/core'))
$resolver.AddSearchDirectory((Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed'))
$reader=[Mono.Cecil.ReaderParameters]::new();$reader.AssemblyResolver=$resolver
if(-not $AssemblyPath){$AssemblyPath=Join-Path $repo 'bin/Release/netstandard2.1/FuckTheseFX.dll'}
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($AssemblyPath,$reader)
try {
    $types=$assembly.MainModule.Types
    $plugin=$types | Where-Object FullName -eq 'FuckTheseFX.Plugin'
    $deps=@($plugin.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'BepInDependency' -and $_.ConstructorArguments[0].Value -eq 'com.matsix.ssrsix' })
    if($deps.Count -ne 0) { throw 'Removed SSRSix soft dependency remains' }
    if($plugin.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'BepInDependency' -and $_.ConstructorArguments[0].Value -match 'ssrprototype' }) { throw 'Retired prototype detection must not ship' }
    if($types | Where-Object { $_.Namespace -eq 'FuckTheseFX.SsrPrototype' -or $_.Name -in @('PrototypePlugin','PrototypeHooks','ComparisonCapture') }) { throw 'Prototype diagnostic/plugin code leaked into release' }
    $integration=$types | Where-Object Name -eq 'StochasticSsrIntegration'
    $conflict=$integration.Methods | Where-Object Name -eq 'HasConflict'
    if($conflict -or ($integration.Fields | Where-Object Name -eq 'SsrsixGuid')) { throw 'Removed SSRSix conflict detection remains' }
    $init=$integration.Methods | Where-Object Name -eq 'Initialize'
    $load=@($init.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'LoadFromFile' })
    if($load.Count -ne 1) { throw 'SSR asset loading missing' }
    foreach($method in $integration.Methods | Where-Object HasBody) {
        if($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and ($_.Operand.Name -eq 'HasConflict' -or $_.Operand.DeclaringType.FullName -eq 'BepInEx.Bootstrap.Chainloader') }) { throw 'Removed runtime SSR compatibility gate remains' }
    }
    $pipeline=$types | Where-Object Name -eq 'SsrPipeline'
    if($pipeline.Methods | Where-Object Name -in @('CaptureDiagnostic','SnapshotNativeTrace','NativeCompose','Resolve','Filter','Finish','BuildReflectionMips')) { throw 'Research-only paths leaked into release pipeline' }
    $markers=$types | Where-Object { $_.Namespace -eq 'FuckTheseFX.Source.StochasticSSR' -and $_.Name -eq 'SsrGpuSample' }
    if($types | Where-Object Name -in @('FidelityDenoiser','AmdReflectionDenoiser','ReflectionDenoiser','ReflectionDirection','ReflectionDepthGapFill')) { throw 'Retired native-signal denoiser remains in release' }
    if($types | Where-Object Namespace -match 'CleanupBaseline|PerformanceBaseline|TraceOptimizationBaseline|ContinuationOptimizationBaseline|HistoryCopyBaseline|HistoryOwnershipBaseline|PreNrdStandard') {throw 'Validation-only baseline leaked into release'}
    $options=$types | Where-Object { $_.Namespace -eq 'FuckTheseFX.Source.StochasticSSR' -and $_.Name -eq 'Options' }
    if(-not ($options.Fields | Where-Object { $_.Name -eq 'EndpointSkyFallback' -and $_.FieldType.FullName -eq 'System.Boolean' })) {throw 'Opt-in endpoint sky fallback runtime option missing'}
    if($options.Fields | Where-Object Name -in @('LimitNativeRange','Distance','BypassHitNormal','GlobalContinuedSky')) {throw 'Removed controls remain in runtime options'}
    if($options.Fields | Where-Object Name -in @('DdaForeground','SimpleMirrorTraversal')) {throw 'Removed traversal experiment remains in runtime options'}
    if($options.Fields | Where-Object Name -match 'Amd|Denoiser|Spatial|HitAware|GapFill|ForegroundSky|ForegroundAdjacent|ForegroundPermissiveness|Shallow|VarianceStrength|Direction|MatchCameraAspect|ConstrainHitToCell|HitNormalConfidenceOnly|RelaxOccluderReturn|SkyAfterContinuation') {throw 'Retired option fields remain'}
    $confidence=$types | Where-Object Name -eq 'SsrReflectionPipeline'
    if($confidence.Fields | Where-Object Name -in @('HistoryRecord','spareRecord')) {throw 'Unpacked source-classification history remains in release'}
    if($confidence.Fields | Where-Object Name -match '^History|^Temporal|Anchor|spare|DepthGapFilled|exactResidual') {throw 'Removed custom temporal storage remains'}
    if(-not ($confidence.Methods | Where-Object Name -eq 'RequireNrd')) {throw 'Mandatory NRD backend gate missing'}
    if($types | Where-Object Name -in @('ConfidenceSsrPipeline','AmbientReflectionCapture')) {throw 'Declined implementation remains in production'}
    if($options.Fields | Where-Object Name -match 'CoverageTemporal|CoverageFilter|CoverageFull|CoverageEnd|WaterNormal|WaterTrace|NormalStart|NormalEnd|NormalRadius|UseCaptured|NrdSpecular') {throw 'Removed settings/options remain'}
    if($confidence.Fields | Where-Object Name -match 'counter|TemporalEvents|Estimate|Filtered|SourceContext|ReuseDiagnostic') {throw 'Retired runtime state remains'}
    if($confidence.Methods | Where-Object Name -match 'Counter|TemporalEvent') {throw 'Continuous diagnostic work remains'}
    if(-not ($types | Where-Object Name -eq 'SsrReflectionPipeline')) { throw 'Reflection pipeline missing' }
    if($types | Where-Object { $_.Name -eq 'ReflectionView' -and ($_.Fields | Where-Object Name -eq 'EnvironmentFallback') }) {throw 'Black-only retired debug view remains'}
    $nrd=$types | Where-Object Name -eq 'NrdSpecularBackend'
    if($nrd.Methods | Where-Object Name -in @('DiagnosticTexture','FTFX_NrdTexture')) {throw 'Retired native diagnostic accessor remains'}
    if($markers.Methods | Where-Object Name -in @('Discover','Snapshot')) { throw 'Research profiler integration leaked into release' }
    $hooks=$types | Where-Object Name -eq 'SsrHooks'
    foreach($name in @('Before','After','Suspend','Prune')) { if(-not ($hooks.Methods | Where-Object Name -eq $name)) {throw "SSR hook missing: $name"} }
    foreach($name in @('Update','OnDisable','OnEnable','OnDestroy')) {
        $method=$plugin.Methods | Where-Object Name -eq $name
        if(-not ($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'StochasticSsrIntegration' })) { throw "SSR lifecycle missing: $name" }
    }
    if(-not ($types | Where-Object { $_.Namespace -eq 'FuckTheseFX.Source.StochasticSSR' -and $_.Name -eq 'SsrSkySource' })) {throw 'Optional CloudSix sky source missing'}
    if(-not ($types | Where-Object { $_.Namespace -eq 'FuckTheseFX.Source.StochasticSSR' -and $_.Name -eq 'CloudSkyProbe' })) {throw 'Optional CloudSix native probe bridge missing'}
    if($assembly.MainModule.AssemblyReferences | Where-Object Name -match 'SsrPrototype|SSRSix|CloudSix') { throw 'Release SSR must not depend on another plugin assembly' }
} finally { $assembly.Dispose();$resolver.Dispose() }
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'))
try {
    $ambient=$game.MainModule.Types | Where-Object Name -eq 'AmbientLight'
    foreach($pair in @(@('_faceNum','System.Int32'),@('_nextRenderTime','System.Single'),@('RenderDelay','System.Single'),@('_cubeRT','UnityEngine.RenderTexture'))) {
        if(-not ($ambient.Fields | Where-Object { $_.Name -eq $pair[0] -and $_.FieldType.FullName -eq $pair[1] })) {throw "Native sky probe field changed: $($pair[0])"}
    }
    $refresh=$ambient.Methods | Where-Object { $_.Name -eq 'method_2' -and $_.Parameters.Count -eq 0 -and $_.ReturnType.FullName -eq 'System.Void' }
    $renders=@($refresh.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'UnityEngine.Camera' -and $_.Operand.Name -eq 'RenderToCubemap' -and $_.Operand.Parameters.Count -eq 2 })
    if($renders.Count -ne 2 -or -not ($refresh.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq '_faceNum' }) -or
       -not ($refresh.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'SetGlobalTexture' })) {throw 'Native sky-only probe update/publish contract changed'}
    foreach($clip in @([single]0.1,[single]0.2)){if(-not ($refresh.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldc.r4' -and $_.Operand -eq $clip })) {throw 'Native sky-only probe clip range changed'}}
    Write-Output 'Verified CloudSix bridge targets the installed sky-only cubemap, native update cadence and global probe publication.'
} finally {$game.Dispose()}
if((Get-FileHash (Join-Path $SptInstallPath 'EscapeFromTarkov_Data/Managed/Unity.Postprocessing.Runtime.dll')).Hash -ne 'B8055ECC0C1A6BA0A04427066A3DEF8C87DA90C1EAC071FA72D8193521441548') { throw 'Native SSR ABI changed' }
$project=Join-Path $repo 'obj/NrdStandardValidation'
$result=Get-Item (Join-Path $project 'validation.txt')
foreach($source in Get-ChildItem (Join-Path $repo 'Source/StochasticSSR') -Filter '*.cs') {
    $tested=Join-Path $project ('Assets/Editor/'+$source.Name)
    if((Get-FileHash $source.FullName).Hash -ne (Get-FileHash $tested).Hash -or $source.LastWriteTimeUtc -gt $result.LastWriteTimeUtc) {throw "Untested release SSR source: $($source.Name)"}
}
foreach($source in @(Get-ChildItem (Join-Path $repo 'unity/StochasticSSR/AMD') -File) + @(Get-Item (Join-Path $repo 'unity/StochasticSSR/HiZ.compute'),(Join-Path $repo 'unity/StochasticSSR/Support.shader'),(Join-Path $repo 'unity/StochasticSSR/Reflection.shader'),(Join-Path $repo 'unity/StochasticSSR/TodReflection.hlsl'),(Join-Path $repo 'unity/StochasticSSR/WaterCapture.shader'),(Join-Path $repo 'unity/StochasticSSR/ReuseBrdf.hlsl'),(Join-Path $repo 'unity/StochasticSSR/RasterState.hlsl'),(Join-Path $repo 'unity/StochasticSSR/NrdInputs.shader'))) {
    $folder=if($source.Directory.Name -eq 'AMD') {'Assets/AMD'}else{'Assets'}
    if((Get-FileHash $source.FullName).Hash -ne (Get-FileHash (Join-Path $project "$folder/$($source.Name)")).Hash -or $source.LastWriteTimeUtc -gt $result.LastWriteTimeUtc) {throw "Untested release SSR asset: $($source.Name)"}
}
if((Get-FileHash (Join-Path $repo 'Assets/stochasticssr')).Hash -ne (Get-FileHash (Join-Path $project 'Output/stochasticssr')).Hash) { throw 'Release SSR bundle differs from validated bundle' }
$reflectionSource=Get-Content (Join-Path $repo 'unity/StochasticSSR/Reflection.shader') -Raw
if($reflectionSource -match '_History|TemporalDisplay|CoverageDisplay|ExactSubtract|_CapturedReflection|_WaterNormalsEnabled') {throw 'Declined shader paths remain'}
$passes=@([regex]::Matches($reflectionSource,'#pragma fragment (\w+)') | ForEach-Object { $_.Groups[1].Value })
if(($passes -join ',') -ne 'Resolve,Compose,ReplayInput,ComposeNormal') {throw 'Compacted reflection pass mapping changed'}
$traceSource=Get-Content (Join-Path $repo 'unity/StochasticSSR/HiZ.compute') -Raw
if($traceSource -match 'FTFX_BYPASS_HIT_NORMAL|BypassHitNormal|_GlobalContinuedSky|FTFX_DDA_FOREGROUND|foregroundDda|SimpleMirror|FTFX_SIMPLE_MIRROR|PrepareWaterNormals|_WaterNormal|_NormalFilter') {throw 'Removed traversal experiment remains in the release shader'}
if($traceSource -notmatch '_ZeroSmoothnessProbe!=0 && smoothness==0' -or $traceSource.IndexOf('_ZeroSmoothnessProbe!=0 && smoothness==0') -ge $traceSource.IndexOf('direction=SampleReflectionVector')) {throw 'Experimental zero-smoothness receivers must bypass GGX before sampling'}
if($reflectionSource -notmatch 'if\(ZeroSmoothnessProbeReceiver\(pixel\)\)return ZeroSmoothnessProbeScene\(pixel,scene\);') {throw 'Experimental probe replacement must bypass SSR reconstruction'}
if($traceSource -match '_Clip\.w>\.5' -or $reflectionSource -match '_ReceiverRange') {throw 'Removed receiver range checks remain'}
$pipelineSource=Get-Content (Join-Path $repo 'Source/StochasticSSR/SsrPipeline.cs') -Raw
if($traceSource -notmatch '#pragma multi_compile_local _ FTFX_TRACE_DIAGNOSTICS' -or $traceSource -match '_DirectionMode|_HitNormalConfidenceOnly|_ConstrainHitToCell') {throw 'Diagnostic variant or fixed tracer behavior mismatch'}
if($traceSource -notmatch '#pragma multi_compile_local _ FTFX_REJECTION_DIAGNOSTICS' -or $traceSource -match 'OctDecode') {throw 'Optional rejection diagnostic variant or shader cleanup mismatch'}
if($pipelineSource -notmatch 'if\(continuedSkyKernel>=0\)') {throw 'All-receiver continued sky must run without water capture'}
if($pipelineSource -notmatch 'SetComputeIntParam\(compute,"_EndpointSkyFallback",o.EndpointSkyFallback\?1:0\)') {throw 'Endpoint sky fallback toggle does not reach the existing finalizer'}
$validation=Get-Content (Join-Path $repo 'obj/NrdStandardValidation/validation.txt') -Raw
foreach($check in @('actual foreground mirror scene recovers endpoint sky for multiple receivers','endpoint fallback disable restores exact frozen pipeline results','seeded foreground reason 11 recovers approximate sky','seeded foreground reason 8 recovers approximate sky','completed sky retains its separate diagnostic classification','enabling endpoint sky fallback invalidates NRD history','disabling endpoint sky fallback invalidates NRD history')) {
    if(-not $validation.Contains($check)) {throw "Missing executed endpoint sky validation: $check"}
}
$rasterSource=Get-Content (Join-Path $repo 'Source/StochasticSSR/SsrRasterState.cs') -Raw
$rasterShader=Get-Content (Join-Path $repo 'unity/StochasticSSR/RasterState.hlsl') -Raw
if($rasterSource -notmatch 'ComputeBuffer\(16,16\)' -or $rasterSource -match 'Store\((16|20|24),' -or $rasterShader -match 'SsrRasterHistory|SsrHistoryUV') {throw 'Unused temporal raster uploads/helpers remain'}
if($reflectionSource -match '\b_Options\b|view==17') {throw 'Retired reflection parameters/debug path remain'}
$retiredFixtureReferences=Get-ChildItem (Join-Path $repo 'unity/StochasticSsrValidation') -Filter '*.cs' | Where-Object { $_.Name -ne 'NrdStandardValidation.cs' -and (Get-Content $_.FullName -Raw) -match 'ConfidenceSsrPipeline|AmbientReflectionCapture|partial class StochasticSsrValidation' }
if($retiredFixtureReferences -or (Test-Path (Join-Path $repo 'scripts/Test-SsrProfilerInterop.ps1'))) {throw 'Stale validation wiring remains outside archive'}
if(Get-ChildItem (Join-Path $repo 'unity/StochasticSSR') -Recurse -File | Where-Object Name -match 'Denoiser|HitReuse|ffx_denoiser') {throw 'Retired production shader dependencies remain'}
Write-Output 'Verified SSR compatibility, lifecycle, release-only code, pinned ABI and validated source/bundle hashes.'

if((Get-FileHash (Join-Path $repo 'Assets/nrdssr')).Hash -ne (Get-FileHash (Join-Path $project 'Output/nrdssr')).Hash) {throw 'NRD bundle differs from validated bundle'}
if((Get-FileHash (Join-Path $repo 'obj/NRD/bridge/Release/FTFXNrd.dll')).Hash -ne (Get-FileHash (Join-Path $project 'Native/FTFXNrd.dll')).Hash) {throw 'Native NRD DLL differs from tested DLL'}
if((Get-FileHash (Join-Path $repo 'unity/StochasticSsrValidation/NrdStandardValidation.cs')).Hash -ne (Get-FileHash (Join-Path $project 'Assets/Editor/NrdStandardValidation.cs')).Hash) {throw 'Untested NRD removal validation'}
if((Get-FileHash (Join-Path $repo 'unity/StochasticSsrValidation/CloudProbeAudit.shader')).Hash -ne (Get-FileHash (Join-Path $project 'Assets/CloudProbeAudit.shader')).Hash) {throw 'Untested cubemap sampling audit'}
