param(
    [string]$Version = '0.1.0',
    [string]$OutDir = '',
    [switch]$SkipBuild,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $root 'release' }
if ($Version -notmatch '^[0-9A-Za-z][0-9A-Za-z.\-]*$') { throw "Bad version: $Version" }

$guide = [string][char]0xC0AC + [char]0xC6A9 + [char]0xBC95 + '.txt'     # the usage guide file name (Korean), kept ASCII-safe here
$sprites = 'blink.png', 'happy.png', 'sleep.png', 'stand.png', 'wag.png', 'walk1.png', 'walk2.png'

$allow = [ordered]@{}
$allow[(Join-Path $root 'build\MemoDoumi.exe')] = 'MemoDoumi.exe'
$allow[(Join-Path $root "docs\$guide")] = $guide
foreach ($s in $sprites) { $allow[(Join-Path $root "assets\sprites\$s")] = "sprites\$s" }
foreach ($f in 'Pretendard-Regular.ttf', 'Pretendard-Bold.ttf', 'Pretendard-LICENSE.txt') { $allow[(Join-Path $root "assets\fonts\$f")] = "fonts\$f" }

if (-not $SkipBuild) {
    & cmd /c (Join-Path $root 'build.cmd')
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
}
foreach ($src in $allow.Keys) { if (-not (Test-Path -LiteralPath $src -PathType Leaf)) { throw "Missing file: $src" } }

$zip = Join-Path $OutDir "MemoDoumi-$Version.zip"
if ((Test-Path -LiteralPath $zip) -and -not $Force) { throw "Already exists: $zip (use -Force to replace it)" }

$stage = Join-Path $root 'build\package-staging'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
$top = Join-Path $stage 'MemoDoumi'
New-Item -ItemType Directory -Path (Join-Path $top 'sprites') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $top 'fonts') -Force | Out-Null
foreach ($src in $allow.Keys) { Copy-Item -LiteralPath $src -Destination (Join-Path $top $allow[$src]) }

$expected = @($allow.Values | ForEach-Object { 'MemoDoumi/' + ($_ -replace '\\', '/') } | Sort-Object)
$staged = @(Get-ChildItem -LiteralPath $stage -Recurse -File | ForEach-Object { $_.FullName.Substring($stage.Length + 1) -replace '\\', '/' } | Sort-Object)
if (Compare-Object $expected $staged) { throw "Staging folder does not match the allow list: $($staged -join ', ')" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$tmp = "$zip.tmp"
if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $tmp, [IO.Compression.CompressionLevel]::Optimal, $false)

$forbidden = '(^|/)(data|notes|trash|backup[^/]*|docs|build|tools|src)(/|$)|\.(log|csv|json|md|bak|migrated|tmp|done|pending)$'
$archive = [IO.Compression.ZipFile]::OpenRead($tmp)
try { $entries = @($archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' } | Where-Object { -not $_.EndsWith('/') } | Sort-Object) }
finally { $archive.Dispose() }
$bad = @($entries | Where-Object { $_ -match $forbidden })
if ($bad.Count -gt 0 -or (Compare-Object $expected $entries)) {
    Remove-Item -LiteralPath $tmp -Force
    throw "Zip content does not match the allow list: $($entries -join ', ')"
}
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Move-Item -LiteralPath $tmp -Destination $zip
Remove-Item -LiteralPath $stage -Recurse -Force

Write-Output "Packaged $($entries.Count) files -> $zip"
$entries
