# Run this once as Administrator to install Python and build the exe
# Right-click PowerShell -> Run as Administrator, then:
#   Set-ExecutionPolicy RemoteSigned -Scope CurrentUser
#   cd path\to\EmusatUI
#   .\setup.ps1

$ErrorActionPreference = "Stop"

# ── 1. Check / install Python ─────────────────────────────────────────────────
$python = Get-Command python -ErrorAction SilentlyContinue

if (-not $python) {
    Write-Host "Python not found. Installing via winget..." -ForegroundColor Yellow

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if ($winget) {
        winget install --id Python.Python.3.12 --source winget --accept-package-agreements --accept-source-agreements -e
    } else {
        Write-Host "winget not found. Downloading Python installer..." -ForegroundColor Yellow
        $installer = "$env:TEMP\python_installer.exe"
        Invoke-WebRequest -Uri "https://www.python.org/ftp/python/3.12.9/python-3.12.9-amd64.exe" -OutFile $installer
        Write-Host "Running Python installer (silent)..." -ForegroundColor Yellow
        Start-Process -FilePath $installer -ArgumentList "/quiet InstallAllUsers=1 PrependPath=1 Include_test=0" -Wait
        Remove-Item $installer
    }

    # Refresh PATH so python is available in this session
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("Path", "User")

    $python = Get-Command python -ErrorAction SilentlyContinue
    if (-not $python) {
        Write-Error "Python installation failed or PATH not updated. Please restart PowerShell and re-run this script."
        exit 1
    }
    Write-Host "Python installed: $(python --version)" -ForegroundColor Green
} else {
    Write-Host "Python found: $(python --version)" -ForegroundColor Green
}

# ── 2. Install dependencies ───────────────────────────────────────────────────
Write-Host "`nInstalling Python packages..." -ForegroundColor Cyan
python -m pip install --upgrade pip
python -m pip install -r requirements.txt

# ── 3. Build exe ──────────────────────────────────────────────────────────────
Write-Host "`nBuilding EmusatMonitor.exe..." -ForegroundColor Cyan
python -m PyInstaller --onefile --windowed --name EmusatMonitor main.py

Write-Host "`nDone! Executable is at: dist\EmusatMonitor.exe" -ForegroundColor Green
Write-Host "You can now run dist\EmusatMonitor.exe directly - no Python needed." -ForegroundColor Green
