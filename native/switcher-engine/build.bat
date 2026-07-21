@echo off
setlocal EnableDelayedExpansion
rem ============================================================================
rem  switcher-engine build helper (Windows / MSVC / CMake)
rem
rem  Builds native\switcher-engine\switcher-engine.dll and copies it next to
rem  Switcher.App.exe so DllImport("switcher-engine") resolves at runtime.
rem
rem  PREREQS (see README.md):
rem    - Visual Studio 2022 with "Desktop development with C++" (MSVC, x64)
rem    - CMake >= 3.24 (bundled with VS, or standalone on PATH)
rem    - libobs DEV FILES matching your installed OBS version (31.0.x):
rem        headers (incl. generated obs-config.h) + obs.lib + libobs CMake pkg.
rem        An installed OBS *app* does NOT include these - build/install OBS
rem        from source, or use its exported libobs package.
rem
rem  USAGE (pick ONE way to point at libobs, then run this .bat):
rem
rem    A) find_package(libobs)  -- preferred, if you have an OBS build/install tree
rem         set OBS_PREFIX=C:\obs-studio\build_x64\install
rem         build.bat
rem
rem    B) explicit header + import-lib paths
rem         set LIBOBS_INCLUDE_DIR=C:\obs-studio\libobs
rem         set LIBOBS_LIB=C:\obs-studio\build_x64\libobs\Release\obs.lib
rem         build.bat
rem
rem  OPTIONAL:
rem    set CONFIG=Debug          (default Release)
rem    set OBS_BIN=C:\Program Files\obs-studio\bin\64bit
rem                              ^ if set, obs.dll is copied next to the app too
rem                                (otherwise put OBS_BIN on your PATH at runtime)
rem ============================================================================

if "%CONFIG%"=="" set CONFIG=Release

set SCRIPT_DIR=%~dp0
set BUILD_DIR=%SCRIPT_DIR%build
rem App output: <repo>\src\Switcher.App\bin\<CONFIG>\net9.0-windows
set APP_OUT=%SCRIPT_DIR%..\..\src\Switcher.App\bin\%CONFIG%\net9.0-windows

echo(
echo === switcher-engine build (%CONFIG%, x64) ===

rem --- decide how to resolve libobs ------------------------------------------
set CMAKE_LIBOBS_ARGS=
if not "%LIBOBS_LIB%"=="" (
    echo [libobs] explicit paths
    echo   include: %LIBOBS_INCLUDE_DIR%
    echo   lib:     %LIBOBS_LIB%
    if "%LIBOBS_INCLUDE_DIR%"=="" (
        echo ERROR: set LIBOBS_INCLUDE_DIR alongside LIBOBS_LIB.
        exit /b 1
    )
    set CMAKE_LIBOBS_ARGS=-DSWITCHER_ENGINE_USE_FIND_PACKAGE=OFF -DLIBOBS_INCLUDE_DIR="%LIBOBS_INCLUDE_DIR%" -DLIBOBS_LIB="%LIBOBS_LIB%"
) else if not "%OBS_PREFIX%"=="" (
    echo [libobs] find_package via CMAKE_PREFIX_PATH=%OBS_PREFIX%
    set CMAKE_LIBOBS_ARGS=-DCMAKE_PREFIX_PATH="%OBS_PREFIX%"
) else (
    echo ERROR: point CMake at libobs first. Set either:
    echo    OBS_PREFIX=^<OBS build/install tree with the libobs CMake package^>
    echo  or
    echo    LIBOBS_INCLUDE_DIR=^<obs headers^>  and  LIBOBS_LIB=^<path\to\obs.lib^>
    echo  See the header of this file and README.md.
    exit /b 1
)

rem --- configure -------------------------------------------------------------
echo(
echo --- cmake configure ---
cmake -S "%SCRIPT_DIR%." -B "%BUILD_DIR%" -A x64 %CMAKE_LIBOBS_ARGS%
if errorlevel 1 (
    echo ERROR: cmake configure failed.
    exit /b 1
)

rem --- build -----------------------------------------------------------------
echo(
echo --- cmake build (%CONFIG%) ---
cmake --build "%BUILD_DIR%" --config %CONFIG%
if errorlevel 1 (
    echo ERROR: build failed.
    exit /b 1
)

rem --- locate the DLL (single-config vs multi-config generators) --------------
set DLL=%BUILD_DIR%\%CONFIG%\switcher-engine.dll
if not exist "%DLL%" set DLL=%BUILD_DIR%\switcher-engine.dll
if not exist "%DLL%" (
    echo ERROR: switcher-engine.dll not found under %BUILD_DIR%.
    exit /b 1
)

rem --- deploy next to the app ------------------------------------------------
if not exist "%APP_OUT%" (
    echo NOTE: app output not found yet: %APP_OUT%
    echo       build Switcher.App ^(dotnet build -c %CONFIG%^) then re-run, or copy manually.
) else (
    echo(
    echo --- copying to app output ---
    copy /Y "%DLL%" "%APP_OUT%\" >nul && echo   switcher-engine.dll -^> %APP_OUT%
    if not "%OBS_BIN%"=="" (
        if exist "%OBS_BIN%\obs.dll" (
            copy /Y "%OBS_BIN%\obs.dll" "%APP_OUT%\" >nul && echo   obs.dll -^> %APP_OUT%
            echo   NOTE: obs.dll alone may be insufficient - libobs also needs its
            echo         data\ folder, plugin modules and libobs-d3d11.dll. Prefer
            echo         running with OBS_BIN on PATH, or deploy the full runtime.
        )
    )
)

echo(
echo === done: %DLL% ===
endlocal
