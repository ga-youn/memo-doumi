param(
    [int]$Seconds = 600,
    [int]$NoteCount = 1000,
    [string]$SourceDir = '',
    [string]$Label = 'run',
    [switch]$NoForcedGc
)
$ErrorActionPreference = 'Stop'
if ($NoteCount -lt 100 -or $Seconds -lt 120) { throw 'Use at least 100 notes and 120 seconds' }
$root = Split-Path $PSScriptRoot -Parent
if (-not $SourceDir) { $SourceDir = Join-Path $root 'src' }
$SourceDir = (Resolve-Path $SourceDir).Path

$template = Join-Path $root "build\stress-template-$NoteCount"
if (-not (Test-Path (Join-Path $template 'notes'))) {
    New-Item -ItemType Directory -Path (Join-Path $template 'notes') -Force | Out-Null
    $md5 = [Security.Cryptography.MD5]::Create()
    for ($i = 0; $i -lt $NoteCount; $i++) {
        $id = -join ($md5.ComputeHash([Text.Encoding]::UTF8.GetBytes("stress-note-$i")) | ForEach-Object { $_.ToString('x2') })
        $body = "---`nid: $id`nsize: 270x250`npinned: false`nformat: md`norder: $($i+1)`n---`nSynthetic $i`n" + ('benchmark text ' * 140)
        [IO.File]::WriteAllText((Join-Path $template "notes\$id.md"), $body)
    }
    [IO.File]::WriteAllText((Join-Path $template 'settings.json'), '{"NotesVisible":false,"StayPut":true}')
}

$dir = Join-Path $root ('build\stress-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Label)
New-Item -ItemType Directory -Path $dir -Force | Out-Null
Copy-Item $template (Join-Path $dir 'data') -Recurse
Copy-Item (Join-Path $root 'assets\sprites') $dir -Recurse

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $dir 'StressMemo.exe'
& $csc /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu32bitpreferred /main:MemoDoumi.StressMain /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll "/win32manifest:$root\app.manifest" "/out:$exe" (Join-Path $SourceDir '*.cs') (Join-Path $root 'tools\StressMain.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compile failed' }
Get-ChildItem (Join-Path $SourceDir '*.cs'), (Join-Path $root 'tools\StressMain.cs') | Get-FileHash |
    Select-Object @{n='File';e={Split-Path $_.Path -Leaf}}, Hash | Export-Csv (Join-Path $dir 'source-hashes.csv') -NoTypeInformation
[pscustomobject]@{
    Started = (Get-Date -Format o); Label = $Label; Seconds = $Seconds; Notes = $NoteCount; ForcedGc = (-not $NoForcedGc)
    SourceDir = $SourceDir; OS = (Get-CimInstance Win32_OperatingSystem).Caption; CPU = (Get-CimInstance Win32_Processor).Name
    LogicalProcessors = [Environment]::ProcessorCount
} | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $dir 'environment.json')

$procArgs = @("$Seconds")
if ($NoForcedGc) { $procArgs += '--no-gc' }
$p = Start-Process $exe -ArgumentList $procArgs -WorkingDirectory $dir -WindowStyle Hidden -PassThru
Write-Output "Artifacts: $dir; PID: $($p.Id)"
try {
    while (!$p.WaitForExit(1000)) { if (((Get-Date) - $p.StartTime).TotalSeconds -gt ($Seconds + 180)) { throw 'Timeout' } }
    if (Test-Path (Join-Path $dir 'failure.txt')) { Get-Content (Join-Path $dir 'failure.txt'); throw 'Harness failed' }
    $notes = @(Get-ChildItem (Join-Path $dir 'data\notes') -Filter *.md -File).Count
    $trash = 0
    if (Test-Path (Join-Path $dir 'data\trash')) { $trash = @(Get-ChildItem (Join-Path $dir 'data\trash') -File).Count }
    $leftover = @(Get-ChildItem (Join-Path $dir 'data') -Recurse -File -Filter *.tmp).Count
    Add-Content (Join-Path $dir 'counts.txt') "note_files=$notes`r`ntrash_files=$trash`r`ntmp_leftover=$leftover`r`nexit_code=$($p.ExitCode)"
    Get-Content (Join-Path $dir 'counts.txt')
    if (Test-Path (Join-Path $dir 'data\error.log')) { throw 'Application logged errors' }
    Write-Output "Completed: exit=$($p.ExitCode)"
} finally { if (!$p.HasExited) { Stop-Process -Id $p.Id } }
