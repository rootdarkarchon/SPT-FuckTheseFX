param([string]$Game='D:\Tarkov-SPT-4.1',[string]$ResearchRoot='A:\dev\source\SPTAOReplacer')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$output=Join-Path $repo 'obj/SsrContinuationAudit/research-build'
& dotnet build (Join-Path $ResearchRoot 'unity/CustomAmbientResearch/CustomAmbientResearch.csproj') -c Release -o $output --nologo
if($LASTEXITCODE -ne 0){throw 'Research companion build failed'}
Add-Type -Path (Join-Path $Game 'BepInEx/core/Mono.Cecil.dll')
$dll=Join-Path $output 'CustomAmbientResearch.dll'
$assembly=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try {
    $attribute=$assembly.MainModule.Types.CustomAttributes | Where-Object {$_.AttributeType.FullName -eq 'BepInEx.BepInPlugin'}
    if($assembly.Name.Version.ToString(3) -ne '0.14.3' -or $attribute.ConstructorArguments[0].Value -ne 'com.matsix.customambient.research' -or $attribute.ConstructorArguments[2].Value -ne '0.14.3'){throw 'Research companion identity/version mismatch'}
} finally {$assembly.Dispose()}
$expected=@{'BepInEx/plugins/CustomAmbientResearch/CustomAmbientResearch.dll'=$dll}
foreach($name in @('CustomAmbientGpu.dll','ambientresearch','glass-pass-programs.bin','glass-pass-programs.json')) {
    $expected['BepInEx/plugins/CustomAmbientResearch/'+$name]=Join-Path $Game ('BepInEx/plugins/CustomAmbientResearch/'+$name)
}
if((Get-FileHash $expected['BepInEx/plugins/CustomAmbientResearch/CustomAmbientGpu.dll']).Hash -ne '0BB86751DDD583772EB7CD6519D9C5D82012254623D7BC6AD8D6588E18ED968C'){throw 'Native timing helper differs from tested ABI'}
$zip=Join-Path $repo 'bin/Release/CustomAmbientResearch-0.14.3-ssr-mip-completion-test.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$file=[IO.File]::Open($zip,[IO.FileMode]::Create);$archive=[IO.Compression.ZipArchive]::new($file,[IO.Compression.ZipArchiveMode]::Create)
try {foreach($name in ($expected.Keys | Sort-Object)){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$expected[$name],$name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null}}
finally {$archive.Dispose();$file.Dispose()}
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try {
    if($archive.Entries.Count -ne $expected.Count){throw 'Unexpected companion ZIP contents'}
    foreach($entry in $archive.Entries){
        if(-not $expected.ContainsKey($entry.FullName)){throw ('Unexpected ZIP entry '+$entry.FullName)}
        $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try {$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}
        finally {$stream.Dispose();$sha.Dispose()}
        if($hash -ne (Get-FileHash $expected[$entry.FullName]).Hash){throw ('ZIP hash mismatch '+$entry.FullName)}
    }
    Write-Output "Verified all $($expected.Count) companion entries and hashes; native helper unchanged."
} finally {$archive.Dispose()}
Get-FileHash $zip
