cmake_minimum_required(VERSION 3.30)
project(FTFXNrd LANGUAGES CXX)
set(NRD_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/vendor/NRD")
set(UPSTREAM_BUILD "${CMAKE_CURRENT_SOURCE_DIR}/../../obj/NRD/upstream")
find_program(FXC fxc PATHS "C:/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64" REQUIRED)
foreach(entry Pack Unpack)
    add_custom_command(OUTPUT "${CMAKE_CURRENT_BINARY_DIR}/${entry}.h"
        COMMAND "${FXC}" /nologo /T cs_5_0 /E ${entry} /I "${NRD_ROOT}/Shaders"
            /Fh "${CMAKE_CURRENT_BINARY_DIR}/${entry}.h" /Vn "FTFX_${entry}" "${CMAKE_CURRENT_SOURCE_DIR}/Adapter.hlsl"
        DEPENDS Adapter.hlsl "${NRD_ROOT}/Shaders/NRD.hlsli" "${NRD_ROOT}/Shaders/NRDConfig.hlsli")
endforeach()
add_library(FTFXNrd SHARED Bridge.cpp "${CMAKE_CURRENT_BINARY_DIR}/Pack.h" "${CMAKE_CURRENT_BINARY_DIR}/Unpack.h")
target_include_directories(FTFXNrd PRIVATE "${NRD_ROOT}/Include" "${CMAKE_CURRENT_BINARY_DIR}")
target_compile_features(FTFXNrd PRIVATE cxx_std_17)
target_compile_definitions(FTFXNrd PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(FTFXNrd PRIVATE /W4 /WX /EHsc)
target_link_libraries(FTFXNrd PRIVATE "${NRD_ROOT}/_Bin/Release/NRD.lib" "${UPSTREAM_BUILD}/_deps/shadermake-build/Release/ShaderMakeBlob.lib" d3d11)
