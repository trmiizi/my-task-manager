param([string]$OutDir = '')
# Builds MyTaskManager.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# No Visual Studio or SDK needed. Run:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw   = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path "$fw\csc.exe")) { $fw = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319" }
$wpf  = "$fw\WPF"
$out  = if ($OutDir) { $OutDir } else { Join-Path $root 'dist' }
New-Item -ItemType Directory -Force $out | Out-Null

# ---- icon: blue rounded square with a white tick, PNG frames packed into an .ico
$ico = Join-Path $root 'src\app.ico'
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    $sizes = 16, 20, 24, 32, 40, 48, 64, 256
    $pngs = foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $g.Clear([System.Drawing.Color]::Transparent)
        $r = [Math]::Max(3, [int]($s * 0.24)); $d = $r * 2; $w = $s - 1
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
        $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90); $path.CloseFigure()
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 59, 91, 219))), $path)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.6, $s * 0.1))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ($s * 0.27), ($s * 0.52)),
            (New-Object System.Drawing.PointF ($s * 0.43), ($s * 0.68)),
            (New-Object System.Drawing.PointF ($s * 0.74), ($s * 0.34)))
        $g.DrawLines($pen, $pts)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        , $ms.ToArray()
    }
    $fs = [System.IO.File]::Create($ico); $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $bw.Write($p) }
    $bw.Close()
    Write-Host "Icon created."
}

$refs = @(
    "$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll", "$wpf\WindowsBase.dll", "$fw\System.Xaml.dll",
    "$fw\System.dll", "$fw\System.Core.dll", "$fw\System.Xml.dll", "$fw\System.Drawing.dll",
    "$fw\System.Windows.Forms.dll", "$fw\System.Web.Extensions.dll"
) | ForEach-Object { "/r:`"$_`"" }

$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { "`"$($_.FullName)`"" }
$cscArgs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', '/platform:anycpu',
    "/out:`"$out\MyTaskManager.exe`"", "/win32icon:`"$ico`"", "/win32manifest:`"$root\src\app.manifest`"",
    "/resource:`"$ico`",app.ico") + $refs + $sources

& "$fw\csc.exe" @cscArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
Write-Host "Built: $out\MyTaskManager.exe"
