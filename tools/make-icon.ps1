# Draws the OpenCrosshair icon at every size Windows uses and writes src/OpenCrosshair/app.ico
# plus docs/images/icon.png. Run from anywhere:  powershell -File tools/make-icon.ps1
$root = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.Drawing

function Draw-Icon([int]$S) {
    $bmp = New-Object Drawing.Bitmap $S, $S, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $small = $S -lt 24

    # Dark rounded tile
    $r = [Math]::Max(2, $S * 0.22); $inset = 0.5; $w = $S - 1
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($inset, $inset, $r * 2, $r * 2, 180, 90)
    $path.AddArc($w - $r * 2, $inset, $r * 2, $r * 2, 270, 90)
    $path.AddArc($w - $r * 2, $w - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc($inset, $w - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $tile = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Point 0, 0), (New-Object Drawing.Point 0, $S), ([Drawing.Color]::FromArgb(44, 48, 56)), ([Drawing.Color]::FromArgb(22, 23, 26))
    $g.FillPath($tile, $path)
    if (-not $small) { $edge = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(78, 84, 94)), ([Math]::Max(1, $S / 64)); $g.DrawPath($edge, $path) }

    # Green crosshair: ring, four arms, center dot
    $green = [Drawing.Color]::FromArgb(74, 222, 128)
    $c = $S / 2
    if (-not $small) {
        $ringR = $S * 0.25
        $ring = New-Object Drawing.Pen $green, ([Math]::Max(1.4, $S * 0.065))
        $g.DrawEllipse($ring, $c - $ringR, $c - $ringR, $ringR * 2, $ringR * 2)
    }
    $armW = if ($small) { [Math]::Max(2, $S * 0.13) } else { [Math]::Max(1.6, $S * 0.08) }
    $gap = $S * ($(if ($small) { 0.13 } else { 0.11 }))
    $end = $S * ($(if ($small) { 0.40 } else { 0.40 }))
    $brush = New-Object Drawing.SolidBrush $green
    $len = $end - $gap
    $g.FillRectangle($brush, [single]($c - $armW / 2), [single]($c - $end), [single]$armW, [single]$len)   # up
    $g.FillRectangle($brush, [single]($c - $armW / 2), [single]($c + $gap), [single]$armW, [single]$len)   # down
    $g.FillRectangle($brush, [single]($c - $end), [single]($c - $armW / 2), [single]$len, [single]$armW)   # left
    $g.FillRectangle($brush, [single]($c + $gap), [single]($c - $armW / 2), [single]$len, [single]$armW)   # right
    $dot = [Math]::Max(1.2, $S * ($(if ($small) { 0.07 } else { 0.055 })))
    $g.FillEllipse($brush, [single]($c - $dot), [single]($c - $dot), [single]($dot * 2), [single]($dot * 2))
    $g.Dispose()
    return $bmp
}

# Classic icon entry (what every program can read): BITMAPINFOHEADER, 32-bit pixels bottom-up, then a 1-bit transparency mask.
function To-Dib([Drawing.Bitmap]$bmp) {
    $S = $bmp.Width
    $data = $bmp.LockBits((New-Object Drawing.Rectangle 0, 0, $S, $S), 'ReadOnly', ([Drawing.Imaging.PixelFormat]::Format32bppArgb))
    $px = New-Object byte[] ($S * $S * 4)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $px, 0, $px.Length); $bmp.UnlockBits($data)
    $maskRow = [int][Math]::Floor(($S + 31) / 32) * 4
    $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
    $w.Write([uint32]40); $w.Write([int32]$S); $w.Write([int32]($S * 2)); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]0); $w.Write([uint32]($px.Length + $maskRow * $S)); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $S - 1; $y -ge 0; $y--) { $w.Write($px, $y * $S * 4, $S * 4) }
    for ($y = $S - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskRow
        for ($x = 0; $x -lt $S; $x++) { if ($px[($y * $S + $x) * 4 + 3] -eq 0) { $row[[int][Math]::Floor($x / 8)] = $row[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) } }
        $w.Write($row)
    }
    $w.Flush(); return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    if ($s -ge 256) { $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bytes = $ms.ToArray() }   # 256 px stays PNG, the usual way
    else { $bytes = To-Dib $bmp }
    $bmp.Dispose()
    , $bytes
}

# .ico = 6-byte header, a 16-byte entry per size, then each image's data
$ico = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $root 'src\OpenCrosshair\app.ico'), $ico.ToArray())
New-Item -ItemType Directory -Force (Join-Path $root 'docs\images') | Out-Null
[IO.File]::WriteAllBytes((Join-Path $root 'docs\images\icon.png'), $pngs[-1])
"wrote app.ico ($($sizes -join ', ') px) and docs/images/icon.png"
