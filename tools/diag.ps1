$log = "F:\订单管理\shots\diag.log"
Remove-Item $log -ErrorAction SilentlyContinue
function L($m) { $m | Out-File -FilePath $log -Append -Encoding UTF8 }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$uia = [System.Windows.Automation.AutomationElement]
L ("uia type=" + $uia.FullName)
L ("ctprop null? " + ($null -eq $uia::ControlTypeProperty))
L ("controltype=" + [System.Windows.Automation.ControlType]::RadioButton)
$rc = New-Object System.Windows.Automation.PropertyCondition($uia::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
L ("rc null? " + ($null -eq $rc))
$app = Get-Process | Where-Object { $_.ProcessName -like '*代肝*' } | Select-Object -First 1
L ("pid=" + $app.Id + " hwnd=" + $app.MainWindowHandle)
$el = $uia::FromHandle([IntPtr]$app.MainWindowHandle)
L ("el=" + $el.Current.Name)
$all = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rc)
L ("radios=" + $all.Count)
