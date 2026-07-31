<#
.SYNOPSIS
    Windows 側のセットアップ（setup.sh の対）。switcher-engine.dll までを自動で用意する。

.DESCRIPTION
    Visual Studio で F5 したときの DllNotFoundException / 0x8007007E は、ネイティブの
    switcher-engine.dll が未ビルドなことが原因。この DLL は .sln 外の CMake ビルドで、
    さらにその手前に libobs の import lib (obs.lib) が要る。obs.lib は OBS の
    インストーラには含まれず、OBS 本体をソースからビルドしないと手に入らない。

    このスクリプトはその依存の連鎖を通しで面倒を見る。

        インストール済み OBS を検出 → 同じ版の obs-studio を clone
          → libobs だけビルド → obs.lib
            → .env に LIBOBS_* を記録
              → build.bat → switcher-engine.dll
                → Switcher.App.csproj が App 出力へ自動コピー（既存の仕組み）

    バージョンは固定しない。マネージド側 (ObsRuntime.cs) が libobs 30.0〜32.99 を
    受け入れる実装なので、こちらもインストール済みの版に合わせる。obs.lib（ビルド時）と
    obs.dll（実行時）の版が食い違うと起動失敗するため、"検出した版に合わせる" のが
    もっとも事故が少ない。OBS が入っていない場合だけ既定版にフォールバックする。

.PARAMETER ObsVersion
    使う OBS の版を明示する（例 32.0.4）。省略時はインストール済み OBS から検出し、
    それも無ければ $DefaultObsVersion。

.PARAMETER ObsInstallPath
    インストール済み OBS のルート。自動検出できないときだけ指定する。

.PARAMETER ObsSourceDir
    obs-studio のソースツリー。既定はリポジトリ直下の obs-studio\（.gitignore 済み）。

.PARAMETER Check
    検証のみ。clone もビルドも .env の書き換えもしない。

.PARAMETER CI
    非対話。libobs のソースビルド（数十分規模）は行わず、検証と switcher-engine の
    ビルドだけに絞る。.env も書き換えない。

.PARAMETER SkipLibobs
    obs.lib のビルドを飛ばす（.env に LIBOBS_* が既にある場合）。

.PARAMETER SkipEngine
    build.bat（switcher-engine.dll）を飛ばす。

.PARAMETER Bundle
    tools/bundle-obs-runtime.ps1 を実行し、App 出力に obs-runtime\ を同梱する。

.PARAMETER WriteLaunchSettings
    Properties\launchSettings.json を生成する。通常は不要（ObsRuntime.AddDllSearchDirectory
    が実行時に bin\64bit を DLL 検索パスへ追加するため）。VS の起動プロファイルを
    明示したいときだけ。

.PARAMETER Force
    obs-studio の作業ツリーを作り直す。

.EXAMPLE
    .\setup.ps1
    インストール済み OBS を検出して、その版で一式そろえる。

.EXAMPLE
    .\setup.ps1 -Check
    何も変更せず、足りないものだけ報告する。

.EXAMPLE
    .\setup.ps1 -ObsVersion 31.0.3
    版を明示する。

.NOTES
    終了コード: 0 = 準備完了 / 1 = 未解決の問題あり。何度実行しても同じ結果になる。
#>
[CmdletBinding()]
param(
    [string]$ObsVersion,
    [string]$ObsInstallPath,
    [string]$ObsSourceDir,
    [switch]$Check,
    [switch]$CI,
    [switch]$SkipLibobs,
    [switch]$SkipEngine,
    [switch]$Bundle,
    [switch]$WriteLaunchSettings,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot
$DefaultObsVersion = '32.0.4'   # ObsRuntime.cs の検証済み版。OBS 未インストール時のみ使う。
$ObsRepo = 'https://github.com/obsproject/obs-studio.git'

if (-not $ObsSourceDir) { $ObsSourceDir = Join-Path $RepoRoot 'obs-studio' }
if ($CI) { $SkipLibobs = $true }

$script:Failures = @()
$script:Warnings = @()

function Write-Head { param([string]$Text) Write-Host ""; Write-Host "== $Text ==" }
function Write-Ok   { param([string]$Text) Write-Host "  ok   $Text" -ForegroundColor Green }
function Write-Note { param([string]$Text) Write-Host "  $Text" -ForegroundColor DarkGray }
function Write-Warn { param([string]$Text) Write-Host "  warn $Text" -ForegroundColor Yellow; $script:Warnings += $Text }
function Write-Fail { param([string]$Text) Write-Host "  NG   $Text" -ForegroundColor Red;    $script:Failures += $Text }

# PowerShell 5.1 では、ネイティブコマンドの stderr をリダイレクトすると各行が
# ErrorRecord (NativeCommandError) に包まれる。$ErrorActionPreference='Stop' の下では
# それが終了エラーになり、終了コード 0 でもその場でスクリプトごと落ちる。
# cmake も git も進捗や警告を平然と stderr に出すので、stderr を捨てる呼び出しは
# すべてこれ経由にして、その間だけ Continue に落とす。$LASTEXITCODE は素通りする。
function Invoke-Native {
    param([Parameter(Mandatory)][scriptblock]$Command)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command } finally { $ErrorActionPreference = $prev }
}

# ---------------------------------------------------------------------------
# 0. 前提チェック
# ---------------------------------------------------------------------------
Write-Head "前提"

# $IsWindows は PowerShell 7 以降にしか無く、StrictMode 下の 5.1 では参照だけで落ちる。
# $env:OS は両方で使えるので、そちらで判定する。
if ($env:OS -ne 'Windows_NT') {
    Write-Host "setup.ps1 は Windows 専用です。Linux/macOS では ./setup.sh を使ってください。" -ForegroundColor Red
    exit 1
}

# MSVC。cmake -A x64 は VS を自前で見つけるが、見つからない環境を先に弾いておく。
# cmake の在り処を VS から引くので、ツールの確認より先に済ませる。
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$VsPath = $null
if (Test-Path $vswhere) {
    $VsPath = Invoke-Native { & $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath 2>$null } | Select-Object -First 1
}
if ($VsPath) { Write-Ok "Visual Studio (C++): $VsPath" }
else         { Write-Fail "Visual Studio 2022 の「C++によるデスクトップ開発」が見つからない" }

$git = Get-Command git -ErrorAction SilentlyContinue
if ($git) { Write-Ok "git ($(& git --version | Select-Object -First 1))" }
else      { Write-Fail "git が PATH に無い" }

# cmake は単体で入れていなくても VS に同梱されている（「C++によるデスクトップ開発」に含まれる）。
# build.bat は子プロセスから素の `cmake` を呼ぶので、PATH そのものに足しておく。
# こうしておけば build.bat 側は手を入れずに済み、環境変数は子プロセスへ継承される。
$cmake = Get-Command cmake -ErrorAction SilentlyContinue
if (-not $cmake -and $VsPath) {
    $bundledCMake = Join-Path $VsPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin'
    if (Test-Path (Join-Path $bundledCMake 'cmake.exe')) {
        $env:PATH = "$bundledCMake;$env:PATH"
        $cmake = Get-Command cmake -ErrorAction SilentlyContinue
        Write-Note "VS 同梱の cmake を PATH に追加した: $bundledCMake"
    }
}
if ($cmake) { Write-Ok "cmake ($(& cmake --version | Select-Object -First 1))" }
else        { Write-Fail "cmake が PATH に無く、VS 同梱のものも見つからない" }

# ---------------------------------------------------------------------------
# 1. OBS の検出とバージョン決定
#    ObsRuntime.Locate() と同じ順序・同じ判定条件で探す。
# ---------------------------------------------------------------------------
Write-Head "OBS"

function Test-ObsRoot {
    param([string]$Root)
    if (-not $Root) { return $false }
    (Test-Path (Join-Path $Root 'bin\64bit\obs.dll')) -and (Test-Path (Join-Path $Root 'data\libobs'))
}

function Find-ObsInstall {
    if ($ObsInstallPath) {
        if (Test-ObsRoot $ObsInstallPath) { return (Resolve-Path $ObsInstallPath).Path }
        Write-Warn "-ObsInstallPath '$ObsInstallPath' は OBS のルートに見えない（bin\64bit\obs.dll / data\libobs が無い）"
    }
    # インストーラが書く Uninstall キー。32bit ビューも見る。
    foreach ($hive in @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OBS Studio',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\OBS Studio')) {
        try {
            $loc = (Get-ItemProperty -Path $hive -Name InstallLocation -ErrorAction Stop).InstallLocation
            if (Test-ObsRoot $loc) { return $loc }
        } catch { }
    }
    foreach ($pf in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramW6432)) {
        if ($pf) {
            $candidate = Join-Path $pf 'obs-studio'
            if (Test-ObsRoot $candidate) { return $candidate }
        }
    }
    return $null
}

$ObsInstall = Find-ObsInstall
$DetectedVersion = $null
if ($ObsInstall) {
    # obs.dll のファイルバージョン。C# 側 (LogRuntimeVersion) と同じ読み方。
    $fileVersion = (Get-Item (Join-Path $ObsInstall 'bin\64bit\obs.dll')).VersionInfo.FileVersion
    # "32.0.4.0" → "32.0.4"。末尾の 0 を落として OBS の tag 表記に合わせる。
    if ($fileVersion -match '^(\d+)\.(\d+)\.(\d+)') { $DetectedVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3])" }
    Write-Ok "インストール済み OBS: $ObsInstall (obs.dll $fileVersion)"
} else {
    Write-Warn "インストール済み OBS が見つからない（実行時に obs.dll が必要。-Bundle で同梱するにも一度は必要）"
}

# 明示指定 > 検出 > 既定。
$TargetVersion = if ($ObsVersion) { $ObsVersion } elseif ($DetectedVersion) { $DetectedVersion } else { $DefaultObsVersion }
if ($ObsVersion -and $DetectedVersion -and ($ObsVersion -ne $DetectedVersion)) {
    # ここが食い違うと「起動はするが落ちる」になるので、素通りさせず警告する。
    Write-Warn "-ObsVersion $ObsVersion がインストール済み $DetectedVersion と違う。obs.lib と obs.dll の版が食い違うと起動失敗/クラッシュする"
} elseif (-not $ObsVersion -and -not $DetectedVersion) {
    Write-Note "OBS が見つからないので既定の $DefaultObsVersion を使う"
}

# マネージド側が受け入れる範囲 (ObsRuntime.cs: MinimumTested/MaximumTested) と突き合わせる。
if ($TargetVersion -match '^(\d+)\.(\d+)') {
    $major = [int]$Matches[1]
    if ($major -lt 30 -or $major -gt 32) {
        Write-Warn "libobs $TargetVersion は ObsRuntime.cs の検証済み範囲 (30.0-32.99) の外。動く見込みはあるが未検証"
    }
}
Write-Ok "対象 libobs バージョン: $TargetVersion"

# ---------------------------------------------------------------------------
# 2. libobs（obs.lib）
# ---------------------------------------------------------------------------
Write-Head "libobs (obs.lib)"

$LibObsLib = $null
$LibObsInclude = $null

function Resolve-ObsTag {
    param([string]$Version)
    # OBS の tag は素の "32.0.4"。無ければ同じ major.minor で最新のものに寄せる。
    $tags = (Invoke-Native { & git ls-remote --tags --refs $ObsRepo 2>$null }) |
        ForEach-Object { ($_ -split '/')[-1] } |
        Where-Object { $_ -match '^\d+\.\d+\.\d+$' }
    if (-not $tags) { return $Version }   # ネットワーク不通。そのまま試す。
    if ($tags -contains $Version) { return $Version }
    if ($Version -match '^(\d+)\.(\d+)') {
        # $Matches はパイプラインの中で上書きされうるので、先に取り出しておく。
        $prefix = "$($Matches[1]).$($Matches[2])."
        $same = $tags | Where-Object { $_.StartsWith($prefix) } |
            Sort-Object { [version]$_ } | Select-Object -Last 1
        if ($same) {
            Write-Warn "tag $Version が無いので $same を使う"
            return $same
        }
    }
    return $null
}

if ($SkipLibobs) {
    Write-Note "スキップ"
    # .env に既に記録があればそれを使う。
    $envFile = Join-Path $RepoRoot '.env'
    if (Test-Path $envFile) {
        foreach ($line in Get-Content $envFile) {
            if ($line -match '^\s*LIBOBS_LIB\s*=\s*"?([^"]*)"?\s*$')          { $LibObsLib = $Matches[1] }
            if ($line -match '^\s*LIBOBS_INCLUDE_DIR\s*=\s*"?([^"]*)"?\s*$')  { $LibObsInclude = $Matches[1] }
        }
        if ($LibObsLib) { Write-Note ".env の LIBOBS_LIB を使う: $LibObsLib" }
    }
} elseif ($Check) {
    if (Test-Path (Join-Path $ObsSourceDir 'libobs')) { Write-Ok "obs-studio ソースあり: $ObsSourceDir" }
    else { Write-Warn "obs-studio ソースが無い（clone + libobs ビルドが必要。初回は数十分かかる）" }
} else {
    $tag = Resolve-ObsTag $TargetVersion
    if (-not $tag) {
        Write-Fail "OBS の tag $TargetVersion が見つからない。-ObsVersion で有効な版を指定する"
    } else {
        if ($Force -and (Test-Path $ObsSourceDir)) {
            Write-Note "-Force: $ObsSourceDir を削除"
            Remove-Item $ObsSourceDir -Recurse -Force
        }

        if (-not (Test-Path (Join-Path $ObsSourceDir '.git'))) {
            Write-Host "  obs-studio $tag を clone 中（数 GB・数分かかる）..."
            # --recursive: libobs のビルドにサブモジュールが要る。--depth 1 で履歴は持たない。
            & git clone --recursive --shallow-submodules --depth 1 --branch $tag $ObsRepo $ObsSourceDir
            if ($LASTEXITCODE -ne 0) { Write-Fail "obs-studio の clone に失敗した" }
        } else {
            # 既存ツリーを目的の tag に合わせ直す（版を切り替えたときのため）。
            Push-Location $ObsSourceDir
            Invoke-Native { & git fetch --depth 1 origin "refs/tags/${tag}:refs/tags/${tag}" 2>$null } | Out-Null
            Invoke-Native { & git checkout --quiet $tag 2>$null }
            if ($LASTEXITCODE -eq 0) {
                & git submodule update --init --recursive --depth 1 | Out-Null
                Write-Ok "obs-studio を $tag に切り替えた"
            } else {
                Write-Warn "既存の $ObsSourceDir を $tag に切り替えられなかった（-Force で作り直せる）"
            }
            Pop-Location
        }

        if ($script:Failures.Count -eq 0) {
            # MSVC 環境を取り込む（cl / MSBuild への PATH）。VS の Native Tools プロンプトと同等にする。
            $devShell = Join-Path $VsPath 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll'
            if (Test-Path $devShell) {
                Import-Module $devShell
                Enter-VsDevShell -VsInstallPath $VsPath -SkipAutomaticLocation `
                    -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
                Write-Note "VS 開発環境を読み込んだ (x64)"
            }

            $buildDir = Join-Path $ObsSourceDir 'build_x64'
            Push-Location $ObsSourceDir
            Write-Host "  cmake configure..."
            # 近年の OBS は preset が依存物の取得までやる。preset の無い古い版は素の generator に落とす。
            Invoke-Native { & cmake --preset windows-x64 }
            if ($LASTEXITCODE -ne 0) {
                Write-Note "preset windows-x64 が使えないので generator 指定にフォールバック"
                & cmake -S . -B $buildDir -G 'Visual Studio 17 2022' -A x64
            }
            if ($LASTEXITCODE -ne 0) {
                Write-Fail "obs-studio の cmake configure に失敗した"
            } else {
                Write-Host "  libobs をビルド中（初回は数十分かかる）..."
                # libobs だけ。OBS 全体（UI/プラグイン）は要らない。
                & cmake --build $buildDir --config Release --target libobs
                if ($LASTEXITCODE -ne 0) { Write-Fail "libobs のビルドに失敗した" }
            }
            Pop-Location

            if ($script:Failures.Count -eq 0) {
                # 出力位置は generator と OBS の版で変わるので探す。決め打ちしない。
                $lib = Get-ChildItem $buildDir -Recurse -Filter 'obs.lib' -ErrorAction SilentlyContinue |
                    Select-Object -First 1
                # obsconfig.h は生成ヘッダ。公開ヘッダ (<src>\libobs) と両方を渡す必要がある。
                $cfg = Get-ChildItem $buildDir -Recurse -Filter 'obsconfig.h' -ErrorAction SilentlyContinue |
                    Select-Object -First 1
                if ($lib -and $cfg) {
                    $LibObsLib = $lib.FullName
                    $LibObsInclude = "$(Join-Path $ObsSourceDir 'libobs');$($cfg.Directory.FullName)"
                    Write-Ok "obs.lib: $LibObsLib"
                } else {
                    Write-Fail "ビルドは通ったが obs.lib / obsconfig.h が見つからない"
                }
            }
        }
    }
}

# ---------------------------------------------------------------------------
# 3. .env への記録
#    setup.sh が bash として source するので、値は必ず二重引用符で囲み、
#    パス区切りは / にする（\ は bash のエスケープになる）。
# ---------------------------------------------------------------------------
Write-Head ".env"

function Set-EnvValue {
    param([string]$Key, [string]$Value)
    $envFile = Join-Path $RepoRoot '.env'
    if (-not (Test-Path $envFile)) {
        $example = Join-Path $RepoRoot '.env.example'
        if (Test-Path $example) { Copy-Item $example $envFile } else { New-Item -ItemType File -Path $envFile | Out-Null }
    }
    $posix = $Value -replace '\\', '/'
    $line = "$Key=`"$posix`""
    # ForEach-Object のスコープ挙動に頼らず、素の foreach で組み立てる。
    $out = [System.Collections.Generic.List[string]]::new()
    $hit = $false
    foreach ($existing in @(Get-Content $envFile)) {
        if ($existing -match "^\s*$([regex]::Escape($Key))\s*=") { $out.Add($line); $hit = $true }
        else { $out.Add($existing) }
    }
    if (-not $hit) { $out.Add($line) }
    Set-Content -Path $envFile -Value $out -Encoding utf8
}

if ($Check) {
    Write-Note "--Check のため書き換えない"
} elseif ($CI) {
    Write-Note "-CI のため書き換えない（値は Actions の env: で渡すこと）"
} elseif ($LibObsLib -and $LibObsInclude) {
    Set-EnvValue 'LIBOBS_LIB' $LibObsLib
    Set-EnvValue 'LIBOBS_INCLUDE_DIR' $LibObsInclude
    Set-EnvValue 'OBS_VERSION' $TargetVersion
    if ($ObsInstall) { Set-EnvValue 'OBS_BIN' (Join-Path $ObsInstall 'bin\64bit') }
    Write-Ok "LIBOBS_* / OBS_VERSION を .env に記録した"
} else {
    Write-Note "記録する値が無い"
}

# ---------------------------------------------------------------------------
# 4. switcher-engine.dll
# ---------------------------------------------------------------------------
Write-Head "switcher-engine.dll"

$engineDll = Join-Path $RepoRoot 'native\switcher-engine\build\Release\switcher-engine.dll'

if ($SkipEngine) {
    Write-Note "スキップ"
} elseif ($Check) {
    if (Test-Path $engineDll) { Write-Ok "ビルド済み: $engineDll" }
    else { Write-Warn "未ビルド。VS の F5 は DllNotFoundException (0x8007007E) で落ちる" }
} elseif (-not $LibObsLib) {
    Write-Fail "obs.lib が無いので build.bat を実行できない"
} else {
    # build.bat は環境変数で libobs を受け取る（-DLIBOBS_* に変換される）。
    $env:LIBOBS_LIB = $LibObsLib
    $env:LIBOBS_INCLUDE_DIR = $LibObsInclude
    if (-not $env:CONFIG) { $env:CONFIG = 'Release' }
    & cmd /c (Join-Path $RepoRoot 'native\switcher-engine\build.bat')
    if ($LASTEXITCODE -ne 0) { Write-Fail "build.bat に失敗した" }
    elseif (Test-Path $engineDll) { Write-Ok "switcher-engine.dll をビルドした" }
    else { Write-Warn "build.bat は成功したが $engineDll が見つからない" }
}

# ---------------------------------------------------------------------------
# 5. 任意: OBS ランタイム同梱 / 起動プロファイル
# ---------------------------------------------------------------------------
if ($Bundle -and -not $Check) {
    Write-Head "OBS ランタイム同梱"
    if (-not $ObsInstall) {
        Write-Fail "-Bundle にはインストール済み OBS が要る（そこから必要部分をコピーする）"
    } else {
        # ObsRuntime.Locate() が App の隣の obs-runtime\ を最優先で見る。
        $dest = Join-Path $RepoRoot 'src\Switcher.App\bin\Release\net9.0-windows\obs-runtime'
        try {
            & (Join-Path $RepoRoot 'tools\bundle-obs-runtime.ps1') -Source $ObsInstall -Destination $dest
            Write-Ok "obs-runtime を同梱した（配布物は GPL-2.0 になる。LICENSE 参照）"
        } catch {
            Write-Fail "bundle-obs-runtime.ps1 に失敗した: $_"
        }
    }
}

if ($WriteLaunchSettings -and -not $Check -and $ObsInstall) {
    Write-Head "launchSettings.json"
    # 通常は不要。ObsRuntime.AddDllSearchDirectory が実行時に bin\64bit を DLL 検索パスへ
    # 追加するので、素の .exe 起動でも obs.dll は解決する。VS の起動プロファイルを
    # 明示したい場合のためだけに用意している。gitignore 済みなのでマシン固有パスでよい。
    $props = Join-Path $RepoRoot 'src\Switcher.App\Properties'
    New-Item -ItemType Directory -Force -Path $props | Out-Null
    @{
        profiles = @{
            'Switcher.App' = @{
                commandName = 'Project'
                environmentVariables = @{ PATH = "$(Join-Path $ObsInstall 'bin\64bit');$env:PATH" }
            }
        }
    } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $props 'launchSettings.json') -Encoding utf8
    Write-Ok "launchSettings.json を生成した"
}

# ---------------------------------------------------------------------------
# 6. 結果
# ---------------------------------------------------------------------------
Write-Head "結果"

if ($script:Failures.Count -gt 0) {
    Write-Host "未解決 $($script:Failures.Count) 件:" -ForegroundColor Red
    $script:Failures | ForEach-Object { Write-Host "  - $_" }
}
if ($script:Warnings.Count -gt 0) {
    Write-Host "警告 $($script:Warnings.Count) 件:" -ForegroundColor Yellow
    $script:Warnings | ForEach-Object { Write-Host "  - $_" }
}

if ($script:Failures.Count -gt 0) {
    Write-Host ""
    Write-Host "準備未完了" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "準備完了" -ForegroundColor Green
if (-not $Check -and -not $CI) {
    Write-Host @"

Visual Studio で HybridSwitcher.sln を開き、構成 Debug|x64 で Switcher.App を
スタートアップに設定して F5。switcher-engine.dll は csproj がビルド時に
App 出力へコピーする。

版を切り替えるとき:  .\setup.ps1 -ObsVersion <version> -Force
状態だけ見るとき:    .\setup.ps1 -Check
"@
}

# 明示しないと直前のネイティブコマンドの $LASTEXITCODE がそのまま終了コードになる。
# .NOTES の「0 = 準備完了」を守るため、成功パスでも必ず 0 を返す。
exit 0
