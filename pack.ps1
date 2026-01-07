#!/usr/bin/env pwsh
<#
.SYNOPSIS
    NexusContract NuGet Package Builder and Publisher

.DESCRIPTION
    This script automates the process of building, packing, and optionally publishing
    NexusContract NuGet packages. It supports version management, validation, and
    both local testing and production publishing.

.PARAMETER Version
    The version to assign to packages (e.g., "1.0.0-preview.1")

.PARAMETER Configuration
    Build configuration (Debug or Release, default: Release)

.PARAMETER NoPack
    Skip packing step (build only)

.PARAMETER Publish
    Push packages to NuGet.org after packing

.PARAMETER Source
    NuGet source URL (default: https://api.nuget.org/v3/index.json)

.PARAMETER ApiKey
    NuGet API key (required for -Publish). Can also be set via $env:NUGET_API_KEY

.PARAMETER LocalOnly
    Pack for local testing (skips validation, no publish)

.EXAMPLE
    .\pack.ps1 -Version "1.0.0-preview.1"
    # Build and pack packages with specified version

.EXAMPLE
    .\pack.ps1 -Version "1.0.0" -Publish -ApiKey "your-api-key"
    # Build, pack, and publish to NuGet.org

.EXAMPLE
    .\pack.ps1 -LocalOnly
    # Quick local build for testing (uses default version from .csproj)
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [Parameter(Mandatory = $false)]
    [switch]$NoPack,

    [Parameter(Mandatory = $false)]
    [switch]$Publish,

    [Parameter(Mandatory = $false)]
    [string]$Source = "https://api.nuget.org/v3/index.json",

    [Parameter(Mandatory = $false)]
    [string]$ApiKey,

    [Parameter(Mandatory = $false)]
    [switch]$LocalOnly
)

# ============================================================================
# Configuration
# ============================================================================

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$ArtifactsDir = Join-Path $RepoRoot "dist"
$SrcDir = Join-Path $RepoRoot "src"

$Projects = @(
    @{ Path = "NexusContract.Abstractions/NexusContract.Abstractions.csproj"; Name = "Abstractions" },
    @{ Path = "NexusContract.Core/NexusContract.Core.csproj"; Name = "Core" },
    @{ Path = "NexusContract.Client/NexusContract.Client.csproj"; Name = "Client" },
    @{ Path = "Providers/NexusContract.Providers.Alipay/NexusContract.Providers.Alipay.csproj"; Name = "Alipay Provider" }
)

# ============================================================================
# Load Environment Variables from .env
# ============================================================================

function Load-EnvFile {
    param([string]$EnvFilePath)
    
    if (Test-Path $EnvFilePath) {
        Write-Host "ℹ️  Loading environment from .env file..." -ForegroundColor Blue
        Get-Content $EnvFilePath | ForEach-Object {
            if ($_ -match '^\s*([^#][^=]+)=(.*)$') {
                $key = $matches[1].Trim()
                $value = $matches[2].Trim()
                Set-Item -Path "env:$key" -Value $value
                Write-Host "  ✓ Loaded $key" -ForegroundColor DarkGray
            }
        }
    }
}

$EnvFilePath = Join-Path $RepoRoot ".env"
Load-EnvFile -EnvFilePath $EnvFilePath

# ============================================================================
# Helper Functions
# ============================================================================

function Write-Step {
    param([string]$Message)
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✅ $Message" -ForegroundColor Green
}

function Write-Error {
    param([string]$Message)
    Write-Host "❌ $Message" -ForegroundColor Red
}

function Write-Warning {
    param([string]$Message)
    Write-Host "⚠️  $Message" -ForegroundColor Yellow
}

function Write-Info {
    param([string]$Message)
    Write-Host "ℹ️  $Message" -ForegroundColor Blue
}

# ============================================================================
# Validation
# ============================================================================

Write-Step "NexusContract Package Builder"

# Check .NET SDK
Write-Info "Checking .NET SDK..."
$dotnetVersion = dotnet --version
if ($LASTEXITCODE -ne 0) {
    Write-Error ".NET SDK not found. Please install .NET 10 SDK."
    exit 1
}
Write-Success ".NET SDK $dotnetVersion found"

# Validate version format
if ($Version -and $Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') {
    Write-Error "Invalid version format. Expected: X.Y.Z or X.Y.Z-suffix (e.g., 1.0.0-preview.1)"
    exit 1
}

# Validate API key for publish
if ($Publish) {
    $ApiKey = if ($ApiKey) { $ApiKey } else { $env:NUGET_API_KEY }
    if (-not $ApiKey) {
        Write-Error "NuGet API key required for publishing. Use -ApiKey parameter or set NUGET_API_KEY environment variable."
        exit 1
    }
    Write-Success "API key found"
}

# ============================================================================
# Clean
# ============================================================================

Write-Step "Cleaning Artifacts"

if (Test-Path $ArtifactsDir) {
    Remove-Item -Path $ArtifactsDir -Recurse -Force
    Write-Success "Removed existing dist directory"
}

New-Item -ItemType Directory -Path $ArtifactsDir | Out-Null
Write-Success "Created dist directory: $ArtifactsDir"

# ============================================================================
# Restore
# ============================================================================

Write-Step "Restoring Dependencies"

dotnet restore "$RepoRoot/NexusContract.sln"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Restore failed"
    exit 1
}
Write-Success "Dependencies restored"

# ============================================================================
# Build
# ============================================================================

Write-Step "Building Solution"

$buildArgs = @(
    "build",
    "$RepoRoot/NexusContract.sln",
    "--configuration", $Configuration,
    "--no-restore"
)

if ($Version) {
    $buildArgs += "-p:Version=$Version"
}

if (-not $LocalOnly) {
    $buildArgs += "-p:ContinuousIntegrationBuild=true"
}

dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}
Write-Success "Build completed successfully"

# ============================================================================
# Pack
# ============================================================================

if ($NoPack) {
    Write-Warning "Skipping pack step (-NoPack flag)"
    exit 0
}

Write-Step "Packing NuGet Packages"

$packArgs = @(
    "--configuration", $Configuration,
    "--no-build",
    "--output", $ArtifactsDir
)

if ($Version) {
    $packArgs += "-p:Version=$Version"
}

if (-not $LocalOnly) {
    $packArgs += "-p:ContinuousIntegrationBuild=true"
    $packArgs += "-p:EmbedUntrackedSources=true"
}

foreach ($project in $Projects) {
    $projectPath = Join-Path $SrcDir $project.Path
    Write-Info "Packing $($project.Name)..."
    
    dotnet pack $projectPath @packArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Pack failed for $($project.Name)"
        exit 1
    }
    Write-Success "$($project.Name) packed"
}

# ============================================================================
# Validate Packages
# ============================================================================

Write-Step "Validating Packages"

$packages = Get-ChildItem -Path $ArtifactsDir -Filter "*.nupkg" | Where-Object { $_.Name -notmatch "\.snupkg$" }

if ($packages.Count -eq 0) {
    Write-Error "No packages found in dist directory"
    exit 1
}

Write-Info "Found $($packages.Count) package(s):"
foreach ($pkg in $packages) {
    $sizeMB = [math]::Round($pkg.Length / 1MB, 2)
    Write-Host "  📦 $($pkg.Name) ($sizeMB MB)" -ForegroundColor White
}

# Basic validation: check for README and SourceLink
if (-not $LocalOnly) {
    Write-Info "`nValidating package contents..."
    foreach ($pkg in $packages) {
        $tempDir = Join-Path $ArtifactsDir "temp_$([guid]::NewGuid())"
        Expand-Archive -Path $pkg.FullName -DestinationPath $tempDir -Force
        
        $hasReadme = Test-Path (Join-Path $tempDir "README.md")
        $hasSourceLink = (Get-ChildItem -Path $tempDir -Filter "*.sourcelink.json" -Recurse).Count -gt 0
        
        if ($hasReadme -and $hasSourceLink) {
            Write-Success "$($pkg.Name): README ✓, SourceLink ✓"
        } else {
            Write-Warning "$($pkg.Name): README=$hasReadme, SourceLink=$hasSourceLink"
        }
        
        Remove-Item -Path $tempDir -Recurse -Force
    }
}

Write-Success "All packages validated"

# ============================================================================
# Publish
# ============================================================================

if ($Publish) {
    Write-Step "Publishing to NuGet.org"
    
    Write-Warning "About to publish $($packages.Count) package(s) to $Source"
    Write-Host "Packages:" -ForegroundColor Yellow
    foreach ($pkg in $packages) {
        Write-Host "  - $($pkg.Name)" -ForegroundColor Yellow
    }
    
    if (-not $LocalOnly) {
        Write-Host "`nPress any key to continue or Ctrl+C to cancel..." -ForegroundColor Yellow
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    
    foreach ($pkg in $packages) {
        Write-Info "Publishing $($pkg.Name)..."
        
        dotnet nuget push $pkg.FullName `
            --source $Source `
            --api-key $ApiKey `
            --skip-duplicate
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to publish $($pkg.Name)"
            exit 1
        }
        Write-Success "$($pkg.Name) published"
    }
    
    Write-Success "All packages published successfully!"
    Write-Info "`nPackages will be available at https://www.nuget.org/packages within a few minutes."
} elseif (-not $LocalOnly) {
    Write-Info "`nPackages are ready in: $ArtifactsDir"
    Write-Info "To publish, run: .\pack.ps1 -Version `"$Version`" -Publish -ApiKey `"your-key`""
}

# ============================================================================
# Summary
# ============================================================================

Write-Step "Build Summary"

Write-Success "Configuration: $Configuration"
if ($Version) {
    Write-Success "Version: $Version"
}
Write-Success "Artifacts: $ArtifactsDir"
Write-Success "Packages: $($packages.Count)"

if ($LocalOnly) {
    Write-Info "Local build completed (no validation/publish)"
} elseif ($Publish) {
    Write-Success "Published to NuGet.org"
} else {
    Write-Info "Ready to publish (use -Publish flag)"
}

Write-Host "`n✨ Done!`n" -ForegroundColor Green
