Add-Type -AssemblyName System.Drawing

$root = "S:\SteamAchievementManager"
$icons = Join-Path $root "SAM.Game\Icons"

function Get-PngBytesFromBitmap([System.Drawing.Bitmap]$bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return ,$bytes
}

function Resize-ToPngBytes([System.Drawing.Bitmap]$source, [int]$size) {
    $dest = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($dest)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.DrawImage($source, 0, 0, $size, $size)
    }
    finally {
        $graphics.Dispose()
    }
    $bytes = Get-PngBytesFromBitmap $dest
    $dest.Dispose()
    return ,$bytes
}

$entries = New-Object System.Collections.Generic.List[object]

function Add-PngFile([int]$size, [string]$name) {
    $path = Join-Path $icons $name
    if (-not (Test-Path $path)) { throw "Missing $path" }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    Write-Host "Add $size from $name ($($bytes.Length) bytes)"
    $entries.Add([pscustomobject]@{ Size = $size; Bytes = $bytes }) | Out-Null
}

Add-PngFile 16 "KF_GI_Mini.png"
Add-PngFile 24 "KF_GI_Small.png"
Add-PngFile 32 "KF_GI_Medium.png"
Add-PngFile 64 "KF_GI_Large.png"
Add-PngFile 96 "KF_GI_XL.png"

$xlPath = Join-Path $icons "KF_GI_XL.png"
$xlImage = [System.Drawing.Image]::FromFile($xlPath)
$xlBitmap = New-Object System.Drawing.Bitmap $xlImage
$xlImage.Dispose()
$entries.Add([pscustomobject]@{ Size = 48; Bytes = (Resize-ToPngBytes $xlBitmap 48) }) | Out-Null
$entries.Add([pscustomobject]@{ Size = 256; Bytes = (Resize-ToPngBytes $xlBitmap 256) }) | Out-Null
Write-Host "Add 48 and 256 scaled from XL"
$xlBitmap.Dispose()

$sorted = $entries | Sort-Object Size
$count = @($sorted).Count
$headerSize = 6 + (16 * $count)

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$count)

$offset = [uint32]$headerSize
foreach ($entry in $sorted) {
    $size = [int]$entry.Size
    $data = [byte[]]$entry.Bytes
    $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$data.Length)
    $writer.Write([uint32]$offset)
    $offset += [uint32]$data.Length
}

foreach ($entry in $sorted) {
    $data = [byte[]]$entry.Bytes
    $writer.Write($data)
}

$writer.Flush()
$bytes = $stream.ToArray()
$writer.Dispose()
$stream.Dispose()

foreach ($target in @(
    (Join-Path $root "SAM.Game\kf.ico"),
    (Join-Path $root "kf.ico")
)) {
    [System.IO.File]::WriteAllBytes($target, $bytes)
    Write-Host "Wrote $target ($($bytes.Length) bytes, $count images)"
}
