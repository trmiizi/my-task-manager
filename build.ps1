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

# ---- icon: yellow sticky note with a tick, PNG frames packed into an .ico (delete src\app.ico to redraw)
$ico = Join-Path $root 'src\app.ico'
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    $sizes = 16, 20, 24, 32, 40, 48, 64, 256
    $pngs = foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $g.Clear([System.Drawing.Color]::Transparent)
        # sticky note: yellow note, folded bottom-right corner, tape on top, dark tick (drawn on a 64-unit grid)
        $k = $s / 64.0
        $g.ScaleTransform($k, $k)
        $note = New-Object System.Drawing.Drawing2D.GraphicsPath
        $note.AddArc(7, 7, 8, 8, 180, 90); $note.AddLine(11, 7, 53, 7); $note.AddArc(49, 7, 8, 8, 270, 90)
        $note.AddLine(57, 11, 57, 43); $note.AddLine(57, 43, 43, 57); $note.AddLine(43, 57, 11, 57)
        $note.AddArc(7, 49, 8, 8, 90, 90); $note.CloseFigure()
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 212, 59))), $note)
        $fold = New-Object System.Drawing.Drawing2D.GraphicsPath
        $fold.AddLine(57, 43, 47, 43); $fold.AddArc(43, 43, 8, 8, 270, -90); $fold.AddLine(43, 47, 43, 57); $fold.CloseFigure()
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 232, 168, 0))), $fold)
        if ($s -ge 24) {
            $st = $g.Save()
            $g.TranslateTransform(32, 7.5); $g.RotateTransform(-4)
            $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 255, 255, 255))), -10, -4.5, 20, 9)
            $g.Restore($st)
        }
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 92, 61, 0)), ([Math]::Max(6.0, 1.7 / $k))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF 19, 32),
            (New-Object System.Drawing.PointF 28, 41),
            (New-Object System.Drawing.PointF 45, 23))
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
