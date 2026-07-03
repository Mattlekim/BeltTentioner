# Fill MonoXR.json.in with the absolute path to the built DLL.
# JSON needs backslashes escaped, so turn C:\a\b into C:\\a\\b.
file(TO_NATIVE_PATH "${LAYER_DLL}" _native)
string(REPLACE "\\" "\\\\" LAYER_DLL_JSON "${_native}")
configure_file("${TEMPLATE}" "${OUT}" @ONLY)
message(STATUS "MonoXR: wrote layer manifest ${OUT} -> ${_native}")
