param(
    [string]$ProjectRoot = "C:\Projects\NTStrategies",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$ntUserDataDir = Join-Path $env:USERPROFILE "Documents\NinjaTrader 8"

$syncTargets = @(
    @{
        Name = "Strategy"
        NtTemplateRoot = Join-Path $ntUserDataDir "templates\Strategy"
        ProjectRoot = Join-Path $ProjectRoot "src\Strategies"
    },
    @{
        Name = "Indicator"
        NtTemplateRoot = Join-Path $ntUserDataDir "templates\Indicator"
        ProjectRoot = Join-Path $ProjectRoot "src\Indicators"
    }
)

function Test-IsJunction {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return $false
    }

    $item = Get-Item $Path -Force
    return (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Ensure-Directory {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        Write-Host "Creating directory: $Path"
        if (-not $DryRun) {
            New-Item -ItemType Directory -Path $Path | Out-Null
        }
    }
}

function Sync-TemplateFolder {
    param(
        [string]$Kind,
        [string]$NtPath,
        [string]$ProjectPath
    )

    Write-Host ""
    Write-Host "[$Kind] $NtPath"
    Write-Host " -> $ProjectPath"

    Ensure-Directory $ProjectPath

    if (-not (Test-Path $NtPath)) {
        Write-Host "NT template folder does not exist. Creating junction."

        $ntParent = Split-Path $NtPath -Parent
        Ensure-Directory $ntParent

        if (-not $DryRun) {
            cmd /c mklink /J "$NtPath" "$ProjectPath" | Out-Null
        }

        return
    }

    if (Test-IsJunction $NtPath) {
        Write-Host "Already a junction. Skipping."
        return
    }

    Write-Host "Copying existing NT templates into project folder..."
    if (-not $DryRun) {
        robocopy "$NtPath" "$ProjectPath" /E | Out-Null
    }

    $backupPath = "$NtPath" + "_backup_" + (Get-Date -Format "yyyyMMdd_HHmmss")

    Write-Host "Renaming original NT folder to backup:"
    Write-Host " $backupPath"

    if (-not $DryRun) {
        Rename-Item "$NtPath" "$backupPath"
        cmd /c mklink /J "$NtPath" "$ProjectPath" | Out-Null
    }
}

foreach ($target in $syncTargets) {
    $kind = $target.Name
    $ntTemplateRoot = $target.NtTemplateRoot
    $projectTypeRoot = $target.ProjectRoot

    Write-Host ""
    Write-Host "=== Syncing $kind templates ==="
    Write-Host "NT root:      $ntTemplateRoot"
    Write-Host "Project root: $projectTypeRoot"

    Ensure-Directory $ntTemplateRoot
    Ensure-Directory $projectTypeRoot

    # Sync existing NT template folders into matching project folders.
    Get-ChildItem $ntTemplateRoot -Directory | Where-Object {
        $_.Name -notlike "*_backup_*"
    } | ForEach-Object {
        $templateName = $_.Name
        $ntPath = $_.FullName
        $projectPath = Join-Path $projectTypeRoot "$templateName\Templates"

        Sync-TemplateFolder -Kind $kind -NtPath $ntPath -ProjectPath $projectPath
    }

    # Also create NT junctions for project Templates folders that do not yet exist in NT.
    Get-ChildItem $projectTypeRoot -Directory | ForEach-Object {
        $scriptFolder = $_.FullName
        $scriptName = $_.Name
        $projectTemplatesPath = Join-Path $scriptFolder "Templates"

        if (-not (Test-Path $projectTemplatesPath)) {
            return
        }

        $ntPath = Join-Path $ntTemplateRoot $scriptName

        if (-not (Test-Path $ntPath)) {
            Sync-TemplateFolder -Kind $kind -NtPath $ntPath -ProjectPath $projectTemplatesPath
        }
    }
}

Write-Host ""
Write-Host "Template sync complete."