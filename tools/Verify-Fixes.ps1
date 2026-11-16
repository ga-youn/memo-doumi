param([string]$BaselineDir = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('build\verify-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $run | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Runtime.Serialization.dll', '/r:System.Xml.dll'

function Build([string]$src, [string]$main, [string]$entry, [string]$out) {
    $extra = @()
    if ($entry -eq "VerifyMain.cs") { $extra += Join-Path $root "tools\VerificationAccess.cs" }
    & $csc /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu32bitpreferred "/main:$main" @refs "/win32manifest:$root\app.manifest" "/out:$out" (Join-Path $src '*.cs') (Join-Path $root "tools\$entry") @extra
    if ($LASTEXITCODE -ne 0) { throw "Compile failed: $entry" }
}

function Invoke-Phase([string]$dir, [string]$scenario, [string]$phase) {
    $marker = Join-Path $dir "$scenario-$phase.complete"
    if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
    $p = Start-Process (Join-Path $dir 'VerifyMemo.exe') -ArgumentList $scenario, $phase -WorkingDirectory $dir -WindowStyle Hidden -PassThru
    if (!$p.WaitForExit(120000)) { Stop-Process -Id $p.Id; throw "Timeout: $scenario/$phase" }
    if ($p.ExitCode -ne 0) { throw "Failed: $scenario/$phase (exit=$($p.ExitCode)); see $dir\results.txt" }
    if (!(Test-Path -LiteralPath $marker) -or [IO.File]::ReadAllText($marker).Trim() -ne "0") { throw "Incomplete phase: $scenario/$phase" }
}

$exe = Join-Path $run 'VerifyMemo.exe'
Build (Join-Path $root 'src') 'MemoDoumi.VerifyMain' 'VerifyMain.cs' $exe

$plan = [ordered]@{
    codec    = @('run')
    links    = @('run')
    ids      = @('prepare', 'run1', 'run2')
    migrate  = @('prepare', 'run1', 'run2', 'run3')
    resume   = @('prepare', 'run1', 'run2', 'run3')
    savefail = @('prepare', 'run')
    evict    = @('prepare', 'run1', 'run2')
    search   = @('prepare', 'run')
    external = @('prepare', 'run1', 'run2', 'run3', 'run4')
    delete   = @('prepare', 'run')
    live     = @('prepare', 'run')
    fonts    = @('prepare', 'run')
}
$all = @()
$earlyDir = Join-Path $run 'earlyexit'
New-Item -ItemType Directory -Path $earlyDir | Out-Null
Copy-Item $exe $earlyDir
Invoke-Phase $earlyDir 'earlyexit' 'prepare'
$rejected = $false
try { Invoke-Phase $earlyDir 'earlyexit' 'run' }
catch { $rejected = $true }
$earlyResults = Join-Path $earlyDir 'results.txt'
$expectedFailure = Test-Path -LiteralPath $earlyResults
if ($expectedFailure) {
    $expectedFailure = [IO.File]::ReadAllText($earlyResults).Contains("FAIL`tearlyexit/run`tUI steps completed`t1/2")
}
if (!$rejected -or !$expectedFailure -or (Test-Path (Join-Path $earlyDir 'unexpected-step.txt'))) {
    throw "Early-exit regression failed: $earlyDir"
}
$all += "PASS`trunner/earlyexit`tNormal exit with unexecuted UI steps is rejected"
foreach ($scenario in $plan.Keys) {
    $dir = Join-Path $run $scenario
    New-Item -ItemType Directory -Path $dir | Out-Null
    Copy-Item $exe $dir
    Copy-Item (Join-Path $root 'assets\sprites') $dir -Recurse
    Copy-Item (Join-Path $root 'assets\fonts') $dir -Recurse
    foreach ($phase in $plan[$scenario]) { Invoke-Phase $dir $scenario $phase }
    $results = Join-Path $dir 'results.txt'
    if (Test-Path $results) { $all += Get-Content $results -Encoding UTF8 } else { $all += "FAIL`t$scenario`t결과 파일 없음" }
}

$sources = [ordered]@{ after = (Join-Path $root 'src') }
if ($BaselineDir) { $sources['before'] = (Resolve-Path $BaselineDir).Path }
foreach ($label in $sources.Keys) {
    $dir = Join-Path $run "collision-$label"
    New-Item -ItemType Directory -Path $dir | Out-Null
    Build $sources[$label] 'MemoDoumi.CollisionMain' 'CollisionMain.cs' (Join-Path $dir 'Collision.exe')
    $p = Start-Process (Join-Path $dir 'Collision.exe') -WorkingDirectory $dir -WindowStyle Hidden -PassThru
    if (!$p.WaitForExit(60000)) { Stop-Process -Id $p.Id; throw 'Timeout: collision' }
    $text = (Get-Content (Join-Path $dir 'collision.txt') -Encoding UTF8) -join ' | '
    if ($label -eq 'after') { $all += ($(if ($p.ExitCode -eq 0) { 'PASS' } else { 'FAIL' }) + "`tcollision/after`t제목·앞 8자가 같은 두 메모가 모두 남음`t$text") }
    else { $all += "INFO`tcollision/before`t변경 전 코드의 결과 (exit=$($p.ExitCode))`t$text" }
}

$round = @{}
foreach ($label in $sources.Keys) {
    $dir = Join-Path $run "roundtrip-$label"
    New-Item -ItemType Directory -Path $dir | Out-Null
    Build $sources[$label] 'MemoDoumi.RoundtripMain' 'RoundtripMain.cs' (Join-Path $dir 'Roundtrip.exe')
    $p = Start-Process (Join-Path $dir 'Roundtrip.exe') -WorkingDirectory $dir -WindowStyle Hidden -PassThru
    if (!$p.WaitForExit(120000)) { Stop-Process -Id $p.Id; throw 'Timeout: roundtrip' }
    $round[$label] = [IO.File]::ReadAllText((Join-Path $dir 'roundtrip.txt'))
    $cases = ([regex]::Matches($round[$label], '(?m)^#\d+ ')).Count
    $all += ($(if ($p.ExitCode -eq 0 -and $cases -gt 0) { 'PASS' } else { 'FAIL' }) + "`troundtrip/$label`t서식을 넣었다 읽기를 되풀이해도 결과가 같음`t$cases 건, 불안정 $($p.ExitCode)")
}
if ($round.ContainsKey('before')) {
    $all += ($(if ($round['before'] -ceq $round['after']) { 'PASS' } else { 'FAIL' }) + "`troundtrip/compare`t변경 전 코드와 서식 읽기 결과가 완전히 같음`t$($round['after'].Length) 글자")
}

$all | Set-Content -Encoding UTF8 (Join-Path $run 'results-all.txt')
$pass = @($all | Where-Object { $_ -like 'PASS*' }).Count
$fail = @($all | Where-Object { $_ -like 'FAIL*' })
$all | Where-Object { $_ -like 'INFO*' }
$fail
Write-Output "Artifacts: $run"
Write-Output "PASS=$pass FAIL=$($fail.Count)"
if ($fail.Count -gt 0) { exit 1 }
