param(
    [string]$AssetsDirectory = (Join-Path $PSScriptRoot '..\AMK-Data-Visualization\Assets'),
    [string]$CompanyEmblemPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [System.Drawing.RectangleF]$Rectangle,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Rectangle.X, $Rectangle.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rectangle.X, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-AppIconBitmap {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $margin = [Math]::Max(1.0, $Size * 0.03125)
    $backgroundRect = [System.Drawing.RectangleF]::new($margin, $margin, $Size - 2 * $margin, $Size - 2 * $margin)
    $backgroundPath = New-RoundedRectanglePath -Rectangle $backgroundRect -Radius ($Size * 0.203125)
    $backgroundBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $backgroundRect,
        [System.Drawing.ColorTranslator]::FromHtml('#173B63'),
        [System.Drawing.ColorTranslator]::FromHtml('#071A2E'),
        45.0)
    $graphics.FillPath($backgroundBrush, $backgroundPath)

    $axisColor = [System.Drawing.ColorTranslator]::FromHtml('#EAF3FA')
    $axisWidth = [float][Math]::Max(1.15, $Size * 0.046875)
    $axisPen = [System.Drawing.Pen]::new($axisColor, $axisWidth)
    $axisPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $axisPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $axisPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $x0 = [float]($Size * 0.21875)
    $y0 = [float]($Size * 0.777344)
    $x1 = [float]($Size * 0.808594)
    $y1 = [float]($Size * 0.203125)
    $arrow = [float]($Size * 0.052734)
    $graphics.DrawLine($axisPen, $x0, $y0, $x0, $y1)
    $graphics.DrawLine($axisPen, $x0, $y1, $x0 - $arrow, $y1 + $arrow * 1.35)
    $graphics.DrawLine($axisPen, $x0, $y1, $x0 + $arrow, $y1 + $arrow * 1.35)
    $graphics.DrawLine($axisPen, $x0, $y0, $x1, $y0)
    $graphics.DrawLine($axisPen, $x1, $y0, $x1 - $arrow * 1.35, $y0 - $arrow)
    $graphics.DrawLine($axisPen, $x1, $y0, $x1 - $arrow * 1.35, $y0 + $arrow)

    $tealPen = [System.Drawing.Pen]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#35D0BA'),
        [float][Math]::Max(1.15, $Size * 0.048828))
    $tealPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $tealPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $tealPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $tealPoints = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($Size * 0.21875, $Size * 0.652344),
        [System.Drawing.PointF]::new($Size * 0.308594, $Size * 0.613281),
        [System.Drawing.PointF]::new($Size * 0.339844, $Size * 0.441406),
        [System.Drawing.PointF]::new($Size * 0.433594, $Size * 0.488281),
        [System.Drawing.PointF]::new($Size * 0.539063, $Size * 0.683594),
        [System.Drawing.PointF]::new($Size * 0.636719, $Size * 0.585938),
        [System.Drawing.PointF]::new($Size * 0.726563, $Size * 0.339844),
        [System.Drawing.PointF]::new($Size * 0.808594, $Size * 0.3125)
    )
    $graphics.DrawCurve($tealPen, $tealPoints, 0.55)

    $amberPen = [System.Drawing.Pen]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#F5B841'),
        [float][Math]::Max(1.0, $Size * 0.039063))
    $amberPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $amberPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $amberPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $amberPoints = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($Size * 0.21875, $Size * 0.550781),
        [System.Drawing.PointF]::new($Size * 0.304688, $Size * 0.527344),
        [System.Drawing.PointF]::new($Size * 0.367188, $Size * 0.636719),
        [System.Drawing.PointF]::new($Size * 0.433594, $Size * 0.589844),
        [System.Drawing.PointF]::new($Size * 0.542969, $Size * 0.367188),
        [System.Drawing.PointF]::new($Size * 0.628906, $Size * 0.425781),
        [System.Drawing.PointF]::new($Size * 0.730469, $Size * 0.621094),
        [System.Drawing.PointF]::new($Size * 0.808594, $Size * 0.503906)
    )
    $graphics.DrawCurve($amberPen, $amberPoints, 0.5)

    Add-CompanyEmblemBadge -Graphics $graphics -Size $Size

    $amberPen.Dispose()
    $tealPen.Dispose()
    $axisPen.Dispose()
    $backgroundBrush.Dispose()
    $backgroundPath.Dispose()
    $graphics.Dispose()
    return $bitmap
}

function Add-CompanyEmblemBadge {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int]$Size
    )

    $badgeScale = if ($Size -le 24) {
        0.42
    }
    elseif ($Size -le 48) {
        0.36
    }
    else {
        0.30
    }

    $diameter = [float][Math]::Max(7.0, $Size * $badgeScale)
    $badgeRight = [float]($Size * 0.953125)
    $badgeTop = [float]($Size * 0.046875)
    $badgeRect = [System.Drawing.RectangleF]::new(
        $badgeRight - $diameter,
        $badgeTop,
        $diameter,
        $diameter)

    $shadowOffset = [float][Math]::Max(0.5, $Size * 0.009)
    $shadowRect = [System.Drawing.RectangleF]::new(
        $badgeRect.X + $shadowOffset,
        $badgeRect.Y + $shadowOffset,
        $badgeRect.Width,
        $badgeRect.Height)
    $shadowBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.Color]::FromArgb(80, 0, 8, 18))
    $badgeBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.Color]::FromArgb(250, 255, 255, 255))
    $badgePen = [System.Drawing.Pen]::new(
        [System.Drawing.Color]::FromArgb(230, 98, 205, 245),
        [float][Math]::Max(0.65, $Size * 0.008))

    $Graphics.FillEllipse($shadowBrush, $shadowRect)
    $Graphics.FillEllipse($badgeBrush, $badgeRect)
    $Graphics.DrawEllipse($badgePen, $badgeRect)

    $innerSize = [float]($diameter * 0.74)
    $sourceRatio = $script:CompanyEmblemBitmap.Width / $script:CompanyEmblemBitmap.Height
    $emblemWidth = $innerSize
    $emblemHeight = [float]($emblemWidth / $sourceRatio)
    if ($emblemHeight -gt $innerSize) {
        $emblemHeight = $innerSize
        $emblemWidth = [float]($emblemHeight * $sourceRatio)
    }

    $emblemRect = [System.Drawing.RectangleF]::new(
        $badgeRect.X + ($badgeRect.Width - $emblemWidth) / 2,
        $badgeRect.Y + ($badgeRect.Height - $emblemHeight) / 2,
        $emblemWidth,
        $emblemHeight)
    $Graphics.DrawImage($script:CompanyEmblemBitmap, $emblemRect)

    $badgePen.Dispose()
    $badgeBrush.Dispose()
    $shadowBrush.Dispose()
}

function Save-AppIconCanvas {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height,
        [double]$IconScale = 0.82
    )

    $canvas = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $iconSize = [int][Math]::Max(16, [Math]::Min($Width, $Height) * $IconScale)
    $icon = New-AppIconBitmap -Size $iconSize
    $left = [int](($Width - $iconSize) / 2)
    $top = [int](($Height - $iconSize) / 2)
    $graphics.DrawImage($icon, $left, $top, $iconSize, $iconSize)

    $canvas.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $icon.Dispose()
    $graphics.Dispose()
    $canvas.Dispose()
}

function Save-MultiSizeIcon {
    param(
        [string]$Path,
        [int[]]$Sizes
    )

    $frames = foreach ($size in $Sizes) {
        $bitmap = New-AppIconBitmap -Size $size
        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        ,$stream.ToArray()
        $stream.Dispose()
    }

    $fileStream = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($fileStream)
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)

    $offset = 6 + 16 * $frames.Count
    for ($index = 0; $index -lt $frames.Count; $index++) {
        $size = $Sizes[$index]
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }

    foreach ($frame in $frames) {
        $writer.Write($frame)
    }

    $writer.Dispose()
    $fileStream.Dispose()
}

$assetsPath = [System.IO.Path]::GetFullPath($AssetsDirectory)
[System.IO.Directory]::CreateDirectory($assetsPath) | Out-Null

if ([string]::IsNullOrWhiteSpace($CompanyEmblemPath)) {
    $CompanyEmblemPath = Join-Path $assetsPath 'CompanyEmblem.ico'
}

$resolvedCompanyEmblemPath = [System.IO.Path]::GetFullPath($CompanyEmblemPath)
if (-not (Test-Path -LiteralPath $resolvedCompanyEmblemPath)) {
    throw "Company emblem was not found: $resolvedCompanyEmblemPath"
}

$companyEmblemIcon = [System.Drawing.Icon]::new($resolvedCompanyEmblemPath)
$script:CompanyEmblemBitmap = $companyEmblemIcon.ToBitmap()
$script:CompanyEmblemBitmap.Save(
    (Join-Path $assetsPath 'CompanyEmblem.png'),
    [System.Drawing.Imaging.ImageFormat]::Png)

try {
    Save-MultiSizeIcon -Path (Join-Path $assetsPath 'DataVisualization.ico') -Sizes @(16, 24, 32, 48, 64, 128, 256)
    Save-AppIconCanvas -Path (Join-Path $assetsPath 'DataVisualization.png') -Width 512 -Height 512 -IconScale 1.0
    Save-AppIconCanvas -Path (Join-Path $assetsPath 'LockScreenLogo.scale-200.png') -Width 48 -Height 48 -IconScale 1.0
    Save-AppIconCanvas -Path (Join-Path $assetsPath 'SplashScreen.scale-200.png') -Width 1240 -Height 600 -IconScale 0.62
    Save-AppIconCanvas -Path (Join-Path $assetsPath 'Square150x150Logo.scale-200.png') -Width 300 -Height 300 -IconScale 1.0
    Save-AppIconCanvas -Path (Join-Path $assetsPath 'Square44x44Logo.scale-200.png') -Width 88 -Height 88 -IconScale 1.0
    Save-AppIconCanvas -Path (Join-Path $assetsPath 'Square44x44Logo.targetsize-24_altform-unplated.png') -Width 24 -Height 24 -IconScale 1.0
    Save-AppIconCanvas -Path (Join-Path $assetsPath 'StoreLogo.png') -Width 50 -Height 50 -IconScale 1.0
    Save-AppIconCanvas -Path (Join-Path $assetsPath 'Wide310x150Logo.scale-200.png') -Width 620 -Height 300 -IconScale 0.82
}
finally {
    $script:CompanyEmblemBitmap.Dispose()
    $companyEmblemIcon.Dispose()
}

Write-Output "Generated application icon assets in $assetsPath"
