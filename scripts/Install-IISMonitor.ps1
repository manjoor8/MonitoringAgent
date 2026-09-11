<#
.SYNOPSIS
    Installs the IIS CPU Root-Cause Monitoring Agent Windows Service.
.DESCRIPTION
    Performs unattended installation: directory creation, binary deployment,
    Windows Service registration, failure auto-recovery configuration, and service startup.
.PARAMETER InstallDir
    Target directory for binaries (Default: C:\Program Files\IISMonitor)
.PARAMETER DataDir
    Target directory for SQLite database and memory dumps (Default: C:\ProgramData\IISMonitor)
.PARAMETER ServiceName
    Name of the Windows Service (Default: IISMonitorAgent)
#>

[CmdletBinding()]
param(
    [string]$InstallDir = "C:\Program Files\IISMonitor",
    [string]$DataDir = "C:\ProgramData\IISMonitor",
    [string]$ServiceName = "IISMonitorAgent"
)

# 1. Verify administrative privileges
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "This script must be executed in an elevated PowerShell session (Run as Administrator)."
    exit 1
}

Write-Host "=== Installing IIS Root-Cause Monitoring Agent ===" -ForegroundColor Cyan

# 2. Stop and remove existing service if present
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Stopping existing service '$ServiceName'..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    Write-Host "Removing existing service registration..." -ForegroundColor Yellow
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
}

# 3. Create installation and data directories
Write-Host "Creating target directories..." -ForegroundColor Green
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $DataDir "Dumps") -Force | Out-Null

# 4. Copy published binaries if running alongside a publish folder
$sourceDir = Join-Path $PSScriptRoot "..\publish"
if (Test-Path $sourceDir) {
    Write-Host "Deploying binaries from $sourceDir to $InstallDir..." -ForegroundColor Green
    Copy-Item -Path "$sourceDir\*" -Destination $InstallDir -Recurse -Force
} else {
    Write-Host "Publish directory not found at $sourceDir. Assuming binaries already placed in $InstallDir." -ForegroundColor Yellow
}

$exePath = Join-Path $InstallDir "IISMonitor.Service.exe"
if (-not (Test-Path $exePath)) {
    Write-Warning "Executable not found at $exePath. Ensure the project is published first: dotnet publish -c Release -o publish"
}

# 5. Create Windows Service
Write-Host "Registering Windows Service '$ServiceName'..." -ForegroundColor Green
$binPathArg = "`"$exePath`""
sc.exe create $ServiceName binPath= $binPathArg start= auto DisplayName= "IIS Root-Cause Monitoring Agent"

# 6. Configure Service Failure Auto-Restart
Write-Host "Configuring service recovery on failure..." -ForegroundColor Green
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/none/60000

# 7. Start the service
if (Test-Path $exePath) {
    Write-Host "Starting '$ServiceName' service..." -ForegroundColor Green
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 3

    $status = Get-Service -Name $ServiceName
    Write-Host "Service Status: $($status.Status)" -ForegroundColor Cyan
}

Write-Host @"
============================================================
IIS CPU Root-Cause Monitoring Agent successfully installed!
Management Web Dashboard: http://localhost:5050
Local Database: $DataDir\monitor.db
Dump Directory: $DataDir\Dumps
============================================================
"@ -ForegroundColor Green
