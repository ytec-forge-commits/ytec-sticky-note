$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourcePath = Join-Path $projectRoot 'src\YtecStickyNote\Assets\app-icon.png'
$assetRoot = Join-Path $projectRoot 'packaging\msix\Assets'
New-Item -ItemType Directory -Force -Path $assetRoot | Out-Null

$source = [System.Drawing.Image]::FromFile($sourcePath)
try {
    $assets = [ordered]@{
        'StoreLogo.png' = 50
        'Square44x44Logo.png' = 44
        'Square71x71Logo.png' = 71
        'Square150x150Logo.png' = 150
    }
    foreach ($asset in $assets.GetEnumerator()) {
        $size = [int]$asset.Value
        $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($source, 0, 0, $size, $size)
            }
            finally {
                $graphics.Dispose()
            }
            $bitmap.Save((Join-Path $assetRoot $asset.Key), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

Write-Output "MSIX assets: $assetRoot"
