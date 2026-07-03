cmake_minimum_required(VERSION 3.20)
project(MonoXRLayer LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Layer output name follows the OpenXR convention XR_APILAYER_<VENDOR>_<name>.
set(LAYER_NAME "XR_APILAYER_NOVELTY_monoxr")

add_library(${LAYER_NAME} SHARED
    src/layer.cpp
)

target_include_directories(${LAYER_NAME} PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/third_party   # third_party/openxr/*.h
    ${CMAKE_CURRENT_SOURCE_DIR}/../shared      # monoxr_abi.h
)

target_compile_definitions(${LAYER_NAME} PRIVATE
    XR_USE_GRAPHICS_API_D3D11
    XR_USE_PLATFORM_WIN32
    NOMINMAX
    WIN32_LEAN_AND_MEAN
    UNICODE
    _UNICODE
)

target_link_libraries(${LAYER_NAME} PRIVATE d3d11 dxgi)

# Emit the layer manifest next to the DLL with an absolute library_path so the
# OpenXR loader can find the DLL when this manifest is registered.
set(_dll_path "$<TARGET_FILE:${LAYER_NAME}>")
add_custom_command(TARGET ${LAYER_NAME} POST_BUILD
    COMMAND ${CMAKE_COMMAND}
        -DLAYER_DLL=${_dll_path}
        -DTEMPLATE=${CMAKE_CURRENT_SOURCE_DIR}/manifest/MonoXR.json.in
        -DOUT=$<TARGET_FILE_DIR:${LAYER_NAME}>/MonoXR.json
        -P ${CMAKE_CURRENT_SOURCE_DIR}/cmake/GenManifest.cmake
    VERBATIM
)
