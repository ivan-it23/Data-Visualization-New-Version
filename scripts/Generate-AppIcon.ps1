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

function New-PolylinePoints {
    param(
        [int]$Size,
        [double[]]$Coordinates
    )

    $points = for ($index = 0; $index -lt $Coordinates.Length; $index += 2) {
        [System.Drawing.PointF]::new(
            [float]($Size * $Coordinates[$index]),
            [float]($Size * $Coordinates[$index + 1]))
    }

    return [System.Drawing.PointF[]]$points
}

function New-AppIconBitmap {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $cornerRadius = [float]($Size * 0.203125)
    $margin = [Math]::Max(1.0, $Size * 0.03125)
    $backgroundRect = [System.Drawing.RectangleF]::new($margin, $margin, $Size - 2 * $margin, $Size - 2 * $margin)
    $backgroundPath = New-RoundedRectanglePath -Rectangle $backgroundRect -Radius $cornerRadius
    $backgroundBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#E9F0F7'))
    $graphics.FillPath($backgroundBrush, $backgroundPath)

    # The light plate needs an outline of its own, otherwise it dissolves into
    # Explorer, dialog and light-theme taskbar backgrounds.
    $borderWidth = [float][Math]::Max(1.0, $Size * 0.01171875)
    $borderRect = [System.Drawing.RectangleF]::new(
        $backgroundRect.X + $borderWidth / 2,
        $backgroundRect.Y + $borderWidth / 2,
        $backgroundRect.Width - $borderWidth,
        $backgroundRect.Height - $borderWidth)
    $borderPath = New-RoundedRectanglePath `
        -Rectangle $borderRect `
        -Radius ([float][Math]::Max(1.0, $cornerRadius - $borderWidth / 2))
    $borderPen = [System.Drawing.Pen]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#A9BDD1'),
        $borderWidth)
    $graphics.DrawPath($borderPen, $borderPath)

    $plotLeft = [float]($Size * 0.1796875)
    $plotRight = [float]($Size * 0.828125)
    $plotTop = [float]($Size * 0.21875)
    $plotBottom = [float]($Size * 0.8203125)

    # Below 32 px the grid collapses into a grey wash and hides the curves.
    if ($Size -ge 32) {
        $gridPen = [System.Drawing.Pen]::new(
            [System.Drawing.ColorTranslator]::FromHtml('#D3DEE9'),
            [float][Math]::Max(0.75, $Size * 0.0078125))
        foreach ($fraction in @(0.3046875, 0.4375, 0.5703125, 0.703125)) {
            $x = [float]($Size * $fraction)
            $graphics.DrawLine($gridPen, $x, $plotTop, $x, $plotBottom)
        }

        foreach ($fraction in @(0.3515625, 0.484375, 0.6171875, 0.75)) {
            $y = [float]($Size * $fraction)
            $graphics.DrawLine($gridPen, $plotLeft, $y, $plotRight, $y)
        }

        $gridPen.Dispose()
    }

    $axisPen = [System.Drawing.Pen]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#5C7E9E'),
        [float][Math]::Max(1.0, $Size * 0.01953125))
    $axisPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Square
    $axisPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Square
    $graphics.DrawLine($axisPen, $plotLeft, $plotTop, $plotLeft, $plotBottom)
    $graphics.DrawLine($axisPen, $plotLeft, $plotBottom, $plotRight, $plotBottom)

    $steelPen = [System.Drawing.Pen]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#1B4A73'),
        [float][Math]::Max(1.0, $Size * 0.0234375))
    $steelPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $steelPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $steelPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawLines($steelPen, (New-PolylinePoints -Size $Size -Coordinates @(
        0.1796875, 0.765625,
        0.3046875, 0.734375,
        0.4375, 0.6484375,
        0.5703125, 0.6875,
        0.703125, 0.5859375,
        0.828125, 0.6171875
    )))

    $accentPen = [System.Drawing.Pen]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#0087C4'),
        [float][Math]::Max(1.25, $Size * 0.03515625))
    $accentPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $accentPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawLines($accentPen, (New-PolylinePoints -Size $Size -Coordinates @(
        0.1796875, 0.703125,
        0.3046875, 0.640625,
        0.4375, 0.7265625,
        0.5703125, 0.5078125,
        0.703125, 0.5703125,
        0.828125, 0.453125
    )))

    Add-CompanyEmblemBadge -Graphics $graphics -Size $Size

    $accentPen.Dispose()
    $steelPen.Dispose()
    $axisPen.Dispose()
    $borderPen.Dispose()
    $borderPath.Dispose()
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

    # The badge is still enlarged for small frames so the emblem stays visible,
    # but less aggressively than on the previous icon: at 16-24 px an oversized
    # badge covered the plot area and the emblem itself turned into a blob.
    $badgeScale = if ($Size -le 24) {
        0.38
    }
    elseif ($Size -le 48) {
        0.33
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

    # On the light plate a drop shadow reads as dirt, so the badge is flat and
    # is separated by the same steel outline as the plate itself.
    $badgeBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $badgePen = [System.Drawing.Pen]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#A9BDD1'),
        [float][Math]::Max(0.65, $Size * 0.009765625))

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
