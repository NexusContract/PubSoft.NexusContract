using System;
using System.Collections.Generic;
using System.Globalization;

namespace NexusContract.Abstractions.Exceptions
{
    /// <summary>
    /// 契约诊断注册表
    /// 
    /// 错误码划分：
    /// - 1xx (NXC1xx): 静态结构红线，由 Validator 在启动时触发
    /// - 2xx (NXC2xx): 运行期守卫，由 Engine 在执行时触发
    /// 
    /// 设计理念：
    /// 错误码唯一确定规则，参数确定现场。当接入 ELK/Sentry 时，可按错误码自动分类和聚合。
    /// </summary>
    public static class ContractDiagnosticRegistry
    {
        // --- 1xx: 静态结构验证（启动时，代码质量问题） ---
        public const string NXC101 = "NXC101"; // 缺失 [ApiOperation]
        public const string NXC102 = "NXC102"; // Operation 标识为空
        public const string NXC103 = "NXC103"; // OneWay 响应类型不是 EmptyResponse
        public const string NXC104 = "NXC104"; // 嵌套深度超过 MaxDepth
        public const string NXC105 = "NXC105"; // 检测到循环引用
        public const string NXC106 = "NXC106"; // 加密字段未显式锁定 Name
        public const string NXC107 = "NXC107"; // 嵌套对象未显式锁定 Name（第 2+ 层）

        // --- 2xx: 运行期执行守卫（执行时，配置/输入问题） ---
        public const string NXC201 = "NXC201"; // 必需字段为 null
        public const string NXC202 = "NXC202"; // 加密字段但 Encryptor 未注入
        public const string NXC203 = "NXC203"; // 投影深度溢出（防御性）

        // --- 3xx: 回填守卫（解析返回值时，脏数据问题） ---
        public const string NXC301 = "NXC301"; // 回填时必需字段缺失
        public const string NXC302 = "NXC302"; // 回填时类型转换失败
        public const string NXC303 = "NXC303"; // 回填时集合大小超限

        // --- 4xx: 硬件层错误（HSM/加密机） ---
        public const string NXC401 = "NXC401"; // HSM 签名超时
        public const string NXC402 = "NXC402"; // HSM 不可用
        public const string NXC403 = "NXC403"; // HSM 配额超限

        // --- 5xx: 框架内部错误（代码生成/编译问题） ---
        public const string NXC501 = "NXC501"; // 表达式树编译失败
        public const string NXC502 = "NXC502"; // 类型转换失败
        public const string NXC503 = "NXC503"; // 反射操作失败
        public const string NXC999 = "NXC999"; // 未知框架错误（兜底）

        // --- 中文模板 (zh-CN) ---
        private static readonly Dictionary<string, string> ZhCnTemplates = new()
        {
            {
                NXC101,
                "[静态红线·NXC101] 类 {0} 必须标注 [ApiOperation] 属性。\n" +
                "  用法: [ApiOperation(\"operation_name\", Interaction = InteractionKind.Request)]"
            },
            {
                NXC102,
                "[静态红线·NXC102] 类 {0} 的 Operation 标识不能为空。\n" +
                "  请在 [ApiOperation(\"your_operation_id\", ...)] 中提供操作标识。"
            },
            {
                NXC103,
                "[静态红线·NXC103] OneWay 操作 {0} 的响应类型必须为 EmptyResponse，但实际为 {1}。\n" +
                "  请将接口改为: public class YourRequest : IApiRequest<EmptyResponse>"
            },
            {
                NXC104,
                "[静态红线·NXC104] 契约嵌套深度超过 {0} 层（物理边界）。路径: {1} → {2}。\n" +
                "  建议: 拆分为多个操作或引入 DTO 中间层以降低复杂度。"
            },
            {
                NXC105,
                "[静态红线·NXC105] 检测到循环引用: {0} → {1}。\n" +
                "  建议: 重新设计数据模型，避免对象间的相互引用。"
            },
            {
                NXC106,
                "[静态红线·NXC106] 属性 {0}.{1} 已标记加密，必须通过 Name 参数显式锁定字段名。\n" +
                "  用法: [ApiField(\"encrypted_field_name\", IsEncrypted = true)]"
            },
            {
                NXC107,
                "[静态红线·NXC107] 嵌套属性 {0}.{1} 处于第 {2} 层（第 2+ 层），必须显式指定 Name 以确保路径锁定。\n" +
                "  用法: [ApiField(\"explicit_field_name\")]"
            },
            {
                NXC201,
                "[运行期·NXC201] 必需字段 {0}.{1} 值为 null，投影被拒绝。\n" +
                "  调用方责任: 确保在构造契约对象时填充所有必需字段。"
            },
            {
                NXC202,
                "[运行期·NXC202] 字段 {0}.{1} 要求加密，但环境中未发现 IEncryptor 实现。\n" +
                "  系统管理: 请在依赖注入容器中注册 IEncryptor 实现。"
            },
            {
                NXC203,
                "[运行期·NXC203] 投影深度溢出（防御性检查）。嵌套层级超过 {0} 层。\n" +
                "  这通常表示契约设计违反了启动时的 Validator 规则，请检查。"
            },
            {
                NXC301,
                "[回填·NXC301] 必需字段 {0}.{1}（映射为 {2}）在三方响应中缺失。\n" +
                "  调查: 确认三方 API 是否应该返回此字段，或协商降级处理。"
            },
            {
                NXC302,
                "[回填·NXC302] 字段 {0}.{1} 的类型转换失败。期待 {2}，但收到值: {3}。\n" +
                "  调查: 检查三方 API 返回值格式，可能需要在 NamingPolicy 或 Decryptor 中补充容错。"
            },
            {
                NXC303,
                "[回填·NXC303] 集合 {0}.{1} 大小超过限制 {2}。\n" +
                "  建议: 启用分页或流式处理大数据集。"
            },
            {
                NXC401,
                "[硬件层·NXC401] 硬件加密机（HSM）签名超时。\n" +
                "  调查: 联系三方服务商检查 HSM 运行状态。"
            },
            {
                NXC402,
                "[硬件层·NXC402] 硬件加密机（HSM）不可用。\n" +
                "  立即告警: 联系三方进行故障排查，可能需要切换备用 HSM。"
            },
            {
                NXC403,
                "[硬件层·NXC403] 硬件加密机（HSM）配额已满（并发签名数达到上限）。\n" +
                "  建议: 降低并发量或申请增加 HSM 实例。"
            },
            {
                NXC501,
                "[框架·NXC501] 表达式树编译失败。\n" +
                "  调查: 检查契约类型是否支持代码生成，可能需要简化类型结构。"
            },
            {
                NXC502,
                "[框架·NXC502] 类型转换失败。\n" +
                "  调查: 检查属性类型是否与预期匹配，可能存在类型不兼容问题。"
            },
            {
                NXC503,
                "[框架·NXC503] 反射操作失败。\n" +
                "  调查: 检查类型定义是否正确，可能存在属性访问权限问题。"
            },
            {
                NXC999,
                "[框架·NXC999] 未知框架错误。\n" +
                "  详情: {0}\n" +
                "  建议: 这是一个未预期的框架内部错误，请联系框架维护者。"
            }
        };

        // --- 英文模板 (en-US) ---
        private static readonly Dictionary<string, string> EnUsTemplates = new()
        {
            {
                NXC101,
                "[Static Redline·NXC101] Type {0} must be marked with [ApiOperation] attribute.\n" +
                "  Usage: [ApiOperation(\"operation_name\", Interaction = InteractionKind.Request)]"
            },
            {
                NXC102,
                "[Static Redline·NXC102] Operation identifier in {0} cannot be empty.\n" +
                "  Please provide operation ID in [ApiOperation(\"your_operation_id\", ...)]."
            },
            {
                NXC103,
                "[Static Redline·NXC103] OneWay operation {0} must return EmptyResponse, but got {1}.\n" +
                "  Change to: public class YourRequest : IApiRequest<EmptyResponse>"
            },
            {
                NXC104,
                "[Static Redline·NXC104] Contract nesting exceeds {0} layers (physical boundary). Path: {1} → {2}.\n" +
                "  Recommendation: Split into multiple operations or introduce DTO intermediate layer."
            },
            {
                NXC105,
                "[Static Redline·NXC105] Circular reference detected: {0} → {1}.\n" +
                "  Recommendation: Redesign data model to avoid mutual references."
            },
            {
                NXC106,
                "[Static Redline·NXC106] Property {0}.{1} is encrypted and must pin a Name explicitly.\n" +
                "  Usage: [ApiField(\"encrypted_field_name\", IsEncrypted = true)]"
            },
            {
                NXC107,
                "[Static Redline·NXC107] Nested property {0}.{1} at layer {2} (2+ layers) must specify Name for path pinning.\n" +
                "  Usage: [ApiField(\"explicit_field_name\")]"
            },
            {
                NXC201,
                "[Runtime·NXC201] Required field {0}.{1} is null, projection rejected.\n" +
                "  Caller responsibility: Ensure all required fields are populated when constructing the contract."
            },
            {
                NXC202,
                "[Runtime·NXC202] Field {0}.{1} requires encryption, but no IEncryptor is registered.\n" +
                "  System admin: Register an IEncryptor implementation in the DI container."
            },
            {
                NXC203,
                "[Runtime·NXC203] Projection depth overflow (defensive check). Nesting exceeds {0} layers.\n" +
                "  This typically indicates a contract design violation, please review."
            },
            {
                NXC301,
                "[Hydration·NXC301] Required field {0}.{1} (mapped as {2}) is missing in third-party response.\n" +
                "  Investigation: Confirm if the third-party API should return this field, or negotiate downgrade handling."
            },
            {
                NXC302,
                "[Hydration·NXC302] Type conversion failed for field {0}.{1}. Expected {2}, but received: {3}.\n" +
                "  Investigation: Check third-party API response format, may need additional fault tolerance in NamingPolicy or Decryptor."
            },
            {
                NXC303,
                "[Hydration·NXC303] Collection {0}.{1} exceeds size limit {2}.\n" +
                "  Recommendation: Enable pagination or stream processing for large datasets."
            },
            {
                NXC401,
                "[Hardware·NXC401] Hardware Security Module (HSM) signing timeout.\n" +
                "  Investigation: Contact third-party service provider to check HSM status."
            },
            {
                NXC402,
                "[Hardware·NXC402] Hardware Security Module (HSM) unavailable.\n" +
                "  Critical Alert: Contact third-party for troubleshooting, consider switching to backup HSM."
            },
            {
                NXC403,
                "[Hardware·NXC403] Hardware Security Module (HSM) quota exceeded (concurrent signing limit reached).\n" +
                "  Recommendation: Reduce concurrency or request additional HSM instances."
            },
            {
                NXC501,
                "[Framework·NXC501] Expression tree compilation failed.\n" +
                "  Investigation: Check if contract type supports code generation, may need to simplify type structure."
            },
            {
                NXC502,
                "[Framework·NXC502] Type conversion failed.\n" +
                "  Investigation: Check if property types match expectations, possible type incompatibility."
            },
            {
                NXC503,
                "[Framework·NXC503] Reflection operation failed.\n" +
                "  Investigation: Check type definition correctness, possible property access permission issues."
            },
            {
                NXC999,
                "[Framework·NXC999] Unknown framework error.\n" +
                "  Details: {0}\n" +
                "  Recommendation: This is an unexpected framework internal error, please contact framework maintainers."
            }
        };

        /// <summary>
        /// 格式化诊断消息
        /// </summary>
        /// <param name="errorCode">错误码（如 NXC101）</param>
        /// <param name="contextArgs">上下文参数（如 类名、属性名、路径等）</param>
        /// <returns>本地化的诊断消息</returns>
        public static string Format(string errorCode, params object[] contextArgs)
        {
            return Format(errorCode, null, contextArgs);
        }

        /// <summary>
        /// 格式化诊断消息（支持显式 CultureInfo）
        /// </summary>
        /// <param name="errorCode">错误码</param>
        /// <param name="culture">目标文化（null 使用当前 UI 文化）</param>
        /// <param name="contextArgs">上下文参数</param>
        /// <returns>本地化的诊断消息</returns>
        public static string Format(string errorCode, CultureInfo? culture, params object[] contextArgs)
        {
            // 1. 优先级：显式指定 > 线程 UI Culture > 默认 zh-CN
            var targetCulture = culture ?? CultureInfo.CurrentUICulture;
            var templates = targetCulture.Name.StartsWith("zh") ? ZhCnTemplates : EnUsTemplates;

            // 2. 如果模板不存在，返回安全的备用信息
            if (!templates.TryGetValue(errorCode, out string? template))
            {
                return $"[Unknown Error {errorCode}] Context: {string.Join(", ", contextArgs ?? Array.Empty<object>())}";
            }

            // 3. 容错：防止参数个数不匹配导致 FormatException
            try
            {
                return string.Format(targetCulture, template, contextArgs);
            }
            catch (FormatException ex)
            {
                // 保留完整现场，而不是让异常链中断
                return $"[Format Error {errorCode}] Template: {template}\n" +
                       $"Args: {string.Join(", ", contextArgs ?? Array.Empty<object>())}\n" +
                       $"Details: {ex.Message}";
            }
        }

        /// <summary>
        /// 获取错误分类（用于日志系统）
        /// </summary>
        public static string GetCategory(string errorCode)
        {
            if (errorCode?.StartsWith("NXC1") == true)
                return "STATIC_STRUCTURE"; // 代码质量问题
            if (errorCode?.StartsWith("NXC2") == true)
                return "RUNTIME_GUARD";    // 执行时问题
            if (errorCode?.StartsWith("NXC3") == true)
                return "HYDRATION_ERROR";  // 回填错误
            if (errorCode?.StartsWith("NXC4") == true)
                return "HARDWARE_ERROR";   // 硬件层错误
            return "UNKNOWN";
        }

        /// <summary>
        /// 获取错误严重度
        /// </summary>
        public static string GetSeverity(string errorCode)
        {
            return errorCode switch
            {
                NXC101 or NXC102 or NXC103 or NXC104 or NXC105 => "CRITICAL",  // 必须修改代码
                NXC106 or NXC107 => "CRITICAL",                                // 安全/结构问题
                NXC201 or NXC202 => "ERROR",                                   // 运行时需要处理
                NXC203 => "WARNING",                                           // 防御检查
                NXC301 or NXC302 or NXC303 => "ERROR",                        // 回填失败需要处理
                NXC401 => "ERROR",                                            // HSM 超时
                NXC402 => "CRITICAL",                                         // HSM 不可用
                NXC403 => "WARNING",                                          // HSM 配额
                NXC501 or NXC502 or NXC503 => "ERROR",                        // 框架内部错误
                NXC999 => "CRITICAL",                                         // 未知框架错误
                _ => "UNKNOWN"
            };
        }
    }
}


