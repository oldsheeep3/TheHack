<#
    Builds a single self-contained preview of the multi-page site.

    The site under web/ is the real deliverable: separate HTML files sharing assets/site.css and
    assets/site.js. This script stitches those same files into one document with hash routing and the
    CSS/JS inlined, so the site can be reviewed (or shared) as a single file without duplicating any
    content — the pages remain the single source of truth and the preview is always regenerated.

    Usage:  pwsh web/build-preview.ps1 [-Out <path>]
#>
param(
    [string]$Out = "$PSScriptRoot\preview.html"
)

$ErrorActionPreference = "Stop"

# The site files are UTF-8 without a BOM. Windows PowerShell 5.1 reads such files as the system ANSI
# code page, which turns every Japanese character into mojibake, so every read here is explicit.
$PSDefaultParameterValues['Get-Content:Encoding'] = 'utf8'

$pages = @(
    @{ Id = "home";     File = "index.html";    Label = "ホーム" }
    @{ Id = "features"; File = "features.html"; Label = "機能" }
    @{ Id = "guide";    File = "guide.html";    Label = "使い方" }
    @{ Id = "hardware"; File = "hardware.html"; Label = "ハードウェア" }
    @{ Id = "atem";     File = "atem.html";     Label = "ATEM 連携" }
)

$css = Get-Content "$PSScriptRoot\assets\site.css" -Raw
$js  = Get-Content "$PSScriptRoot\assets\site.js"  -Raw

$sections = foreach ($page in $pages) {
    $html = Get-Content "$PSScriptRoot\$($page.File)" -Raw

    # Take everything between <main> and </main>; the header/footer are rendered once by the shell.
    $match = [regex]::Match($html, '(?s)<main>(.*?)</main>')
    if (-not $match.Success) { throw "no <main> in $($page.File)" }

    $body = $match.Groups[1].Value
    # Rewrite inter-page links to hash routes so navigation works inside the single file.
    foreach ($p in $pages) { $body = $body.Replace("href=`"$($p.File)`"", "href=`"#$($p.Id)`"") }

    "<section class=`"route`" data-route=`"$($page.Id)`" hidden>$body</section>"
}

$nav = ($pages | ForEach-Object {
    $soon = if ($_.Id -in @("hardware", "atem")) { " data-soon" } else { "" }
    "<a href=`"#$($_.Id)`"$soon>$($_.Label)</a>"
}) -join "`n      "

$doc = @"
<!doctype html>
<html lang="ja" data-style="console">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Switcher — サイト プレビュー</title>
<style>
$css
/* Preview shell only: pages become hash routes in the single-file build. */
.route[hidden] { display: none; }
.route > .hero:first-child { padding-top: clamp(40px, 6vw, 84px); }
</style>
</head>
<body>
<header class="site-header">
  <div class="wrap">
    <a class="brand" href="#home"><span class="brand-mark"></span> Switcher</a>
    <nav class="site-nav" aria-label="サイト">
      $nav
    </nav>
  </div>
</header>

<main>
$($sections -join "`n")
</main>

<footer class="site-footer">
  <div class="wrap">
    <span>Switcher — 2026-Team-38</span>
    <span class="spacer"></span>
    <span>この 1 ファイルはレビュー用。配布用の実体は web/ 以下のマルチページです。</span>
  </div>
</footer>

<script>
$js
</script>
<script>
(function () {
  function show() {
    var id = (location.hash || "#home").slice(1);
    var found = false;
    document.querySelectorAll(".route").forEach(function (s) {
      var on = s.dataset.route === id;
      s.hidden = !on;
      if (on) { found = true; }
    });
    if (!found) { document.querySelector('.route[data-route="home"]').hidden = false; }
    document.querySelectorAll(".site-nav a").forEach(function (a) {
      if (a.getAttribute("href") === "#" + id) { a.setAttribute("aria-current", "page"); }
      else { a.removeAttribute("aria-current"); }
    });
    window.scrollTo(0, 0);
  }
  addEventListener("hashchange", show);
  document.addEventListener("DOMContentLoaded", show);
})();
</script>
</body>
</html>
"@

# UTF-8 without a BOM, matching the pages this is built from.
[System.IO.File]::WriteAllText($Out, $doc, (New-Object System.Text.UTF8Encoding $false))
Write-Output "wrote $Out ($([math]::Round((Get-Item $Out).Length / 1kb, 1)) KB)"
