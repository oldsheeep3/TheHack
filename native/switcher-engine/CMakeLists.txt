cmake_minimum_required(VERSION 3.24)
project(switcher-engine LANGUAGES CXX)

# switcher-engine: native libobs video engine behind the Switcher.Engine P/Invoke layer.
# Links against an installed OBS Studio's libobs (headers + import lib). See README.md for the
# pinned OBS version and how to point CMake at it (LIBOBS_INCLUDE_DIR / LIBOBS_LIB).

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(SWITCHER_ENGINE_USE_FIND_PACKAGE "Resolve libobs via find_package(libobs)" ON)

add_library(switcher-engine SHARED
    src/engine.cpp
)

target_include_directories(switcher-engine PUBLIC include)

# --- libobs resolution -------------------------------------------------------
# Preferred: a libobs CMake package (shipped with OBS dev files / obs-studio build tree).
if(SWITCHER_ENGINE_USE_FIND_PACKAGE)
    find_package(libobs QUIET)
endif()

if(TARGET OBS::libobs)
    target_link_libraries(switcher-engine PRIVATE OBS::libobs)
else()
    # Fallback: explicit paths. Pass -DLIBOBS_INCLUDE_DIR=... -DLIBOBS_LIB=... (see README.md).
    if(NOT DEFINED LIBOBS_INCLUDE_DIR OR NOT DEFINED LIBOBS_LIB)
        message(FATAL_ERROR
            "libobs not found. Either install OBS dev files (find_package(libobs)) "
            "or pass -DLIBOBS_INCLUDE_DIR=<path/to/obs headers> -DLIBOBS_LIB=<path/to/obs.lib>.")
    endif()
    target_include_directories(switcher-engine PRIVATE ${LIBOBS_INCLUDE_DIR})
    target_link_libraries(switcher-engine PRIVATE ${LIBOBS_LIB})
endif()

# Output name must match the DllImport("switcher-engine") in NativeMethods.cs.
set_target_properties(switcher-engine PROPERTIES
    OUTPUT_NAME "switcher-engine"
    PREFIX ""
)
