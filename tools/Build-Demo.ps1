param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$run=Join-Path $root ('build\demo-'+(Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path $run | Out-Null
Copy-Item (Join-Path $root 'assets\sprites') $run -Recurse
Copy-Item (Join-Path $root 'assets\fonts') $run -Recurse
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /codepage:65001 /target:winexe /main:MemoDoumi.DemoMain /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll "/out:$run\Demo.exe" (Join-Path $root 'src\*.cs') (Join-Path $root 'tools\DemoMain.cs')
if($LASTEXITCODE -ne 0){throw 'Demo compilation failed'}
$p=Start-Process (Join-Path $run 'Demo.exe') -ArgumentList ('"'+(Join-Path $run 'frames')+'"') -WorkingDirectory $run -WindowStyle Hidden -PassThru
if(!$p.WaitForExit(60000)){Stop-Process -Id $p.Id; throw 'Demo rendering timeout'}
if($p.ExitCode -ne 0){throw 'Demo rendering failed'}
& python (Join-Path $root "tools\encode_demo.py") (Join-Path $run "frames") (Join-Path $root "docs\demo.gif")
if($LASTEXITCODE -ne 0){throw "GIF encoding failed"}
Write-Output $run
