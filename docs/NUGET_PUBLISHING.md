# NuGet Package Publishing Guide

[**中文文档 / Chinese Documentation**](./NUGET_PUBLISHING.zh-CN.md)

This document describes how to publish NexusContract NuGet packages to NuGet.org.

## 📦 Published Packages

| Package | Description | Target Framework |
|---------|-------------|------------------|
| `NexusContract.Abstractions` | Core abstraction layer (contracts, attributes) | netstandard2.0 |
| `NexusContract.Core` | Core engine (gateway, validator) | .NET 10 |
| `NexusContract.Client` | Client SDK (HTTP communication) | .NET 10 |
| `NexusContract.Providers.Alipay` | Alipay provider implementation | .NET 10 |

## 🚀 Quick Publishing (Local Script)

### Option 1: Local Testing Pack

```powershell
# Build and pack only, no publish (for local testing)
.\pack.ps1 -Version "1.0.0-preview.1" -LocalOnly
```

### Option 2: Build, Validate, Publish

```powershell
# Set API Key (choose one)
$env:NUGET_API_KEY = "your-nuget-api-key"
# OR pass as parameter
.\pack.ps1 -Version "1.0.0-preview.1" -Publish -ApiKey "your-nuget-api-key"
```

### Script Parameters

| Parameter | Description | Example |
|-----------|-------------|---------|
| `-Version` | Package version (semantic versioning) | `1.0.0-preview.1` |
| `-Configuration` | Build configuration (Debug/Release) | `Release` (default) |
| `-Publish` | Publish to NuGet.org | Switch parameter |
| `-ApiKey` | NuGet API Key (or use `NUGET_API_KEY` env var) | `oy2abc...` |
| `-LocalOnly` | Local pack only, skip validation | Switch parameter |
| `-NoPack` | Build only, skip packing | Switch parameter |

## 🔄 Automated Publishing (GitHub Actions)

### Trigger Methods

#### Method 1: Git Tag Trigger (Recommended)

```bash
# Create version tag
git tag -a v1.0.0-preview.1 -m "Release 1.0.0-preview.1"
git push origin v1.0.0-preview.1

# GitHub Actions will automatically:
# 1. Build and test
# 2. Pack NuGet packages
# 3. Publish to NuGet.org
# 4. Create GitHub Release
```

#### Method 2: Manual Trigger

1. Visit your GitHub repository's **Actions** tab
2. Select **"Publish NuGet Packages"** workflow
3. Click **"Run workflow"**
4. Enter version number (e.g., `1.0.0-preview.1`)
5. Click **"Run workflow"** button

### Configure GitHub Secrets

Add the following secret in your GitHub repository settings:

1. Visit **Settings → Secrets and variables → Actions**
2. Add `NUGET_API_KEY` (get API Key from NuGet.org)

**Get NuGet API Key:**
1. Visit https://www.nuget.org/account/apikeys
2. Create new API Key (permission: "Push")
3. Copy and save to GitHub Secrets

## 📋 Pre-Publishing Checklist

### Code Preparation

- [ ] Ensure all tests pass (`dotnet test`)
- [ ] Update version and release notes (in each project's `.csproj`)
- [ ] Update `README.md` and package-level `README.md`
- [ ] Confirm `IMPLEMENTATION.md` is synchronized with code

### Package Configuration Validation

- [ ] `Directory.Build.props` configuration complete (SourceLink, symbol packages)
- [ ] Each project's `.csproj` NuGet metadata complete
  - PackageId, Title, Description
  - PackageTags, PackageReleaseNotes
  - RepositoryUrl, PackageLicenseExpression
- [ ] README.md exists in each package directory (auto-embedded)

### Build Validation

```powershell
# Validate local build
.\pack.ps1 -Version "1.0.0-preview.1" -LocalOnly

# Check generated packages
Get-ChildItem .\artifacts\*.nupkg | ForEach-Object {
    Write-Host $_.Name
    # Extract and view contents
    Expand-Archive $_.FullName -DestinationPath ".\artifacts\temp" -Force
    Get-ChildItem ".\artifacts\temp" -Recurse
}
```

## 🔍 Package Validation

### Verify Package Contents

```powershell
# View package file list (PowerShell)
Expand-Archive .\artifacts\NexusContract.Core.1.0.0-preview.1.nupkg -DestinationPath .\temp
Get-ChildItem .\temp -Recurse

# Or use NuGet CLI
nuget list NexusContract.Core -Prerelease
```

### Required Package Contents

✅ **Packages should include:**
- `lib/net10.0/*.dll` (or `lib/netstandard2.0/*.dll`)
- `README.md` (package documentation)
- `*.pdb` (symbol files for debugging)
- `*.sourcelink.json` (source code link)
- `.nuspec` (package metadata)

## 📚 Version Management Strategy

### Semantic Versioning

```
MAJOR.MINOR.PATCH-PRERELEASE

Examples:
1.0.0-preview.1  ← First preview
1.0.0-preview.2  ← Second preview
1.0.0-rc.1       ← Release Candidate
1.0.0            ← Stable release
1.0.1            ← Patch (bug fixes)
1.1.0            ← Minor (new features, backward compatible)
2.0.0            ← Major (breaking changes)
```

### Current Version Plan

- `1.0.0-preview.x` - Preview releases (current)
- `1.0.0-rc.x` - Release candidates
- `1.0.0` - Stable release

## 🐛 Troubleshooting

### Issue 1: SourceLink Validation Failed

**Symptoms:** GitHub Actions build fails with "SourceLink validation failed"

**Solution:**
```xml
<!-- Ensure in Directory.Build.props -->
<PublishRepositoryUrl>true</PublishRepositoryUrl>
<EmbedUntrackedSources>true</EmbedUntrackedSources>
<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
```

### Issue 2: Symbol Package Not Generated

**Symptoms:** Only `.nupkg` generated, no `.snupkg`

**Solution:**
```xml
<!-- Ensure in Directory.Build.props -->
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```

### Issue 3: README Not Embedded in Package

**Symptoms:** NuGet.org doesn't display package description

**Solution:**
1. Ensure `README.md` exists in project directory
2. Check if `.csproj` inherits `PackageReadmeFile` from `Directory.Build.props`
3. Or explicitly add to `.csproj`:
   ```xml
   <ItemGroup>
     <None Include="README.md" Pack="true" PackagePath="\" />
   </ItemGroup>
   ```

## 🔐 Security Best Practices

### API Key Management

❌ **DON'T:**
- Hard-code API Key in code
- Commit API Key to Git repository
- Share or publicize API Key

✅ **DO:**
- Store API Key in GitHub Secrets
- Rotate API Key regularly
- Limit API Key permissions (Push only, not Unlist/Delete)

## 📖 Reference Resources

- [NuGet Official Documentation](https://docs.microsoft.com/nuget/)
- [Semantic Versioning Specification](https://semver.org/)
- [SourceLink Documentation](https://github.com/dotnet/sourcelink)
- [GitHub Actions Documentation](https://docs.github.com/actions)

## 🎯 Post-Publishing Verification

### 1. Check NuGet.org

```powershell
# Search published packages
dotnet nuget search "NexusContract" --prerelease

# Or visit
https://www.nuget.org/packages/NexusContract.Core/
```

### 2. Test Installation

```powershell
# Create test project
mkdir test-install
cd test-install
dotnet new console
dotnet add package NexusContract.Core --version 1.0.0-preview.1 --prerelease
dotnet build
```

### 3. Verify SourceLink

```csharp
// In Visual Studio, enable SourceLink debugging
// Tools → Options → Debugging → General
// ✅ Enable Source Link support
// ✅ Enable source server support

// Set breakpoint in NexusContract code, verify jump to GitHub source
```

---

**Maintainer:** NexusContract  
**Last Updated:** January 7, 2026
