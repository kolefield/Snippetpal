$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src\Snippetpal\Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = 'AntiAlias'
$graphics.Clear([System.Drawing.Color]::FromArgb(28,33,38))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(169,232,202)),5
$pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
$graphics.DrawLines($pen, [System.Drawing.Point[]]@((New-Object System.Drawing.Point 25,17),(New-Object System.Drawing.Point 17,17),(New-Object System.Drawing.Point 17,47),(New-Object System.Drawing.Point 25,47)))
$graphics.DrawLines($pen, [System.Drawing.Point[]]@((New-Object System.Drawing.Point 39,17),(New-Object System.Drawing.Point 47,17),(New-Object System.Drawing.Point 47,47),(New-Object System.Drawing.Point 39,47)))
$graphics.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(169,232,202))),29,29,6,6)
$memory = New-Object System.IO.MemoryStream
$bitmap.Save($memory,[System.Drawing.Imaging.ImageFormat]::Png)
$bytes = $memory.ToArray()
$file = [System.IO.File]::Create((Join-Path $assets 'Snippetpal.ico'))
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]1)
$writer.Write([byte]64); $writer.Write([byte]64); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$bytes.Length); $writer.Write([UInt32]22); $writer.Write($bytes)
$writer.Dispose(); $memory.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $pen.Dispose()
