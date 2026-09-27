param([string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$assets = @('distantshadows','occlusion','stochasticssr')
& dotnet build (Join-Path $repo 'FuckTheseFX.csproj') -c Release "-p:SptInstallPath=$SptInstallPath" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release build failed' }
& (Join-Path $PSScriptRoot 'Test-Configuration.ps1') -SptInstallPath $SptInstallPath
& (Join-Path $PSScriptRoot 'Test-OcclusionContracts.ps1') -SptInstallPath $SptInstallPath
& (Join-Path $PSScriptRoot 'Test-ColorBleedContracts.ps1') -SptInstallPath $SptInstallPath
& (Join-Path $PSScriptRoot 'Test-BloomContracts.ps1') -SptInstallPath $SptInstallPath
& (Join-Path $PSScriptRoot 'Test-DistantShadowContracts.ps1') -SptInstallPath $SptInstallPath
& (Join-Path $PSScriptRoot 'Test-AmbientHighlightContracts.ps1') -SptInstallPath $SptInstallPath
& (Join-Path $PSScriptRoot 'Test-GlassCullingContracts.ps1') -SptInstallPath $SptInstallPath
& (Join-Path $PSScriptRoot 'Test-StochasticSsrContracts.ps1') -SptInstallPath $SptInstallPath
Add-Type -Path (Join-Path $SptInstallPath 'BepInEx\core\Mono.Cecil.dll')
$built = Join-Path $repo 'bin\Release\netstandard2.1\FuckTheseFX.dll'
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($built)
try {
    $version = $assembly.Name.Version.ToString(3)
    $plugin = $assembly.MainModule.Types | Where-Object FullName -eq 'FuckTheseFX.Plugin'
    $attribute = $plugin.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' }
    if ($attribute.ConstructorArguments[0].Value -ne 'com.rootdarkarchon.fuckthesefx' -or $attribute.ConstructorArguments[2].Value -ne $version) { throw 'Plugin identity/version mismatch' }
    if ($assembly.MainModule.AssemblyReferences | Where-Object Name -match 'SPTAOReplacer|FuckingCustomAmbient') { throw 'Unexpected AO plugin dependency' }
    if ($assembly.MainModule.AssemblyReferences | Where-Object Name -match 'Fika') { throw 'Unexpected Fika dependency' }
    foreach ($name in @('AmbientHighlightIntegration','BloomResolutionIntegration','DistantShadowSamplingIntegration','DistantShadowResolutionIntegration','XeGTAOIntegration','AmbientOcclusionBridge','VintageFilterIntegration','ColorBleedPipeline','GlassCullingIntegration','StochasticSsrIntegration')) {
        if (-not ($assembly.MainModule.Types | Where-Object Name -eq $name)) { throw "Missing integration: $name" }
    }
    if ($assembly.MainModule.Types | Where-Object FullName -match 'AmplifyOcclusion|AmplifyGTAO') { throw 'Amplify code leaked into FX plugin' }
    if ($assembly.MainModule.Types | Where-Object Namespace -match '\.SceneCulling') { throw 'Retired scene culling integration remains in package' }
    if ($assembly.MainModule.GetTypeReferences() | Where-Object Name -match '^StaticDeferredDecal') { throw 'Retired static decal hooks remain in package' }
    $glass = $assembly.MainModule.Types | Where-Object Name -eq 'GlassCullingIntegration'
    $glassTypes = @($glass) + @($glass.NestedTypes)
    foreach ($type in $glassTypes) { foreach ($method in $type.Methods) {
        if ($method.HasBody -and ($method.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -match 'Glass culling matched:|Glass culling skipped:|Candidates=' })) {
            throw 'Verbose glass culling logging leaked into Release'
        }
    } }
    Write-Output 'Verified plugin GUID, version, all FX hooks and independence from AO Replacer.'
} finally { $assembly.Dispose() }
$expected = @{
    'BepInEx/plugins/FuckTheseFX/FuckTheseFX.dll' = $built
    'BepInEx/plugins/FuckTheseFX/Licenses/XeGTAO.txt' = (Join-Path $repo 'unity\Assets\XeGTAO\Upstream\LICENSE')
    'BepInEx/plugins/FuckTheseFX/FoliageLightingTest.md' = (Join-Path $repo 'docs\FoliageLightingTest.md')
}
foreach ($name in @('LICENSE.txt','SSSR-LICENSE.txt','NOTICE.md','UPSTREAM.json')) {
    $expected["BepInEx/plugins/FuckTheseFX/Licenses/StochasticSSR/$name"] = Join-Path $repo "unity/StochasticSSR/AMD/$name"
}
foreach ($asset in $assets) { $expected["BepInEx/plugins/FuckTheseFX/Assets/$asset"] = Join-Path $repo "Assets\$asset" }
$zip = Join-Path $repo "bin\Release\FuckTheseFX-$version-SPT4.1.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
# FileMode.Create replaces this exact generated artifact without touching other files.
$file = [IO.File]::Open($zip, [IO.FileMode]::Create)
$archive = [IO.Compression.ZipArchive]::new($file, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in ($expected.Keys | Sort-Object)) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $expected[$name], $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose(); $file.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    if ($archive.Entries.Count -ne $expected.Count) { throw 'Unexpected ZIP contents.' }
    foreach ($entry in $archive.Entries) {
        if (-not $expected.ContainsKey($entry.FullName)) { throw "Unexpected ZIP entry: $($entry.FullName)" }
        $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath $expected[$entry.FullName]).Hash) { throw "ZIP hash mismatch: $($entry.FullName)" }
    }
    Write-Output "Verified all $($archive.Entries.Count) package entries and hashes."
} finally { $archive.Dispose() }
Get-FileHash -LiteralPath $zip -Algorithm SHA256


