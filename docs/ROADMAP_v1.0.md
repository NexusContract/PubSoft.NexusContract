# NexusContract v1.0 执行路线图

> **目标版本：** v1.0.0  
> **当前状态：** Phase 1 完成（多租户删除，代码库清洁）  
> **发布目标：** 2026-02-15  
> **核心原则：** 保守发版，v1.0 仅包含核心功能，不规划新特性

---

## 📊 v1.0 发布路线

| 阶段 | 状态 | 目标 | 时间线 |
|------|------|------|--------|
| **Phase 1：代码清洁** | ✅ 完成 | 删除多租户、NexusGuard 创建、编译验证 | 完成 |
| **Phase 2：代码审查** | 🟡 进行中 | PR 代码审查、修复遗留问题 | 2026-01-13 ~ 01-15 |
| **Phase 3：测试** | ⏳ 待执行 | 单元测试、集成测试执行 | 2026-01-16 ~ 01-19 |
| **Phase 4：发版** | ⏳ 待执行 | 版本号更新、标签创建、发布 | 2026-01-20 ~ 01-22 |

---

## ✅ Phase 1：已完成工作

### 1.1 多租户架构删除

**已删除：**
- ✅ `ITenantIdentity.cs` — 身份容器接口
- ✅ `TenantContext.cs` — 多租户上下文
- ✅ `TenantContextFactory.cs` — 魔法身份提取
- ✅ `TenantConfigurationManager.cs` — 多层级索引
- ✅ `TenantIdentityExtensions.cs` — 扩展方法

**验证：**
```bash
# 编译：0 错误，~20 可忽略警告（NETSDK1057）
dotnet build
# Exit Code: 0 ✅
```

### 1.2 接口签名更新

**核心变更：**
```csharp
// 旧签名 (v0.x)
ExecuteAsync<T>(IApiRequest<T> req, ITenantIdentity identity, CancellationToken ct)

// 新签名 (v1.0)
ExecuteAsync<T>(IApiRequest<T> req, string providerName, string profileId, CancellationToken ct)
```

**更新文件：**
- ✅ INexusEngine.cs
- ✅ NexusEngine.cs
- ✅ IConfigurationResolver.cs
- ✅ InMemoryConfigResolver.cs
- ✅ HybridConfigResolver.cs
- ✅ NexusEndpoint.cs
- ✅ AlipayEndpointBase.cs（示例）

### 1.3 NexusGuard 创建

**特性：**
- ⚡ 零分配（仅两个空检查）
- 🎯 JIT 可内联（宪法 007）
- 💥 原位爆炸异常（宪法 012）

**用法：**
```csharp
NexusGuard.EnsurePhysicalAddress("Alipay", profileId, nameof(NexusEndpoint));
```

### 1.4 文档对齐

**已更新：**
- ✅ CONSTITUTIONAL_FRAMEWORK.md — 移除多租户，强调 NexusGuard
- ✅ CONSTITUTIONAL_REFACTOR_ROADMAP.md — 路由参数标准化
- ✅ 示例代码 — {merchantId} → {profileId}

---

## 🔍 Phase 2：代码审查（进行中）

### 2.1 审查清单

- [ ] **代码一致性检查**
  - 验证所有 Endpoint 都调用 `NexusGuard.EnsurePhysicalAddress()`
  - 验证所有 Route 使用 `{profileId}` 而非 `{merchantId}`
  - 确认无 ITenantIdentity 遗留引用

- [ ] **签名一致性检查**
  - 验证 INexusEngine.ExecuteAsync 调用统一使用 `(request, providerName, profileId, ct)`
  - 验证 IConfigurationResolver.ResolveAsync 调用统一使用 `(providerName, profileId, ct)`
  - 检查测试文件中的 Mock 是否已更新

- [ ] **缓存键设计检查**
  - 验证 InMemoryConfigResolver 使用 `{provider}:{profileId}` 键格式
  - 验证 HybridConfigResolver 的 Redis 操作统一

- [ ] **文件扫描**
  ```bash
  # 确保无剩余 ITenantIdentity 引用
  grep -r "ITenantIdentity" src/ --include="*.cs"
  
  # 确保无剩余 TenantContext 引用
  grep -r "TenantContext" src/ --include="*.cs"
  
  # 验证 merchantId 路由参数已完全替换
  grep -r "{merchantId}" src/ --include="*.cs"
  ```

### 2.2 PR 合并流程

```bash
# 当前分支：refactor/remove-ITenantIdentity
# 目标分支：main

# 推送代码
git push origin refactor/remove-ITenantIdentity

# 创建 PR，抄送审查人
# PR 名称：Remove multi-tenant ITenantIdentity, enforce explicit parameter passing
# PR 描述：
# - Deleted: 5 Tenant-related classes
# - Changed: 4 core interface signatures
# - Added: NexusGuard zero-allocation validation
# - Build: 0 errors, ~20 expected warnings
```

---

## 🧪 Phase 3：测试执行（待执行，预计 2026-01-16）

### 3.1 单元测试更新

**必须更新的文件：**
```
tests/
  ├─ NexusContract.Core.Tests/
  │  ├─ Engine/NexusEngineTests.cs
  │  ├─ Configuration/InMemoryConfigResolverTests.cs
  │  └─ Configuration/HybridConfigResolverTests.cs
  └─ NexusContract.Hosting.Tests/
     ├─ Endpoints/NexusEndpointTests.cs
     └─ Endpoints/AlipayEndpointTests.cs
```

**关键变更：**
```csharp
// OLD: 创建 Mock<ITenantIdentity>
var mockIdentity = new Mock<ITenantIdentity>();
mockIdentity.Setup(x => x.ProfileId).Returns("MERCHANT001");
await _engine.ExecuteAsync(request, mockIdentity.Object, ct);

// NEW: 直接传递字符串参数
await _engine.ExecuteAsync(request, "Alipay", "MERCHANT001", ct);
```

**测试命令：**
```bash
dotnet test --configuration Release --logger "console;verbosity=normal"
```

**成功标准：**
- ✅ 所有测试通过（0 失败）
- ✅ 代码覆盖率 ≥ 80%

### 3.2 集成测试场景

| 场景 | 路由 | 预期 |
|------|------|------|
| 有效请求 | `POST /{providerName}/MERCHANT001/pay` | 200 OK |
| 缺少 ProfileId | `POST /{providerName}//pay` | 400 Bad Request（NXC201） |
| 空 ProfileId | `POST /{providerName}//pay` | 400 Bad Request（NXC201） |
| 配置未找到 | `POST /{providerName}/UNKNOWN/pay` | 404 Not Found（NXC301） |

**测试工具：**
```bash
# 使用 FastEndpoints 内置测试框架
dotnet test tests/NexusContract.Hosting.Tests/
```

### 3.3 性能验证

**性能指标（基准）：**
- NexusGuard.EnsurePhysicalAddress：< 100ns
- 配置解析（从 L1 缓存）：< 50μs
- 完整执行链路（Validate → Project → Execute → Hydrate）：< 500ms

---

## 📦 Phase 4：发版（待执行，预计 2026-01-20）

### 4.1 版本号更新

**文件：** [Directory.Build.props](../src/Directory.Build.props)

```xml
<!-- 当前 -->
<VersionPrefix>1.0.0</VersionPrefix>

<!-- 不变 -->
<VersionPrefix>1.0.0</VersionPrefix>
```

**发布版本序列：**
- `1.0.0-rc.1` — RC 候选（如需要）
- `1.0.0` — 正式版本

### 4.2 Git 标签创建

```bash
git tag -a v1.0.0 \
  -m "v1.0.0: Remove multi-tenant ITenantIdentity, enforce explicit parameter passing

BREAKING CHANGES:
- Deleted: ITenantIdentity, TenantContext, TenantContextFactory
- Changed: INexusEngine.ExecuteAsync(req, providerName, profileId, ct)
- Changed: IConfigurationResolver.ResolveAsync(providerName, profileId, ct)
- Added: NexusGuard.EnsurePhysicalAddress() zero-allocation validation

Migration: See MIGRATION_TO_v1.0.md"

git push origin v1.0.0
```

### 4.3 发布文档

**必需文档：**

#### CHANGELOG.md
```markdown
## [1.0.0] - 2026-01-20

### BREAKING CHANGES
- Deleted `ITenantIdentity` interface
- Deleted `TenantContext` class
- Deleted `TenantContextFactory` class
- Changed `INexusEngine.ExecuteAsync()` signature
- Changed `IConfigurationResolver.ResolveAsync()` signature

### NEW FEATURES
- `NexusGuard.EnsurePhysicalAddress()` — zero-allocation validation
- Explicit ProfileId routing — improved clarity and performance

### MIGRATION
See [MIGRATION_TO_v1.0.md](docs/MIGRATION_TO_v1.0.md)
```

#### MIGRATION_TO_v1.0.md
```markdown
# 迁移指南：v0.x → v1.0

## 路由参数变更

### OLD (v0.x)
POST /{providerName}/pay
Body: { "merchantId": "2088..." }

### NEW (v1.0)
POST /{providerName}/{profileId}/pay
URL: providerName 和 profileId 必须在路径中

## 代码变更

### OLD
var identity = await _factory.CreateAsync(HttpContext);
var response = await _engine.ExecuteAsync(request, identity, ct);

### NEW
var profileId = Route<string>("profileId");
var response = await _engine.ExecuteAsync(request, "Alipay", profileId, ct);
```

### 4.4 NuGet 包发布

```bash
dotnet pack src/NexusContract.Abstractions/NexusContract.Abstractions.csproj \
  --configuration Release \
  --output ./nupkg

dotnet nuget push nupkg/*.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key $NUGET_API_KEY
```

---

## 📋 完整检查清单

### Phase 1（已完成）
- [x] 删除 5 个 Tenant 相关类
- [x] 更新 4 个核心接口签名
- [x] 创建 NexusGuard
- [x] 编译验证（0 错误）
- [x] 文档对齐

### Phase 2（进行中）
- [ ] 运行代码扫描（确保无遗留引用）
- [ ] 代码审查（pull request）
- [ ] 合并到 main

### Phase 3（待执行）
- [ ] 更新单元测试（新签名）
- [ ] 运行测试套件
- [ ] 验证集成测试场景

### Phase 4（待执行）
- [ ] 版本号确认
- [ ] Git 标签创建
- [ ] 发布文档完成
- [ ] NuGet 包发布

---

## 🎯 注意事项

### 范围限制
- ✅ v1.0 是稳定版本，仅包含核心功能
- ❌ 不规划新特性（defer to v1.1+）
- ❌ 不引入大规模重构（仅修复多租户遗留问题）

### 性能约束
- NexusGuard 必须 JIT 可内联（< 100ns）
- 缓存命中率需要 > 95%（L1 缓存）
- 无新的反射调用（宪法 007）

### 文档约束
- 所有代码示例必须使用 v1.0 新签名
- MIGRATION 文档必须清晰易懂
- 无 v2.0 规划（保留给后续版本）

---

## � 12 条宪法的 v1.0 实现进展

| 宪法序号 | 名称 | 物理约束 | v1.0 状态 |
|---------|------|--------|---------|
| **001** | 显式契约锁定 | Contract 启动期冻结为 FrozenDictionary | ✅ 代码完成 / 🔄 测试中 |
| **002** | URL 资源寻址 | ProfileId 从路径显式给定，NexusGuard 防御 | ✅ 代码完成 / 🔄 测试中 |
| **003** | 物理槽位隔离 | Provider:ProfileId 唯一寻址，无 Realm 索引 | ✅ 代码完成 / 🔄 测试中 |
| **004** | BFF/Gate 职责拆分 | BFF 负责身份→ProfileId，Gate 仅执行 | ✅ 代码完成 / 🔄 测试中 |
| **005** | 热路径脱网自治 | L1 缓存 30 天绝对过期，支撑 Redis 离线 | ✅ 代码完成 / 🔄 测试中 |
| **006** | 启动期全量体检 | 启动失败 ⟺ 元数据不可靠（无降级） | ✅ 代码完成 / 🔄 测试中 |
| **007** | 零反射缓存引擎 | Projection/Hydration 走智能缓存反射，无重复反射调用 | ✅ 代码完成 / 🔄 测试中 |
| **008** | 四阶段原子管道 | Validate → Project → Execute → Hydrate 各自独立 | ✅ 代码完成 / 🔄 测试中 |
| **009** | Provider 协议主权 | 各 Provider 独立签名算法，框架无权干涉 | ✅ 代码完成 / 🔄 测试中 |
| **010** | Provider 无状态单例 | 同一 Provider 服务所有 ProfileId，无状态 | ✅ 代码完成 / 🔄 测试中 |
| **011** | 单一标准加密存储 | 私钥 Redis 中 AES 加密（Base64 编码） | ✅ 代码完成 / ✅ 测试通过 |
| **012** | NXC 结构化诊断 | 每个错误立即在发生阶段生成 NXC 码 | ✅ 代码完成 / 🔄 测试中 |

**v1.0 完整度：** 12/12 宪法完成（零反射缓存引擎已实现）

---

## 🔄 后续版本（v1.1+）规划

**不在 v1.0 范围内：**
- 新 Provider 集成（WeChat, UnionPay 等）
- 性能优化（缓存预热、P50 延迟优化）
- 新诊断工具（链路追踪、性能分析）
- SDK 扩展（Node.js, Java, Go 等）

**v1.0 稳定后再规划上述功能。**

---

## 进度追踪

```
2026-01-11  ✅ Phase 1 完成
2026-01-13  🔄 Phase 2 开始
2026-01-15  ⏳ Phase 2 目标完成
2026-01-16  ⏳ Phase 3 开始
2026-01-19  ⏳ Phase 3 完成
2026-01-20  ⏳ Phase 4 完成，v1.0.0 发布
```

---

最后更新：2026-01-11
