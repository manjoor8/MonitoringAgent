<#
.SYNOPSIS
    Uninstalls the IIS CPU Root-Cause Monitoring Agent Windows Service.
.PARAMETER ServiceName
    Name of the service to remove (Default: IISMonitorAgent)
.PARAMETER InstallDir
    Binary installation directory to remove (Default: C:\Program Files\IISMonitor)
.PARAMETER PurgeData
    Switch to also delete the SQLite database and captured dumps in C:\ProgramData\IISMonitor
#>

[CmdletBinding()]
param(
    [string]$ServiceName = "IISMonitorAgent",
    [string]$InstallDir = "C:\Program Files\IISMonitor",
    [string]$DataDir = "C:\ProgramData\IISMonitor",
    [switch]$PurgeData
)

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "This script must be executed in an elevated PowerShell session (Run as Administrator)."
    exit 1
}

Write-Host "=== Uninstalling IIS Root-Cause Monitoring Agent ===" -ForegroundColor Yellow

# 1. Stop service
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -eq 'Running') {
        Write-Host "Stopping service '$ServiceName'..." -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }

    # 2. Delete service
    Write-Host "Deleting service '$ServiceName'..." -ForegroundColor Yellow
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
} else {
    Write-Host "Service '$ServiceName' not found." -ForegroundColor Gray
}

# 3. Remove installation directory
if (Test-Path $InstallDir) {
    Write-Host "Removing installation directory: $InstallDir" -ForegroundColor Yellow
    Remove-Item -Path $InstallDir -Recurse -Force
}

# 4. Optionally purge data directory
if ($PurgeData) {
    if (Test-Path $DataDir) {
        Write-Host "Purging data directory and dumps: $DataDir" -ForegroundColor Red
        Remove-Item -Path $DataDir -Recurse -Force
    }
} else {
    Write-Host "Preserved data directory: $DataDir (Run with -PurgeData to remove)." -ForegroundColor Cyan
}

Write-Host "Uninstallation complete." -ForegroundColor Green
