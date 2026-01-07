# NuGet 包发布指南

本文档描述如何发布 NexusContract NuGet 包到 NuGet.org。

## 📦 发布的包

| 包名 | 描述 | 目标框架 |
|------|------|----------|
| `NexusContract.Abstractions` | 核心抽象层（契约、属性） | netstandard2.0 |
| `NexusContract.Core` | 核心引擎（网关、验证器） | .NET 10 |
| `NexusContract.Client` | 客户端 SDK（HTTP 通信） | .NET 10 |
| `NexusContract.Providers.Alipay` | 支付宝提供商实现 | .NET 10 |

## 🚀 快速发布（本地脚本）

### 方式 1: 本地测试打包

```powershell
# 仅构建和打包，不发布（用于本地测试）
.\pack.ps1 -Version "1.0.0-preview.1" -LocalOnly
```

### 方式 2: 构建、验证、发布

```powershell
# 设置 API Key（二选一）
$env:NUGET_API_KEY = "your-nuget-api-key"
# 或者直接传参
.\pack.ps1 -Version "1.0.0-preview.1" -Publish -ApiKey "your-nuget-api-key"
```

### 脚本参数说明

| 参数 | 说明 | 示例 |
|------|------|------|
| `-Version` | 包版本号（语义化版本） | `1.0.0-preview.1` |
| `-Configuration` | 构建配置（Debug/Release） | `Release` (默认) |
| `-Publish` | 发布到 NuGet.org | 开关参数 |
| `-ApiKey` | NuGet API Key（或用环境变量 `NUGET_API_KEY`） | `oy2abc...` |
| `-LocalOnly` | 仅本地打包，跳过验证 | 开关参数 |
| `-NoPack` | 仅构建，不打包 | 开关参数 |

## 🔄 自动化发布（GitHub Actions）

### 触发方式

#### 方式 1: Git Tag 触发（推荐）

```bash
# 创建版本标签
git tag -a v1.0.0-preview.1 -m "Release 1.0.0-preview.1"
git push origin v1.0.0-preview.1

# GitHub Actions 自动触发：
# 1. 构建和测试
# 2. 打包 NuGet 包
# 3. 发布到 NuGet.org
# 4. 创建 GitHub Release
```

#### 方式 2: 手动触发

1. 访问 GitHub 仓库的 **Actions** 标签页
2. 选择 **"Publish NuGet Packages"** 工作流
3. 点击 **"Run workflow"**
4. 输入版本号（如 `1.0.0-preview.1`）
5. 点击 **"Run workflow"** 按钮

### 配置 GitHub Secrets

在 GitHub 仓库设置中添加以下 Secret：

1. 访问 **Settings → Secrets and variables → Actions**
2. 添加 `NUGET_API_KEY`（从 NuGet.org 获取 API Key）

**获取 NuGet API Key:**
1. 访问 https://www.nuget.org/account/apikeys
2. 创建新 API Key（权限选择 "Push"）
3. 复制并保存到 GitHub Secrets

## 📋 发布前检查清单

### 代码准备

- [ ] 确保所有测试通过 (`dotnet test`)
- [ ] 更新版本号和 Release Notes（在各项目的 `.csproj` 中）
- [ ] 更新 `README.md` 和包级 `README.md`
- [ ] 确认 `IMPLEMENTATION.md` 与代码同步

### 包配置验证

- [ ] `Directory.Build.props` 配置完整（SourceLink、符号包）
- [ ] 各项目 `.csproj` 的 NuGet 元数据完整
  - PackageId, Title, Description
  - PackageTags, PackageReleaseNotes
  - RepositoryUrl, PackageLicenseExpression
- [ ] README.md 存在于各包目录（自动嵌入包）

### 构建验证

```powershell
# 本地验证构建
.\pack.ps1 -Version "1.0.0-preview.1" -LocalOnly

# 检查生成的包
Get-ChildItem .\artifacts\*.nupkg | ForEach-Object {
    Write-Host $_.Name
    # 解压查看内容
    Expand-Archive $_.FullName -DestinationPath ".\artifacts\temp" -Force
    Get-ChildItem ".\artifacts\temp" -Recurse
}
```

## 🔍 包验证

### 验证包内容

```powershell
# 查看包内文件列表（PowerShell）
Expand-Archive .\artifacts\NexusContract.Core.1.0.0-preview.1.nupkg -DestinationPath .\temp
Get-ChildItem .\temp -Recurse

# 或使用 NuGet CLI
nuget list NexusContract.Core -Prerelease
```

### 必须包含的内容

✅ **包内应包含:**
- `lib/net10.0/*.dll`（或 `lib/netstandard2.0/*.dll`）
- `README.md`（包说明文档）
- `*.pdb`（符号文件，调试用）
- `*.sourcelink.json`（源码链接）
- `.nuspec`（包元数据）

## 📚 版本管理策略

### 语义化版本 (Semantic Versioning)

```
主版本.次版本.修订版本-预发布标识

示例:
1.0.0-preview.1  ← 第一个预览版
1.0.0-preview.2  ← 第二个预览版
1.0.0-rc.1       ← Release Candidate
1.0.0            ← 正式版
1.0.1            ← 修订版（Bug 修复）
1.1.0            ← 次版本（新特性，向后兼容）
2.0.0            ← 主版本（破坏性更改）
```

### 当前版本规划

- `1.0.0-preview.x` - 预览版（当前）
- `1.0.0-rc.x` - 候选版本
- `1.0.0` - 正式版

## 🐛 常见问题

### 问题 1: SourceLink 验证失败

**症状:** GitHub Actions 构建失败，提示 "SourceLink validation failed"

**解决:**
```xml
<!-- 在 Directory.Build.props 中确保 -->
<PublishRepositoryUrl>true</PublishRepositoryUrl>
<EmbedUntrackedSources>true</EmbedUntrackedSources>
<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
```

### 问题 2: 符号包未生成

**症状:** 只生成 `.nupkg`，没有 `.snupkg`

**解决:**
```xml
<!-- 在 Directory.Build.props 中确保 -->
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```

### 问题 3: README 未嵌入包

**症状:** NuGet.org 不显示包说明

**解决:**
1. 确保项目目录存在 `README.md`
2. 检查 `.csproj` 是否继承了 `Directory.Build.props` 的 `PackageReadmeFile` 配置
3. 或在 `.csproj` 中显式添加：
   ```xml
   <ItemGroup>
     <None Include="README.md" Pack="true" PackagePath="\" />
   </ItemGroup>
   ```

### 问题 4: 包依赖版本不匹配

**症状:** 运行时报 "Could not load file or assembly" 错误

**解决:**
- 确保所有包版本一致（同时发布）
- 检查 `<ProjectReference>` 是否正确（不要用 `<PackageReference>` 引用本解决方案内的项目）

## 🔐 安全最佳实践

### API Key 管理

❌ **不要:**
- 在代码中硬编码 API Key
- 提交 API Key 到 Git 仓库
- 分享或公开 API Key

✅ **推荐:**
- 使用 GitHub Secrets 存储 API Key
- 定期轮换 API Key
- 限制 API Key 权限（仅 Push，不包括 Unlist/Delete）

### 包签名（未来）

```powershell
# 使用证书签名包（可选，提升信任度）
dotnet nuget sign .\artifacts\*.nupkg \
    --certificate-path certificate.pfx \
    --timestamper http://timestamp.digicert.com
```

## 📖 参考资源

- [NuGet 官方文档](https://docs.microsoft.com/nuget/)
- [语义化版本规范](https://semver.org/)
- [SourceLink 文档](https://github.com/dotnet/sourcelink)
- [GitHub Actions 文档](https://docs.github.com/actions)

## 🎯 发布后验证

### 1. 检查 NuGet.org

```powershell
# 搜索已发布的包
dotnet nuget search "NexusContract" --prerelease

# 或访问
https://www.nuget.org/packages/NexusContract.Core/
```

### 2. 测试安装

```powershell
# 创建测试项目
mkdir test-install
cd test-install
dotnet new console
dotnet add package NexusContract.Abstractions --version 1.0.0-preview.1 --prerelease
dotnet add package NexusContract.Providers.Alipay --version 1.0.0-preview.1 --prerelease

# 创建测试代码（Program.cs）
@'
using NexusContract.Providers.Alipay;
using Demo.Alipay.Contract.Transactions;

var provider = new AlipayProvider("test-app-id", "test-key", "test-pub-key");
var diagnostics = provider.PreloadMetadata();
Console.WriteLine($"Health: {diagnostics.IsHealthy}");
'@ | Out-File Program.cs

dotnet build
```

### 3. 验证 SourceLink

```csharp
// 在 Visual Studio 中启用 SourceLink 调试
// Tools → Options → Debugging → General
// ✅ Enable Source Link support
// ✅ Enable source server support

// 设置断点进入 NexusContract 代码，验证能否跳转到 GitHub 源码
```

---

**维护者:** NexusContract  
**最后更新:** 2025-01-15
