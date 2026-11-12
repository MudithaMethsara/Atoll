$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$RepoRoot = Resolve-Path "$PSScriptRoot\.."
$ResourcesDir = Join-Path $RepoRoot "src\Atoll\Resources"
$AssetsDir = Join-Path $RepoRoot "installer\Assets"
New-Item -ItemType Directory -Force -Path $ResourcesDir, $AssetsDir | Out-Null

function New-RoundedRectPath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($Radius * 2, [Math]::Min($Width, $Height))
    if ($d -le 0.5) {
        $path.AddRectangle((New-Object System.Drawing.RectangleF($X, $Y, $Width, $Height)))
        return $path
    }
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $Width - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $Width - $d, $Y + $Height - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $Height - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Render-AtollPngBytes {
    param([int]$Size)

    # Render at 4x supersampling for razor-sharp antialiasing at small icon sizes
    $ss = $Size * 4
    $bmp = New-Object System.Drawing.Bitmap($ss, $ss, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = $ss * 0.06
    $cardSize = $ss - ($pad * 2)
    $cardRadius = $cardSize * 0.24

    # Outer obsidian squircle badge
    $cardPath = New-RoundedRectPath -X $pad -Y $pad -Width $cardSize -Height $cardSize -Radius $cardRadius
    $p1 = New-Object System.Drawing.PointF ([float]$pad), ([float]$pad)
    $p2 = New-Object System.Drawing.PointF ([float]$pad), ([float]($pad + $cardSize))
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $p1,
        $p2,
        [System.Drawing.Color]::FromArgb(255, 28, 30, 38),
        [System.Drawing.Color]::FromArgb(255, 11, 12, 16)
    )
    $g.FillPath($bgBrush, $cardPath)

    # Subtle ambient blue aura behind the pill
    $glowRect = New-Object System.Drawing.RectangleF ([float]($ss * 0.14)), ([float]($ss * 0.22)), ([float]($ss * 0.72)), ([float]($ss * 0.56))
    $glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glowPath.AddEllipse($glowRect)
    $glowBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowPath)
    $glowBrush.CenterColor = [System.Drawing.Color]::FromArgb(145, 10, 132, 255)
    $glowBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 10, 132, 255))
    $g.FillPath($glowBrush, $glowPath)

    # Rim highlight on outer badge
    $rimPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(62, 255, 255, 255), [Math]::Max(2.0, $ss * 0.015))
    $g.DrawPath($rimPen, $cardPath)

    # Pure black Dynamic Island capsule centered in badge
    $pillW = $cardSize * 0.78
    $pillH = $cardSize * 0.34
    $pillX = ($ss - $pillW) * 0.5
    $pillY = ($ss - $pillH) * 0.5
    $pillPath = New-RoundedRectPath -X $pillX -Y $pillY -Width $pillW -Height $pillH -Radius ($pillH * 0.5)

    $pillBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 0, 0))
    $g.FillPath($pillBrush, $pillPath)

    $pillStroke = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(90, 255, 255, 255), [Math]::Max(2.0, $ss * 0.016))
    $g.DrawPath($pillStroke, $pillPath)

    # Left mini waveform bars inside pill
    $barColor = [System.Drawing.Color]::FromArgb(255, 10, 132, 255)
    $barBrush = New-Object System.Drawing.SolidBrush($barColor)
    $barW = [Math]::Max(3.0, $pillW * 0.045)
    $barGap = $barW * 0.85
    $barHeights = @(0.28, 0.50, 0.36)
    $startX = $pillX + ($pillH * 0.34)
    for ($i = 0; $i -lt 3; $i++) {
        $bh = $pillH * $barHeights[$i]
        $bx = $startX + $i * ($barW + $barGap)
        $by = $pillY + ($pillH - $bh) * 0.5
        $bp = New-RoundedRectPath -X $bx -Y $by -Width $barW -Height $bh -Radius ($barW * 0.5)
        $g.FillPath($barBrush, $bp)
        $bp.Dispose()
    }

    # Right privacy/sensor dot inside pill
    $dotSize = $pillH * 0.36
    $dotX = $pillX + $pillW - ($pillH * 0.32) - $dotSize
    $dotY = $pillY + ($pillH - $dotSize) * 0.5
    $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 48, 209, 88))
    $g.FillEllipse($dotBrush, $dotX, $dotY, $dotSize, $dotSize)

    # Downsample 4x -> target size
    $final = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $fg = [System.Drawing.Graphics]::FromImage($final)
    $fg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $fg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $fg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $fg.Clear([System.Drawing.Color]::Transparent)
    $fg.DrawImage($bmp, 0, 0, $Size, $Size)

    $ms = New-Object System.IO.MemoryStream
    $final.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)

    $dotBrush.Dispose()
    $barBrush.Dispose()
    $pillStroke.Dispose()
    $pillBrush.Dispose()
    $pillPath.Dispose()
    $rimPen.Dispose()
    $glowBrush.Dispose()
    $glowPath.Dispose()
    $bgBrush.Dispose()
    $cardPath.Dispose()
    $g.Dispose()
    $bmp.Dispose()
    $fg.Dispose()
    $final.Dispose()

    return ,([byte[]]$ms.ToArray())
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = New-Object System.Collections.Generic.List[object]
foreach ($s in $sizes) {
    [byte[]]$pngBytes = Render-AtollPngBytes -Size $s
    $frames.Add([PSCustomObject]@{ Size = $s; Bytes = $pngBytes })
}

# Write multi-resolution PNG-encoded ICO
$icoPath = Join-Path $ResourcesDir "Atoll.ico"
$fs = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    # ICONDIR header
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$frames.Count)

    $offset = 6 + (16 * $frames.Count)
    foreach ($f in $frames) {
        [byte]$dim = if ($f.Size -ge 256) { 0 } else { [byte]$f.Size }
        $bw.Write($dim)              # bWidth
        $bw.Write($dim)              # bHeight
        $bw.Write([byte]0)           # bColorCount
        $bw.Write([byte]0)           # bReserved
        $bw.Write([uint16]1)         # wPlanes
        $bw.Write([uint16]32)        # wBitCount
        $bw.Write([uint32]$f.Bytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $f.Bytes.Length
    }

    foreach ($f in $frames) {
        [byte[]]$bytes = $f.Bytes
        $bw.Write($bytes, 0, $bytes.Length)
    }
}
finally {
    $bw.Dispose()
    $fs.Dispose()
}

# Write MSIX assets
[System.IO.File]::WriteAllBytes((Join-Path $AssetsDir "Square44x44Logo.png"), (Render-AtollPngBytes -Size 44))
[System.IO.File]::WriteAllBytes((Join-Path $AssetsDir "Square150x150Logo.png"), (Render-AtollPngBytes -Size 150))

Write-Host "Generated multi-res icon: $icoPath" -ForegroundColor Green
Write-Host "Generated MSIX logos in: $AssetsDir" -ForegroundColor Green
