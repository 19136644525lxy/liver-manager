Add-Type -AssemblyName System.Drawing

$dir = Join-Path $PSScriptRoot '_out\survey'
$files = Get-ChildItem $dir -Filter '*.png' | Sort-Object Name
$cols = 6
$tileW = 220
$imgH = 138
$labelH = 18
$rows = [math]::Ceiling($files.Count / $cols)
$sheetW = $cols * $tileW
$sheetH = $rows * ($imgH + $labelH)

$bmp = New-Object System.Drawing.Bitmap($sheetW, $sheetH)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(30, 30, 30))
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$font = New-Object System.Drawing.Font('Consolas', 10)
$br = [System.Drawing.Brushes]::White

$i = 0
foreach ($f in $files) {
    $col = $i % $cols
    $row = [math]::Floor($i / $cols)
    $x = $col * $tileW
    $y = $row * ($imgH + $labelH)
    $img = [System.Drawing.Image]::FromFile($f.FullName)
    $g.DrawImage($img, $x, $y, $tileW, $imgH)
    $img.Dispose()
    $g.DrawString($f.BaseName, $font, $br, $x + 3, $y + $imgH + 2)
    $i++
}
$out = Join-Path $dir 'montage.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
Write-Host "wrote $out ($sheetW x $sheetH), tiles=$($files.Count)"
