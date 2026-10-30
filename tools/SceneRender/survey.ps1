# Batch-render every scene wallpaper at small size: success rate and failure classification.
$root = 'F:\steam\steamapps\workshop\content\431960'
$out  = Join-Path $PSScriptRoot '_out\survey'
$exe  = Join-Path $PSScriptRoot 'bin\Release\net10.0-windows\SceneRender.dll'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$rows = @()
$ids = Get-ChildItem $root -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'scene.pkg') } | ForEach-Object { $_.Name }
foreach ($id in $ids) {
    $pkg = Join-Path $root "$id\scene.pkg"
    $o   = Join-Path $out "$id.png"
    $sw  = [Diagnostics.Stopwatch]::StartNew()
    $log = & dotnet $exe render $pkg 660 430 $o --verbose 2>&1
    $code = $LASTEXITCODE
    $sw.Stop()
    $txt = ($log | Out-String)
    $m = [regex]::Match($txt, 'STATS drew=(\d+) skipped=(\d+) video=(\d+)')
    $drew = 0; $skip = 0; $vid = 0
    if ($m.Success) { $drew = [int]$m.Groups[1].Value; $skip = [int]$m.Groups[2].Value; $vid = [int]$m.Groups[3].Value }
    $err = ''
    if ($txt -match 'Exception') { $err = (($txt -split "`n") | Where-Object { $_ -match 'Exception' } | Select-Object -First 1).Trim() }
    $ok = (Test-Path $o) -and ($code -eq 0)
    $rows += [pscustomobject]@{ id = $id; ok = $ok; code = $code; sec = [math]::Round($sw.Elapsed.TotalSeconds, 2); drew = $drew; skip = $skip; video = $vid; err = $err }
    Write-Host ("{0} ok={1} code={2} {3}s drew={4} skip={5} vid={6} {7}" -f $id, $ok, $code, $rows[-1].sec, $drew, $skip, $vid, $err)
}
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $out 'survey.csv')
$okCount = ($rows | Where-Object ok).Count
Write-Host "TOTAL=$($rows.Count) OK=$okCount"
