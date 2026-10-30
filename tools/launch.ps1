$root = Split-Path $PSScriptRoot -Parent
$exe = (Get-ChildItem (Join-Path $root "src\OrderManager\bin\Debug\net10.0-windows") -Filter *.exe | Select-Object -First 1).FullName
$pname = [System.IO.Path]::GetFileNameWithoutExtension($exe)
Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { $_.Kill() }
Start-Sleep -Milliseconds 800
& cmd /c start "" $exe
$app = $null
for ($i = 0; $i -lt 40; $i++) {
  Start-Sleep -Milliseconds 500
  $app = Get-Process | Where-Object { $_.ProcessName -eq $pname } | Select-Object -First 1
  if ($app -and $app.MainWindowHandle -ne 0) { break }
}
if (-not $app) { Write-Output "launch failed"; exit 1 }
Start-Sleep -Seconds 3
Write-Output ("pid=" + $app.Id + " title=" + $app.MainWindowTitle + " hwnd=" + $app.MainWindowHandle)
