<#
.SYNOPSIS
    Installs the IIS CPU Root-Cause Monitoring Agent Windows Service from a published ZIP file or folder.
.DESCRIPTION
    Performs unattended installation:
    1. Verifies Administrator elevation.
    2. Detects the application ZIP file in the script's folder (or specified path).
    3. Stops any existing service instance.
    4. Extracts files directly to the installation directory (e.g. C:\Program Files\IISMonitor).
    5. Creates the data directory (C:\ProgramData\IISMonitor and Dumps).
    6. Registers the Windows Service with automatic startup.
    7. Configures service auto-restart on failure.
    8. Starts the service and displays dashboard access URL.
.PARAMETER ZipPath
    Optional path to the ZIP file. If omitted, the script automatically searches the script's directory for IISMonitor.zip or *.zip.
.PARAMETER InstallDir
    Target directory for binaries (Default: C:\Program Files\IISMonitor).
.PARAMETER DataDir
    Target directory for SQLite database and memory dumps (Default: C:\ProgramData\IISMonitor).
.PARAMETER ServiceName
    Name of the Windows Service (Default: IISMonitorAgent).
.EXAMPLE
    .\Install-IISMonitor.ps1
    Extracts IISMonitor.zip from the current folder and installs the service.
.EXAMPLE
    .\Install-IISMonitor.ps1 -ZipPath "D:\Downloads\IISMonitor.zip"
#>

[CmdletBinding()]
param(
    [string]$ZipPath,
    [string]$InstallDir = "C:\Program Files\IISMonitor",
    [string]$DataDir = "C:\ProgramData\IISMonitor",
    [string]$ServiceName = "IISMonitorAgent"
)

$ErrorActionPreference = "Stop"

# 1. Verify administrative privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "This script must be executed in an elevated PowerShell session (Run as Administrator)."
    exit 1
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "   IIS Root-Cause Monitoring Agent - Installation Script    " -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 2. Locate the ZIP package
$scriptFolder = if ($PSScriptRoot) { $PSScriptRoot } else { Get-Location }

$targetZip = $null
if (-not [string]::IsNullOrWhiteSpace($ZipPath) -and (Test-Path $ZipPath)) {
    $targetZip = (Resolve-Path $ZipPath).Path
} else {
    # Check for IISMonitor.zip in current script directory
    $candidate = Join-Path $scriptFolder "IISMonitor.zip"
    if (Test-Path $candidate) {
        $targetZip = $candidate
    } else {
        # Check for any *IISMonitor*.zip or *.zip in the script folder
        $foundZip = Get-ChildItem -Path $scriptFolder -Filter "*IISMonitor*.zip" | Select-Object -First 1
        if (-not $foundZip) {
            $foundZip = Get-ChildItem -Path $scriptFolder -Filter "*.zip" | Select-Object -First 1
        }
        if ($foundZip) {
            $targetZip = $foundZip.FullName
        }
    }
}

# 3. Stop existing service before extracting/overwriting binaries
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "`nStopping existing service '$ServiceName'..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    Write-Host "Removing existing service registration..." -ForegroundColor Yellow
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# 4. Create target directories
Write-Host "`nCreating target directories:" -ForegroundColor Green
Write-Host "  Installation: $InstallDir" -ForegroundColor Gray
Write-Host "  Data:         $DataDir" -ForegroundColor Gray
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $DataDir "Dumps") -Force | Out-Null

# 5. Extract or Copy Application Binaries
if ($targetZip) {
    Write-Host "`nFound deployment package: $targetZip" -ForegroundColor Green
    Write-Host "Extracting to $InstallDir..." -ForegroundColor Green
    Expand-Archive -Path $targetZip -DestinationPath $InstallDir -Force
    Write-Host "Extraction completed." -ForegroundColor Green
} else {
    # Fallback: Check if binaries exist in script folder or adjacent publish directory
    $directExe = Join-Path $scriptFolder "IISMonitor.Service.exe"
    $publishFolder = Join-Path $scriptFolder "publish"
    $singleFileFolder = Join-Path $scriptFolder "publish-singlefile"

    if (Test-Path $directExe) {
        Write-Host "`nCopying binaries from script directory to $InstallDir..." -ForegroundColor Green
        Copy-Item -Path "$scriptFolder\*" -Destination $InstallDir -Recurse -Force -Exclude "*.zip", "*.ps1"
    } elseif (Test-Path $singleFileFolder) {
        Write-Host "`nCopying binaries from $singleFileFolder to $InstallDir..." -ForegroundColor Green
        Copy-Item -Path "$singleFileFolder\*" -Destination $InstallDir -Recurse -Force
    } elseif (Test-Path $publishFolder) {
        Write-Host "`nCopying binaries from $publishFolder to $InstallDir..." -ForegroundColor Green
        Copy-Item -Path "$publishFolder\*" -Destination $InstallDir -Recurse -Force
    } else {
        Write-Error "Could not find 'IISMonitor.zip' or published binaries in '$scriptFolder'. Please place 'IISMonitor.zip' in the same folder as this script."
        exit 1
    }
}

# 6. Verify executable exists
$exePath = Join-Path $InstallDir "IISMonitor.Service.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found at '$exePath'. The package may be corrupted or missing."
    exit 1
}

# 7. Register Windows Service
Write-Host "`nRegistering Windows Service '$ServiceName'..." -ForegroundColor Green
$binPathArg = "`"$exePath`""
sc.exe create $ServiceName binPath= $binPathArg start= auto DisplayName= "IIS Root-Cause Monitoring Agent" | Out-Null

# 8. Configure Service Auto-Recovery on Failure
Write-Host "Configuring service recovery on failure..." -ForegroundColor Green
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/none/60000 | Out-Null

# 9. Start Windows Service
Write-Host "Starting '$ServiceName' service..." -ForegroundColor Green
Start-Service -Name $ServiceName
Start-Sleep -Seconds 3

# 10. Verify Service Status
$status = Get-Service -Name $ServiceName
if ($status.Status -eq 'Running') {
    Write-Host "Service '$ServiceName' is RUNNING!" -ForegroundColor Green
} else {
    Write-Warning "Service '$ServiceName' status: $($status.Status). Check Windows Event Log for details."
}

Write-Host @"
============================================================
IIS CPU Root-Cause Monitoring Agent successfully installed!
Management Web Dashboard: http://localhost:5050
Local Database:          $DataDir\monitor.db
Dump Directory:          $DataDir\Dumps
============================================================
"@ -ForegroundColor Cyan
