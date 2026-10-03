param([string]$SptInstallPath = 'D:\Tarkov-SPT-4.1')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
& dotnet build (Join-Path $repo 'unity\NativeSsrProfiling\NativeSsrProfiling.csproj') -c Release "-p:SptInstallPath=$SptInstallPath" --nologo
if ($LASTEXITCODE -ne 0) { throw 'SSR profiling build failed' }
$built = Join-Path $repo 'obj\NativeSsrProfiling\plugin\netstandard2.1\FTFX.NativeSsrProfiling.dll'
$expected = @{
    'BepInEx/plugins/FTFX.NativeSsrProfiling/FTFX.NativeSsrProfiling.dll' = $built
}
New-Item -ItemType Directory -Force (Join-Path $repo 'bin\Research') | Out-Null
$zip = Join-Path $repo 'bin\Research\FTFX-NativeSsrProfiling-0.1.0-SPT4.1.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$file = [IO.File]::Open($zip,[IO.FileMode]::Create)
$archive = [IO.Compression.ZipArchive]::new($file,[IO.Compression.ZipArchiveMode]::Create)
try { foreach ($name in ($expected.Keys | Sort-Object)) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$expected[$name],$name) | Out-Null } }
finally { $archive.Dispose(); $file.Dispose() }
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    if ($archive.Entries.Count -ne $expected.Count) { throw 'Unexpected profiling package contents' }
    foreach ($entry in $archive.Entries) {
        if (-not $expected.ContainsKey($entry.FullName)) { throw 'Unexpected profiling package entry' }
        $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($hash -ne (Get-FileHash $expected[$entry.FullName]).Hash) { throw 'Profiling ZIP hash mismatch' }
    }
} finally { $archive.Dispose() }
Get-FileHash $zip
