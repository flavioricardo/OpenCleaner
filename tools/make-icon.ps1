# Gera assets\FreeCleaner.ico (PNG embutido em ICO: 16, 32, 48 e 256 px).
param([string]$Out = (Join-Path $PSScriptRoot '..\assets\FreeCleaner.ico'))

Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)

    # Fundo: círculo com degradê verde-azulado
    $rect = New-Object Drawing.RectangleF 0, 0, ($s - 1), ($s - 1)
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush $rect, ([Drawing.Color]::FromArgb(0, 184, 160)), ([Drawing.Color]::FromArgb(0, 105, 140)), 45
    $g.FillEllipse($grad, $rect)

    # Vassoura estilizada: cabo + cerdas
    $w = [Drawing.Color]::White
    $pen = New-Object Drawing.Pen $w, ([Math]::Max(1.5, $s * 0.075))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLine($pen, $s * 0.66, $s * 0.20, $s * 0.46, $s * 0.52)
    $brush = New-Object Drawing.SolidBrush $w
    $pts = @(
        (New-Object Drawing.PointF ($s * 0.36), ($s * 0.46)),
        (New-Object Drawing.PointF ($s * 0.56), ($s * 0.58)),
        (New-Object Drawing.PointF ($s * 0.44), ($s * 0.80)),
        (New-Object Drawing.PointF ($s * 0.20), ($s * 0.70))
    )
    $g.FillPolygon($brush, $pts)

    # Brilho
    if ($s -ge 32) {
        $sp = $s * 0.10
        $cx = $s * 0.72; $cy = $s * 0.66
        $star = @(
            (New-Object Drawing.PointF $cx, ($cy - $sp)), (New-Object Drawing.PointF ($cx + $sp * 0.28), ($cy - $sp * 0.28)),
            (New-Object Drawing.PointF ($cx + $sp), $cy), (New-Object Drawing.PointF ($cx + $sp * 0.28), ($cy + $sp * 0.28)),
            (New-Object Drawing.PointF $cx, ($cy + $sp)), (New-Object Drawing.PointF ($cx - $sp * 0.28), ($cy + $sp * 0.28)),
            (New-Object Drawing.PointF ($cx - $sp), $cy), (New-Object Drawing.PointF ($cx - $sp * 0.28), ($cy - $sp * 0.28))
        )
        $g.FillPolygon($brush, $star)
    }
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 32, 48, 256
$images = foreach ($s in $sizes) { ,(New-IconPng $s) }

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [IO.File]::Create($Out)
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Host "Icone gerado: $Out"
