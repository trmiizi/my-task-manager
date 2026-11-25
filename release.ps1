param(
    [Parameter(Mandatory = $true)][string]$Version,   # e.g. 2.2.0
    [string]$Notes = ''                                # shown under Settings > Updates as "What's new"
)
# Publishes a new version: sets the version, builds, runs the self-test, commits, tags, pushes,
# and creates a GitHub Release with MyTaskManager.exe attached (found via Settings > Check for updates).
#   powershell -ExecutionPolicy Bypass -File release.ps1 -Version 2.2.0 -Notes "Archive button, dark menus"
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 2.2.0" }
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = "$env:ProgramFiles\GitHub CLI\gh.exe" }
if (-not (Test-Path $gh)) { throw "GitHub CLI not found. Install it: winget install GitHub.cli" }

$tag = "v$Version"
git fetch --tags --quiet 2>$null
if (git tag --list $tag) { throw "$tag already exists. Pick a higher version." }

# 1. version
$vfile = Join-Path $root 'src\Version.cs'
@"
using System.Reflection;

// Set by release.ps1. The app compares this with the latest GitHub release to offer updates.
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
[assembly: AssemblyInformationalVersion("$Version")]
"@ | Set-Content -Path $vfile -Encoding UTF8

# 2. build (close the running app first, its .exe is locked)
Get-Process MyTaskManager -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $root 'dist\MyTaskManager.exe') } | Stop-Process -Force
Start-Sleep -Milliseconds 500
& powershell -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# 3. self-test must pass
$report = Join-Path $env:TEMP "mtm-release-selftest.txt"
$p = Start-Process (Join-Path $root 'dist\MyTaskManager.exe') -ArgumentList '--selftest', "`"$report`"" -Wait -PassThru
if ($p.ExitCode -ne 0) { Get-Content $report | Select-String 'FAIL'; throw "Self-test failed, nothing was published." }
Write-Host "Self-test passed."

# 4. commit, tag, push
git add -A
git commit -m "Release $tag" -m $Notes --allow-empty | Out-Null
git tag -a $tag -m "Release $tag"
git push origin HEAD --follow-tags

# 5. GitHub release with the .exe (the app downloads this file)
if (-not $Notes) { $Notes = "My Task Manager $Version" }
& $gh release create $tag (Join-Path $root 'dist\MyTaskManager.exe') (Join-Path $root 'dist\README.txt') --title "My Task Manager $Version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "Creating the GitHub release failed." }
Write-Host ""
Write-Host "Published $tag. Installed copies get it from Settings > Check for updates."
