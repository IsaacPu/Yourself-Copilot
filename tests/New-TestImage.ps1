param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Tests create this neutral image inside their isolated .qa directory.
# No user image or live application data is read.
$bitmap = $null
$canvas = $null
$brush = $null
try {
    $bitmap = New-Object System.Drawing.Bitmap -ArgumentList 640,400
    $canvas = [System.Drawing.Graphics]::FromImage($bitmap)
    $canvas.Clear([System.Drawing.Color]::FromArgb(247,245,239))
    $brush = New-Object System.Drawing.SolidBrush -ArgumentList ([System.Drawing.Color]::FromArgb(104,121,99))
    $canvas.FillRectangle($brush, 48, 48, 544, 48)
    $canvas.FillRectangle($brush, 48, 128, 32, 32)
    $canvas.FillRectangle($brush, 104, 136, 392, 16)
    $canvas.FillRectangle($brush, 48, 192, 32, 32)
    $canvas.FillRectangle($brush, 104, 200, 320, 16)
    $canvas.FillEllipse($brush, 504, 280, 64, 64)
    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    if ($brush) { $brush.Dispose() }
    if ($canvas) { $canvas.Dispose() }
    if ($bitmap) { $bitmap.Dispose() }
}
