<#
.SYNOPSIS
  Downloads the official Windows embeddable Python into python/runtime/ (git-ignored)
  and installs Pillow into it, so the VOX export works without a system Python.

.DESCRIPTION
  ksh2vox needs Python 3.10+ (PEP 604 annotations such as `str | None`).
  3.12.10 is the default: the last 3.12 release that python.org ships as binaries,
  long-tested with Pillow wheels. Override with -Version (e.g. 3.13.x).

  Steps:
    1. download python-<ver>-embed-amd64.zip from python.org, verify it is a zip
    2. unpack into python/runtime/
    3. enable site-packages: uncomment `import site` in python3XX._pth
       (the embeddable distribution ignores site-packages and PYTHONPATH otherwise)
    4. bootstrap pip via get-pip.py and install Pillow (jacket resizing)

  ffmpeg is NOT bundled — it must be on PATH (same requirement as the converter).

.EXAMPLE
  pwsh tools/fetch_python.ps1
  pwsh tools/fetch_python.ps1 -Version 3.13.5 -Force
#>
[CmdletBinding()]
param(
    [string]$Version = "3.12.10",
    [string]$Arch = "amd64",
    [switch]$Force,
    [switch]$NoPillow
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest is very slow with the progress bar
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = Split-Path -Parent $PSScriptRoot
$runtime = Join-Path $repo "python\runtime"
$python = Join-Path $runtime "python.exe"

if ((Test-Path $python) -and -not $Force) {
    Write-Host "python/runtime already present: $(& $python --version 2>&1). Use -Force to replace it."
    exit 0
}

if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw "Version must look like 3.12.10" }
$major = [int]$Matches[1]; $minor = [int]$Matches[2]
if ($major -ne 3 -or $minor -lt 10) { throw "ksh2vox needs Python 3.10 or newer" }

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("sdvxeditor-python-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    $zipName = "python-$Version-embed-$Arch.zip"
    $url = "https://www.python.org/ftp/python/$Version/$zipName"
    $zip = Join-Path $tmp $zipName
    Write-Host "downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

    $fs = [IO.File]::OpenRead($zip)
    try { $b0 = $fs.ReadByte(); $b1 = $fs.ReadByte() } finally { $fs.Dispose() }
    if ($b0 -ne 0x50 -or $b1 -ne 0x4B) { throw "download is not a zip archive: $url" }

    if (Test-Path $runtime) { Remove-Item -Recurse -Force $runtime -Confirm:$false }
    New-Item -ItemType Directory -Force $runtime | Out-Null
    Expand-Archive -Path $zip -DestinationPath $runtime

    # Enable site-packages: the ._pth file decides sys.path for the embeddable build.
    $pth = Get-ChildItem $runtime -Filter "python*._pth" | Select-Object -First 1
    if ($null -eq $pth) { throw "no python*._pth in the embeddable archive" }
    $lines = Get-Content $pth.FullName
    $lines = $lines | ForEach-Object { if ($_ -match '^\s*#\s*import site\s*$') { "import site" } else { $_ } }
    if (-not ($lines -contains "import site")) { $lines += "import site" }
    if (-not ($lines -contains "Lib\site-packages")) { $lines = @("Lib\site-packages") + $lines }
    Set-Content -Path $pth.FullName -Value $lines -Encoding ascii
    New-Item -ItemType Directory -Force (Join-Path $runtime "Lib\site-packages") | Out-Null

    if (-not $NoPillow) {
        $getPip = Join-Path $tmp "get-pip.py"
        Write-Host "downloading get-pip.py"
        Invoke-WebRequest -Uri "https://bootstrap.pypa.io/get-pip.py" -OutFile $getPip -UseBasicParsing
        & $python $getPip --no-warn-script-location --disable-pip-version-check
        if ($LASTEXITCODE -ne 0) { throw "get-pip.py failed ($LASTEXITCODE)" }
        & $python -m pip install --no-warn-script-location --disable-pip-version-check --only-binary=:all: Pillow
        if ($LASTEXITCODE -ne 0) { throw "pip install Pillow failed ($LASTEXITCODE)" }
    }

    if ($NoPillow) { $check = & $python --version 2>&1 }
    else { $check = & $python -c "import sys; import PIL; print(sys.version.split()[0], 'Pillow', PIL.__version__)" 2>&1 }
    Write-Host "ok: python/runtime -> $check"
}
finally {
    Remove-Item -Recurse -Force $tmp -Confirm:$false -ErrorAction SilentlyContinue
}
