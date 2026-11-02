# Генерирует app.ico (16–256 px) для TS Overlay.
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot "..\src\TsOverlay\app.ico"
$sizes = 16, 24, 32, 48, 64, 128, 256

function New-IconBitmap([int]$size) {
    $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([Drawing.Color]::Transparent)

    $pad = [Math]::Max(0, [int]($size * 0.04))
    $rect = New-Object Drawing.Rectangle $pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad)
    $radius = [Math]::Max(3, [int]($size * 0.22))
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [Drawing.Color]::FromArgb(255, 28, 108, 224),
        [Drawing.Color]::FromArgb(255, 38, 198, 218),
        45.0)
    $g.FillPath($brush, $path)

    $fontSize = [Math]::Max(6, [single]($size * 0.44))
    $font = New-Object Drawing.Font("Segoe UI", $fontSize, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $format = New-Object Drawing.StringFormat
    $format.Alignment = [Drawing.StringAlignment]::Center
    $format.LineAlignment = [Drawing.StringAlignment]::Center
    $g.DrawString("TS", $font, [Drawing.Brushes]::White, (New-Object Drawing.RectangleF 0, 0, $size, $size), $format)

    # зелёная точка «говорит»
    $dot = [Math]::Max(3, [int]($size * 0.22))
    $dotBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 74, 222, 128))
    $g.FillEllipse($dotBrush, ($size - $dot - $pad - [Math]::Max(1, [int]($size * 0.06))), ($size - $dot - $pad - [Math]::Max(1, [int]($size * 0.06))), $dot, $dot)

    $g.Dispose()
    $font.Dispose()
    $brush.Dispose()
    $dotBrush.Dispose()
    $path.Dispose()
    $format.Dispose()
    return $bmp
}

$frames = @{}
foreach ($s in $sizes) {
    $frames[$s] = New-IconBitmap $s
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([UInt16]0)      # reserved
$bw.Write([UInt16]1)      # type icon
$bw.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
$pngData = @{}
foreach ($s in $sizes) {
    $png = New-Object System.IO.MemoryStream
    $frames[$s].Save($png, [Drawing.Imaging.ImageFormat]::Png)
    $pngData[$s] = $png.ToArray()
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0)    # colors
    $bw.Write([byte]0)    # reserved
    $bw.Write([UInt16]1)  # planes
    $bw.Write([UInt16]32) # bpp
    $bw.Write([UInt32]$pngData[$s].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngData[$s].Length
}

foreach ($s in $sizes) {
    $bw.Write($pngData[$s])
}
$bw.Flush()
[System.IO.File]::WriteAllBytes($out, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()
foreach ($s in $sizes) { $frames[$s].Dispose() }
Write-Host "written: $out"
