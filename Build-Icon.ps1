<#
    Generates icon.ico for Stale Device Manager.
    Theme: blue rounded square (matches app header), white monitor,
    amber clock badge (device + staleness/time). Multi-resolution .ico.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = Split-Path -Parent $PSCommandPath
$icoPath = Join-Path $outDir 'icon.ico'
$sizes = 16,24,32,48,64,128,256

function New-Png([int]$S) {
    $bmp = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    # draw everything in a 256 coordinate space, scaled to S
    $g.ScaleTransform($S/256, $S/256)

    # rounded-rect path helper
    function RR([single]$x,[single]$y,[single]$w,[single]$h,[single]$r) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r*2
        $p.AddArc($x, $y, $d, $d, 180, 90)
        $p.AddArc($x+$w-$d, $y, $d, $d, 270, 90)
        $p.AddArc($x+$w-$d, $y+$h-$d, $d, $d, 0, 90)
        $p.AddArc($x, $y+$h-$d, $d, $d, 90, 90)
        $p.CloseFigure()
        return $p
    }

    # background gradient rounded square
    $bg = RR 10 10 236 236 46
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0,10)), (New-Object System.Drawing.Point(0,246)),
        [System.Drawing.Color]::FromArgb(255,27,125,203), [System.Drawing.Color]::FromArgb(255,15,61,92))
    $g.FillPath($bgBrush, $bg)

    # monitor (white)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $screenOuter = RR 44 56 152 106 16
    $g.FillPath($white, $screenOuter)
    # inner screen
    $screenBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(56,68)), (New-Object System.Drawing.Point(56,150)),
        [System.Drawing.Color]::FromArgb(255,90,169,224), [System.Drawing.Color]::FromArgb(255,46,123,184))
    $screenInner = RR 56 68 128 82 8
    $g.FillPath($screenBrush, $screenInner)
    # stand
    $g.FillRectangle($white, 111, 162, 20, 16)
    $standBase = RR 84 176 78 14 6
    $g.FillPath($white, $standBase)

    # separation ring (background color) so badge detaches from monitor
    $sep = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255,16,55,84))
    $g.FillEllipse($sep, 140, 136, 100, 100)

    # amber clock badge
    $amber = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(148,144)), (New-Object System.Drawing.Point(148,228)),
        [System.Drawing.Color]::FromArgb(255,224,169,58), [System.Drawing.Color]::FromArgb(255,201,138,0))
    $g.FillEllipse($amber, 148, 144, 84, 84)   # center (190,186) r=42

    # clock hands (white)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 7)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLine($pen, 190, 186, 190, 160)   # hour hand up
    $g.DrawLine($pen, 190, 186, 212, 192)   # minute hand right
    $g.FillEllipse($white, 184, 180, 12, 12) # center dot

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

# build PNGs
$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = New-Png $s }

# assemble ICO
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)            # reserved
$bw.Write([UInt16]1)            # type = icon
$bw.Write([UInt16]$sizes.Count) # count

$offset = 6 + (16 * $sizes.Count)
foreach ($s in $sizes) {
    $len = $pngs[$s].Length
    $bw.Write([Byte]($(if ($s -ge 256) {0} else {$s})))  # width
    $bw.Write([Byte]($(if ($s -ge 256) {0} else {$s})))  # height
    $bw.Write([Byte]0)    # palette
    $bw.Write([Byte]0)    # reserved
    $bw.Write([UInt16]1)  # planes
    $bw.Write([UInt16]32) # bpp
    $bw.Write([UInt32]$len)
    $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
$bw.Flush(); $bw.Close(); $fs.Close()

Write-Host "Icon written: $icoPath ($([math]::Round((Get-Item $icoPath).Length/1kb,1)) KB, $($sizes.Count) sizes)" -ForegroundColor Green
