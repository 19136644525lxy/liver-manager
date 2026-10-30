$root = Split-Path $PSScriptRoot -Parent
$log = Join-Path $root "shots\debug.log"
New-Item -ItemType Directory -Force -Path (Join-Path $root "shots") | Out-Null
Remove-Item $log -ErrorAction SilentlyContinue
function L($m) { ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) | Out-File -FilePath $log -Append -Encoding UTF8 }
L "step0 root=$root"
$exe = (Get-ChildItem (Join-Path $root "src\OrderManager\bin\Debug\net10.0-windows") -Filter *.exe | Select-Object -First 1).FullName
L "exe=$exe"
$p = Start-Process -FilePath $exe -PassThru
L "started pid=$($p.Id)"
Start-Sleep -Seconds 8
$p.Refresh()
L "hasExited=$($p.HasExited)"
if ($p.HasExited) { L ("exitcode=" + $p.ExitCode); exit 1 }
L "mainHandle=$($p.MainWindowHandle)"
L "title=$($p.MainWindowTitle)"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
L "uia loaded"
try {
  $uia = [System.Windows.Automation.AutomationElement]
  $cond = New-Object System.Windows.Automation.PropertyCondition($uia::ProcessIdProperty, [int]$p.Id)
  $tops = $uia::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
  L "tops=$($tops.Count)"
  foreach ($t in $tops) { L ("  cls=" + $t.Current.ClassName + " name=" + $t.Current.Name) }
} catch {
  L ("uia query failed: " + $_.Exception.Message)
}
L "step done"
