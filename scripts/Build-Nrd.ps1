param()
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$vendor=Join-Path $repo 'native/NRD/vendor/NRD'
$upstream=Join-Path $repo 'obj/NRD/upstream'
$bridge=Join-Path $repo 'obj/NRD/bridge'
if(-not (Test-Path (Join-Path $vendor 'Include/NRD.h'))){throw 'Fetch the NRD revision recorded in native/NRD/UPSTREAM.json first.'}
$revision=(& git -C $vendor rev-parse HEAD).Trim()
$pin=(Get-Content (Join-Path $repo 'native/NRD/UPSTREAM.json') -Raw | ConvertFrom-Json).revision
if($revision -ne $pin){throw "Unpinned NRD revision: $revision"}
& cmake -S $vendor -B $upstream -DNRD_STATIC_LIBRARY=ON -DNRD_NRI=OFF -DNRD_EMBEDS_DXBC_SHADERS=ON -DNRD_EMBEDS_DXIL_SHADERS=OFF -DNRD_EMBEDS_SPIRV_SHADERS=OFF -DNRD_NORMAL_ENCODING=2 -DNRD_ROUGHNESS_ENCODING=1 -DNRD_SUPPORTS_CHECKERBOARD=ON -DNRD_SUPPORTS_QUAD_INTRINSICS=OFF
if($LASTEXITCODE -ne 0){throw 'NRD configuration failed'}
& cmake --build $upstream --config Release --parallel 12
if($LASTEXITCODE -ne 0){throw 'NRD DXBC build failed'}
& cmake -S (Join-Path $repo 'native/NRD') -B $bridge -DBUILD_TESTING=ON
if($LASTEXITCODE -ne 0){throw 'NRD bridge configuration failed'}
& cmake --build $bridge --config Release --parallel 8
if($LASTEXITCODE -ne 0){throw 'NRD bridge build failed'}
& ctest --test-dir $bridge -C Release --output-on-failure --no-tests=error
if($LASTEXITCODE -ne 0){throw 'NRD bridge regression failed'}
Get-FileHash (Join-Path $bridge 'Release/FTFXNrd.dll')
