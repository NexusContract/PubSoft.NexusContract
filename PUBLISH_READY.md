# 🚀 NuGet Package Publishing - Ready to Publish

## ✅ Packages Built Successfully

All 4 packages have been built and are ready in the `dist/` directory:

```
📦 NexusContract.Abstractions.1.0.0-preview.1.nupkg (18.01 KB)
📦 NexusContract.Core.1.0.0-preview.1.nupkg (33.28 KB)
📦 NexusContract.Client.1.0.0-preview.1.nupkg (16.81 KB)
📦 NexusContract.Providers.Alipay.1.0.0-preview.1.nupkg (19.56 KB)
```

Plus 4 symbol packages (.snupkg) for debugging support.

## 🔐 API Key Configuration

API Key is now loaded from `.env` file automatically.

**Current setup:**
- ✅ `.env` file created with NUGET_API_KEY
- ✅ `.env` added to .gitignore (won't be committed)
- ✅ Pack script loads `.env` automatically

## ⚠️ Publishing Error Resolution

If you encounter **403 Forbidden** error when publishing:

### Cause 1: API Key Permissions

Visit https://www.nuget.org/account/apikeys and verify:
- ✅ Key has **"Push"** permission
- ✅ Key is not expired
- ✅ Glob pattern allows `NexusContract.*` or `*` (all packages)

### Cause 2: Package ID Already Reserved

Check if `NexusContract.*` package IDs are already taken:
- Visit https://www.nuget.org/packages?q=NexusContract
- If packages exist but you don't own them, you need to:
  - Contact the owner to transfer ownership, OR
  - Use a different package ID prefix

### Solution: Use Your Own Package ID Prefix

If `NexusContract` is taken, update package IDs in `src/Directory.Build.props`:

```xml
<PackageId>YourCompany.NexusContract.$(MSBuildProjectName)</PackageId>
<!-- Or use your GitHub username -->
<PackageId>YourUsername.NexusContract.$(MSBuildProjectName)</PackageId>
```

Then rebuild:

```powershell
.\pack.ps1 -Version "1.0.0-preview.1"
```

## 📋 Next Steps

### Option 1: Publish Using Script (Recommended)

```powershell
# Set your NuGet API Key (get from https://www.nuget.org/account/apikeys)
$env:NUGET_API_KEY = "your-nuget-api-key-here"

# Publish all packages
.\pack.ps1 -Version "1.0.0-preview.1" -Publish
```

### Option 2: Manual Publish

```powershell
# Publish each package individually
dotnet nuget push dist\NexusContract.Abstractions.1.0.0-preview.1.nupkg `
    --source https://api.nuget.org/v3/index.json `
    --api-key $env:NUGET_API_KEY `
    --skip-duplicate

dotnet nuget push dist\NexusContract.Core.1.0.0-preview.1.nupkg `
    --source https://api.nuget.org/v3/index.json `
    --api-key $env:NUGET_API_KEY `
    --skip-duplicate

dotnet nuget push dist\NexusContract.Client.1.0.0-preview.1.nupkg `
    --source https://api.nuget.org/v3/index.json `
    --api-key $env:NUGET_API_KEY `
    --skip-duplicate

dotnet nuget push dist\NexusContract.Providers.Alipay.1.0.0-preview.1.nupkg `
    --source https://api.nuget.org/v3/index.json `
    --api-key $env:NUGET_API_KEY `
    --skip-duplicate
```

### Option 3: Automated Publishing via GitHub Actions

Create and push a version tag:

```bash
git add .
git commit -m "chore: prepare for NuGet publish - use dist directory"
git push origin main

# Create version tag
git tag -a v1.0.0-preview.1 -m "Release 1.0.0-preview.1"
git push origin v1.0.0-preview.1
```

**Prerequisites for GitHub Actions:**
- Add `NUGET_API_KEY` to GitHub repository secrets
- Path: Settings → Secrets and variables → Actions → New repository secret

## 🔐 Getting NuGet API Key

1. Visit https://www.nuget.org/account/apikeys
2. Click **"Create"**
3. Set options:
   - **Key Name**: NexusContract-Publish
   - **Expiration**: 365 days (or custom)
   - **Scopes**: Push (select specific packages or all)
4. Click **"Create"**
5. **Copy the API key** (you won't see it again!)

## 📊 Package Validation

Before publishing, packages have been validated for:

- ✅ README.md embedded
- ✅ Symbol packages (.snupkg) generated
- ✅ SourceLink metadata included
- ✅ Deterministic build (ContinuousIntegrationBuild=true)
- ✅ Version: 1.0.0-preview.1
- ✅ All dependencies correctly referenced

## 🎯 Post-Publishing Verification

After publishing, verify packages are live:

```powershell
# Search for packages (may take 5-10 minutes to index)
dotnet nuget search "NexusContract" --prerelease

# Or visit directly:
# https://www.nuget.org/packages/NexusContract.Abstractions/
# https://www.nuget.org/packages/NexusContract.Core/
# https://www.nuget.org/packages/NexusContract.Client/
# https://www.nuget.org/packages/NexusContract.Providers.Alipay/
```

## 🔄 For Future Releases

```powershell
# Clean previous build
Remove-Item -Path dist -Recurse -Force -ErrorAction SilentlyContinue

# Build new version
.\pack.ps1 -Version "1.0.0-preview.2"

# Publish
.\pack.ps1 -Version "1.0.0-preview.2" -Publish
```

---

**Note:** The `dist/` directory is ignored by git (already added to .gitignore).
