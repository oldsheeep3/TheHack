<#
    Assembles the OBS runtime that ships alongside Switcher, so the app needs no OBS installation.

    Switcher's video engine is libobs. Rather than requiring every machine to install OBS Studio, a
    release carries the pieces of it that the engine actually loads. `ObsRuntime` prefers this bundled
    copy over anything installed on the machine, so the shipped build always runs against the version it
    was tested with.

    Only what the engine loads is copied:
      bin\64bit\                     obs.dll, the graphics module and their dependencies
      data\libobs\                   core effects — without these obs_reset_video() fails
      obs-plugins\64bit\<module>.dll the allow-listed modules only
      data\obs-plugins\<module>\     per-plugin data for those modules

    Plugins that construct Qt widgets (frontend-tools, obs-websocket, decklink-output-ui, …) are
    deliberately NOT copied: the engine never loads them, and in a host with no QApplication loading one
    aborts the process.

    LICENSING: libobs and the OBS plugins are GPL-2.0. Shipping them alongside Switcher means the
    distributed whole is governed by the GPL — see LICENSE and THIRD-PARTY-NOTICES.md. This script does
    not copy the NDI runtime: its licence does not permit redistribution, so NDI stays an optional
    component the user installs (the engine loads it dynamically if present).

    Usage:
      pwsh tools/bundle-obs-runtime.ps1 -Destination src\Switcher.App\bin\Release\net9.0-windows\obs-runtime
#>
param(
    [string]$Source = "$env:ProgramFiles\obs-studio",
    [Parameter(Mandatory = $true)][string]$Destination
)

$ErrorActionPreference = "Stop"

# Must match kSourceModules in native/switcher-engine/src/engine.cpp.
$modules = @(
    "win-dshow", "win-capture", "win-wasapi", "obs-ffmpeg", "image-source",
    "obs-text", "text-freetype2", "obs-transitions", "obs-filters", "vlc-video",
    "obs-x264", "obs-outputs", "rtmp-services", "obs-browser"
)

if (-not (Test-Path "$Source\bin\64bit\obs.dll")) {
    throw "no obs.dll under '$Source'. Point -Source at an OBS Studio installation."
}

$version = (Get-Item "$Source\bin\64bit\obs.dll").VersionInfo.FileVersion
Write-Output "Bundling libobs $version from $Source"

New-Item -ItemType Directory -Force -Path $Destination | Out-Null

# --- bin: copied wholesale, because obs.dll's dependency set changes between releases and picking it
#     apart by hand is how you ship a build that fails on someone else's machine.
Write-Output "  bin\64bit ..."
New-Item -ItemType Directory -Force -Path "$Destination\bin\64bit" | Out-Null
Copy-Item "$Source\bin\64bit\*" -Destination "$Destination\bin\64bit" -Recurse -Force

# --- core data (required before obs_reset_video)
Write-Output "  data\libobs ..."
New-Item -ItemType Directory -Force -Path "$Destination\data\libobs" | Out-Null
Copy-Item "$Source\data\libobs\*" -Destination "$Destination\data\libobs" -Recurse -Force

# --- allow-listed plugins and their data
$copied = 0
foreach ($module in $modules) {
    $dll = "$Source\obs-plugins\64bit\$module.dll"
    if (-not (Test-Path $dll)) {
        Write-Output "  - $module (absent, skipped)"
        continue
    }

    New-Item -ItemType Directory -Force -Path "$Destination\obs-plugins\64bit" | Out-Null
    Copy-Item $dll -Destination "$Destination\obs-plugins\64bit" -Force

    $data = "$Source\data\obs-plugins\$module"
    if (Test-Path $data) {
        New-Item -ItemType Directory -Force -Path "$Destination\data\obs-plugins\$module" | Out-Null
        Copy-Item "$data\*" -Destination "$Destination\data\obs-plugins\$module" -Recurse -Force
    }

    $copied++
    Write-Output "  + $module"
}

# obs-browser drags in CEF (the library, the subprocess exe and its resources), which sit in the plugin
# bin folder next to the module rather than in bin\64bit.
$cef = @("libcef.dll", "chrome_elf.dll", "libEGL.dll", "libGLESv2.dll", "obs-browser-page.exe",
         "v8_context_snapshot.bin", "icudtl.dat", "snapshot_blob.bin", "vk_swiftshader.dll",
         "vulkan-1.dll", "vk_swiftshader_icd.json")
foreach ($file in $cef) {
    if (Test-Path "$Source\obs-plugins\64bit\$file") {
        Copy-Item "$Source\obs-plugins\64bit\$file" -Destination "$Destination\obs-plugins\64bit" -Force
    }
}
foreach ($dir in @("locales", "swiftshader")) {
    if (Test-Path "$Source\obs-plugins\64bit\$dir") {
        Copy-Item "$Source\obs-plugins\64bit\$dir" -Destination "$Destination\obs-plugins\64bit\$dir" -Recurse -Force
    }
}

# Record what was bundled; the app logs its own detected version, making the two comparable in support.
@{
    libobs_version = $version
    bundled_at_utc = (Get-Date).ToUniversalTime().ToString("o")
    source         = $Source
    modules        = $modules
} | ConvertTo-Json -Depth 3 | Set-Content "$Destination\bundle-info.json" -Encoding utf8

$size = [math]::Round((Get-ChildItem $Destination -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
Write-Output ""
Write-Output "Bundled $copied plugin(s), libobs $version -> $Destination ($size MB)"
Write-Output "Reminder: this makes the distributed build GPL-2.0. See LICENSE / THIRD-PARTY-NOTICES.md."
