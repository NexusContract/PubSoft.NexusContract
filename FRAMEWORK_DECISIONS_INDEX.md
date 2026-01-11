# NexusContract 框架决策清单 - 总索引

> **四份核心文档的快速导航和概览**

**编制日期**：2026-01-11  
**文档版本**：1.0  
**总决策数**：51 项架构决策 + 21 项代码模式

---

## 📚 四份核心文档说明

### 1. FRAMEWORK_CORE_DECISIONS.md
**目标**：框架的建筑蓝图  
**内容**：51 项架构决策，分 11 个类别  
**阅读对象**：架构师、技术负责人  

**包含内容**：
- 宪法基础（4 项）：显式、确定性、启动失败、零依赖
- 执行管道（3 项）：REPR-P、四阶段、元数据冻结
- 多租户 ISV（4 项）：双层身份、三层模型、JIT、热更新
- 缓存策略（3 项）：L1/L2、滑动过期、防护三角
- 安全设计（3 项）：加密锁定、Provider 自决、私钥保护
- 新商家隔离（3 项）：快速失败、四重防线、扁平化
- BFF/HttpApi 解耦（3 项）：Map 上移、URL 寻址、分层通知
- 适配器模式（2 项）：无状态单例、适配优先
- 诊断系统（2 项）：NXC 法典、全量扫描
- 版本演进（2 项）：预览版路线、验证清单
- 核心价值观（3 项）：可维护 > 性能、确定性、隔离性

**快速查询**：
```
显式优于隐式          → CF-001
确定性执行            → CF-002
启动期失败            → CF-003
零依赖/最少依赖       → CF-004
REPR-P 模型           → ED-001
四阶段管道            → ED-002
Realm/Profile         → MT-001
三层数据模型          → MT-002
JIT 配置解析          → MT-003
L1/L2 缓存            → CS-001
滑动过期              → CS-002
加密字段锁定          → SD-001
新商家隔离            → ISO-001
Map 层上移            → DECOUPLE-001
NXC 诊断码            → DIAG-001
```

---

### 2. FRAMEWORK_INTERFACE_CONTRACTS.md
**目标**：接口规范和约束  
**内容**：7 大核心接口的完整契约  
**阅读对象**：开发人员、组件实现者  

**包含的接口**：
1. **IApiRequest<TResponse>**（业务意图）
   - 必须标注 [ApiOperation]
   - 实现泛型接口 IApiRequest<TResponse>

2. **IProvider**（平台实现）
   - 无状态单例
   - 配置通过参数传入
   - 支持并发调用

3. **INexusEngine**（请求调度）
   - 多租户路由
   - JIT 配置加载
   - Provider 注册

4. **ITenantIdentity**（身份标识）
   - RealmId + ProfileId + ProviderName
   - 支持序列化
   - 可扩展元数据

5. **IConfigurationResolver**（配置解析）
   - L1/L2 缓存
   - 热更新（Refresh）
   - 批量预热（Warmup）

6. **INexusTransport**（传输层）
   - HTTP/2 多路复用
   - Polly 重试/熔断
   - 连接预热

7. **IProviderConfiguration**（物理配置）
   - AppId、PrivateKey、PublicKey、GatewayUrl
   - 扩展设置
   - 加密存储

**关键约束**：
- C1：线程安全（所有实现必须支持并发）
- C2：配置不可变（从 IConfigurationResolver 获取后不可修改）
- C3：异常映射（三方异常映射为 NexusContract 异常）
- C4：取消令牌支持（所有异步接口必须支持 CancellationToken）

**实现检查清单**：
- 每个接口都有详细的实现检查清单
- 包含 Constructor Requirements 和 Thread Safety 检查

---

### 3. FRAMEWORK_CODE_PATTERNS.md
**目标**：实现模式和最佳实践  
**内容**：21 项核心代码模式  
**阅读对象**：开发人员、代码审查者  

**包含的模式**：
1. **分层架构模式（2 项）**
   - PA-001：入口出口物理分离（Ingress/Egress）
   - PA-002：四阶段管道（Validate→Project→Execute→Hydrate）

2. **元数据管理模式（2 项）**
   - MM-001：启动期元数据冻结
   - MM-002：体检机制

3. **多租户 ISV 模式（3 项）**
   - ISV-001：Realm/Profile 双层身份
   - ISV-002：JIT 配置解析
   - ISV-003：配置热更新

4. **安全和加密模式（2 项）**
   - SEC-001：加密字段处理
   - SEC-002：签名验证

5. **诊断和监控模式（2 项）**
   - DIAG-001：结构化诊断报告
   - DIAG-002：性能指标收集

6. **依赖注入模式（1 项）**
   - DI-001：标准 DI 注册

**每个模式包含**：
- 原则说明
- 代码实现
- 性能特征
- 使用场景

---

## 🎯 使用场景导航

### 场景 1：理解框架核心思想
**文档**：FRAMEWORK_CORE_DECISIONS.md  
**重点**：CF-001 → CF-004（宪法基础）  
**时间**：15 分钟

```
显式优于隐式（CF-001）
    ↓
确定性执行（CF-002）
    ↓
启动期失败（CF-003）
    ↓
零依赖（CF-004）
```

### 场景 2：实现新的 Provider
**文档**：FRAMEWORK_INTERFACE_CONTRACTS.md + FRAMEWORK_CODE_PATTERNS.md  
**重点**：
- IProvider 接口规范（IC-2）
- IProvider 实现模式（PA-001, SEC-002）
**时间**：30 分钟

```
1. 读 IProvider 接口定义
2. 查看 AlipayProvider 实现
3. 按 PA-002（四阶段管道）实现
4. 集成 SEC-002（签名验证）
```

### 场景 3：配置多租户系统
**文档**：FRAMEWORK_CORE_DECISIONS.md + FRAMEWORK_CODE_PATTERNS.md  
**重点**：
- MT-001（Realm/Profile）
- MT-002（三层数据模型）
- ISV-002（JIT 解析）
- ISO-001（新商家隔离）
**时间**：45 分钟

```
1. 理解 Realm/Profile 概念（MT-001）
2. 学习三层模型设计（MT-002）
3. 实现 IConfigurationResolver（ISV-002）
4. 应用隔离策略（ISO-001）
```

### 场景 4：性能优化
**文档**：FRAMEWORK_CORE_DECISIONS.md  
**重点**：
- CS-001（混合缓存）
- CS-002（滑动过期）
- CS-003（防护三角）
**时间**：30 分钟

```
配置→ L1 MemoryCache 24h 滑动 + 30d 绝对
   → L2 Redis 30min
   → 负缓存 5min
```

### 场景 5：故障排查
**文档**：FRAMEWORK_INTERFACE_CONTRACTS.md + FRAMEWORK_CODE_PATTERNS.md  
**重点**：
- DIAG-001（诊断码）
- NXC1xx/2xx/3xx 含义
**时间**：10 分钟

```
NXC101 → 缺少 [ApiOperation]
NXC201 → 必填项缺失（投影时）
NXC301 → 响应字段缺失（回填时）
```

---

## 📊 决策分类速查表

### 按优先级

**1 级决策（绝对必须）**：
- CF-001：显式优于隐式
- CF-003：启动期失败
- ED-002：四阶段管道
- MT-001：Realm/Profile

**2 级决策（强烈推荐）**：
- CS-001：混合缓存
- SEC-001：加密字段锁定
- ISO-001：新商家隔离

**3 级决策（可选优化）**：
- DECOUPLE-001：Map 层上移
- CS-002：滑动过期

### 按涉及范围

**架构层面**：
- ED-001：REPR-P 模型
- PA-001：入口出口分离
- DECOUPLE-001：BFF/HttpApi 解耦

**数据模型**：
- MT-001：双层身份
- MT-002：三层数据模型

**性能相关**：
- CS-001：混合缓存
- CS-002：滑动过期
- ISV-002：JIT 解析

**安全相关**：
- SD-001：加密字段
- SD-002：Provider 自决
- SEC-002：签名验证

**可运维性**：
- DIAG-001：诊断码
- CF-003：启动检查

---

## 🔍 按问题类型查询

### 问题：如何实现多租户？
**答案**：MT-001 + MT-002 + ISV-002
1. 使用 Realm/Profile 双层身份识别
2. 在 Redis 中建立三层数据模型
3. 通过 IConfigurationResolver 实现 JIT 加载

### 问题：如何处理配置变更？
**答案**：MT-004 + DIAG-003
1. 使用 Redis Pub/Sub 发送 MappingChange/ConfigChange 消息
2. 不同的消息类型触发不同的缓存清理策略

### 问题：如何保护老商家？
**答案**：ISO-001 + ISO-002 + ISO-003
1. 新商家冷启动 500ms 快速失败
2. 四重防线（Push + Pull + 滑动过期 + 负缓存）
3. 扁平化复合标识符实现 O(1) 隔离

### 问题：如何实现高可用性？
**答案**：CS-001 + CS-002 + CS-003
1. L1/L2 混合缓存
2. 滑动过期 + 绝对过期
3. 防穿透/防击穿/防雪崩三角防护

### 问题：如何实现加密？
**答案**：SD-001 + SEC-001 + SD-003
1. [ApiField] 显式标注加密字段
2. ProtectedPrivateKeyConverter 处理序列化
3. AES-256-GCM 加密存储，版本前缀支持轮换

### 问题：如何快速定位问题？
**答案**：DIAG-001 + CF-003
1. 所有违规都有 NXC 诊断码
2. 启动期全量扫描报告所有错误

---

## 💡 设计原则速记

### 三大宪法原则
```
显式优于隐式（CF-001）
确定性执行（CF-002）
启动期失败（CF-003）
```

### 三个核心约束
```
线程安全（C1）
配置不可变（C2）
异常映射（C3）
```

### 三个隔离维度
```
资源隔离（500ms 快速失败）
时间隔离（滑动过期 + 绝对过期）
配置隔离（扁平化复合 ID）
```

### 三层防护
```
防穿透（负缓存）
防击穿（SemaphoreSlim）
防雪崩（细粒度清理）
```

---

## 📖 文档用途总结

| 文档 | 主要内容 | 阅读时间 | 适合角色 |
|------|--------|--------|--------|
| **CORE_DECISIONS** | 51 项架构决策 | 2 小时 | 架构师、CTO |
| **INTERFACE_CONTRACTS** | 7 大接口规范 | 1 小时 | 开发人员、组件实现者 |
| **CODE_PATTERNS** | 21 项代码模式 | 1.5 小时 | 开发人员、代码审查 |
| **总索引**（本文件） | 快速导航 | 10 分钟 | 所有人 |

---

## 🚀 快速入门推荐阅读顺序

### 对于架构师
1. 本索引（10 min）
2. FRAMEWORK_CORE_DECISIONS.md - CF-001 到 CF-004（20 min）
3. FRAMEWORK_CORE_DECISIONS.md - ED-001 到 ED-003（20 min）
4. FRAMEWORK_CORE_DECISIONS.md - MT-001 到 MT-004（30 min）

### 对于开发人员
1. 本索引（10 min）
2. FRAMEWORK_INTERFACE_CONTRACTS.md - IApiRequest 到 IProvider（30 min）
3. FRAMEWORK_CODE_PATTERNS.md - PA-002（四阶段管道）（20 min）
4. FRAMEWORK_CORE_DECISIONS.md - ISO-001 到 ISO-003（15 min）

### 对于新项目负责人
1. 本索引（10 min）
2. FRAMEWORK_CORE_DECISIONS.md - 全部（2 小时）
3. FRAMEWORK_INTERFACE_CONTRACTS.md - 全部（1 小时）
4. FRAMEWORK_CODE_PATTERNS.md - 相关模式（1 小时）

---

## 📞 常见问题快速查询

| 问题 | 答案文档 | 具体位置 |
|------|--------|---------|
| 什么是 NexusContract？ | CORE_DECISIONS | CF-001~CF-004 |
| 如何实现 IProvider？ | INTERFACE_CONTRACTS + CODE_PATTERNS | IC-2, PA-002 |
| 如何配置多租户？ | CORE_DECISIONS + CODE_PATTERNS | MT-001~004, ISV-002 |
| 如何处理缓存失效？ | CORE_DECISIONS | CS-001~003, MT-004 |
| 如何加密私钥？ | CORE_DECISIONS + CODE_PATTERNS | SD-001, SEC-001 |
| 错误码 NXC101 是什么？ | CORE_DECISIONS | DIAG-001 |
| 如何保护老商家？ | CORE_DECISIONS | ISO-001~003 |

---

**文档生成日期**：2026-01-11  
**版本**：1.0.0-preview.10  
**总覆盖范围**：全代码库全文档全注释

---

## 🎯 核心决策总数统计

| 类别 | 数量 | 文档 |
|-----|------|------|
| 宪法基础 | 4 | CORE_DECISIONS |
| 执行管道 | 3 | CORE_DECISIONS |
| 多租户 ISV | 4 | CORE_DECISIONS |
| 缓存策略 | 3 | CORE_DECISIONS |
| 安全设计 | 3 | CORE_DECISIONS |
| 隔离策略 | 3 | CORE_DECISIONS |
| BFF 解耦 | 3 | CORE_DECISIONS |
| 适配器模式 | 2 | CORE_DECISIONS |
| 诊断系统 | 2 | CORE_DECISIONS |
| 版本演进 | 2 | CORE_DECISIONS |
| 核心价值观 | 3 | CORE_DECISIONS |
| **小计** | **51** | **CORE_DECISIONS** |
| 代码模式 | 21 | CODE_PATTERNS |
| **总计** | **72** | **全文档** |

---

**✨ 这四份文档构成了 NexusContract 框架的完整决策库。**
