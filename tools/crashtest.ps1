$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root ("src\OrderManager\bin\Debug\net10.0-windows\" + [char]0x4EE3 + [char]0x809D + [char]0x7BA1 + [char]0x5BB6 + ".exe")
$pname = [System.IO.Path]::GetFileNameWithoutExtension($exe)
$data = Join-Path $root "src\OrderManager\bin\Debug\net10.0-windows\data"
$db = Join-Path $data "app.db"
$base = Join-Path $PSScriptRoot "base-clean.db"
$tools = $PSScriptRoot

function Kill-App {
  Get-Process | Where-Object { $_.ProcessName -eq $pname } | ForEach-Object { try { $_.Kill() } catch {} }
  Start-Sleep -Milliseconds 800
}

Kill-App
Remove-Item $db -Force -ErrorAction SilentlyContinue
$p0 = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 7
Write-Output ("base launch alive=" + (-not $p0.HasExited))
Kill-App
Copy-Item $db $base -Force
Write-Output ("base rows copied to " + $base)

$today = (Get-Date).ToString("yyyy-MM-dd")
$now = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
$variants = @(
  @{ n = "A-amount0-defaults"; sql = "insert into orders (boss_name,item,quantity,amount,personal,status,order_date,created_at,updated_at) values ('V1','代肝服务',1,0,1,'进行中','$today','$now','$now')" },
  @{ n = "B-emptyitem";        sql = "insert into orders (boss_name,item,quantity,amount,personal,status,order_date,created_at,updated_at) values ('V2','',1,50,1,'进行中','$today','$now','$now')" },
  @{ n = "C-normal";           sql = "insert into orders (boss_name,item,quantity,amount,personal,status,order_date,created_at,updated_at) values ('V3','代肝服务',1,50,1,'进行中','$today','$now','$now')" },
  @{ n = "D-emptyboss";        sql = "insert into orders (boss_name,item,quantity,amount,personal,status,order_date,created_at,updated_at) values ('','代肝服务',1,50,1,'进行中','$today','$now','$now')" }
)

foreach ($v in $variants) {
  Kill-App
  Copy-Item $base $db -Force
  Push-Location $tools
  $r = (& dotnet run dbmod.cs -- $db $v.sql 2>&1) -join " / "
  Pop-Location
  $p = Start-Process -FilePath $exe -PassThru
  Start-Sleep -Seconds 7
  $alive = -not $p.HasExited
  Write-Output ($v.n + " => " + $(if ($alive) { "ALIVE" } else { "CRASH" }) + " | " + $r)
  if ($alive) { try { $p.Kill() } catch {} }
  Start-Sleep -Milliseconds 500
}
Kill-App
Write-Output "done"
