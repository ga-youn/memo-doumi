param([int]$Seconds = 30)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('build\audit-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $run | Out-Null
$harness = @"
using System;
using System.Windows.Forms;
namespace MemoDoumi {
internal static class AuditMain {
[STAThread] static void Main() {
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
var app = new App();
var timer = new Timer();
timer.Interval = $(($Seconds + 15) * 1000);
timer.Tick += (s,e) => { timer.Stop(); app.Exit(); };
timer.Start(); app.Run(); timer.Dispose();
}
}
}
"@
$harness | Set-Content -Encoding UTF8 (Join-Path $run 'AuditMain.cs')
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$results = @()
foreach ($count in @(1,1000)) {
    $dir = Join-Path $run "notes-$count"
    $notes = Join-Path $dir 'data\notes'
    New-Item -ItemType Directory -Path $notes -Force | Out-Null
    Copy-Item (Join-Path $root 'assets\sprites') $dir -Recurse
    for ($i=1; $i -le $count; $i++) {
        $id = $i.ToString('x32')
        $body = "---`nid: $id`nsize: 270x250`npinned: false`nformat: md`norder: $i`n---`nSynthetic note $i`n" + ('benchmark text ' * 140)
        [IO.File]::WriteAllText((Join-Path $notes "$id.md"), $body)
    }
    '{"NotesVisible":false,"StayPut":false}' | Set-Content -Encoding UTF8 (Join-Path $dir 'data\settings.json')
    $exe = Join-Path $dir 'AuditMemo.exe'
    & $csc /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu32bitpreferred /main:MemoDoumi.AuditMain /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll "/out:$exe" (Join-Path $root 'src\*.cs') (Join-Path $run 'AuditMain.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Compile failed' }
    $p = Start-Process $exe -WorkingDirectory $dir -WindowStyle Hidden -PassThru
    try {
        Start-Sleep -Seconds 5
        $p.Refresh()
        $cpu0 = $p.TotalProcessorTime.TotalSeconds
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $samples = @()
        for ($j=0; $j -lt $Seconds; $j++) {
            Start-Sleep -Seconds 1
            $p.Refresh()
            if ($p.HasExited) { throw 'Unexpected exit' }
            $samples += [pscustomobject]@{Second=$clock.Elapsed.TotalSeconds;WorkingSetMiB=$p.WorkingSet64/1MB;PrivateMiB=$p.PrivateMemorySize64/1MB;Handles=$p.HandleCount;CpuSeconds=$p.TotalProcessorTime.TotalSeconds}
        }
        $cpu = 100*($p.TotalProcessorTime.TotalSeconds-$cpu0)/$clock.Elapsed.TotalSeconds
        $samples | Export-Csv (Join-Path $dir 'samples.csv') -NoTypeInformation
        $results += [pscustomobject]@{Notes=$count;Seconds=$clock.Elapsed.TotalSeconds;CpuOneCorePercent=$cpu;CpuMachinePercent=$cpu/[Environment]::ProcessorCount;WorkingSetMeanMiB=($samples.WorkingSetMiB|Measure-Object -Average).Average;WorkingSetMaxMiB=($samples.WorkingSetMiB|Measure-Object -Maximum).Maximum;PrivateMeanMiB=($samples.PrivateMiB|Measure-Object -Average).Average;PrivateDeltaMiB=$samples[-1].PrivateMiB-$samples[0].PrivateMiB;HandlesEnd=$samples[-1].Handles}
        if (!$p.WaitForExit(20000)) { throw 'Timed exit failed' }
    } finally { if (!$p.HasExited) { Stop-Process -Id $p.Id } }
}
$report = [pscustomobject]@{Date=(Get-Date -Format o);OS=(Get-CimInstance Win32_OperatingSystem).Caption;CPU=(Get-CimInstance Win32_Processor).Name;LogicalProcessors=[Environment]::ProcessorCount;Results=$results}
$report | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $run 'results.json')
$report | ConvertTo-Json -Depth 5
Write-Output "Artifacts: $run"
