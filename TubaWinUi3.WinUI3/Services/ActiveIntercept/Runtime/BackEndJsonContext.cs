using System.Text.Json.Serialization;
using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// NativeAOT 下 System.Text.Json 使用反射序列化会被裁剪掉，
/// 必须用源生成上下文。所有序列化/反序列化统一走 JsonSerializerContext。
/// </summary>
[JsonSerializable(typeof(InterceptStateFile))]
[JsonSerializable(typeof(InterceptStateEntry))]
[JsonSerializable(typeof(InterceptEvent))]
[JsonSerializable(typeof(RegistryValueBackup))]
[JsonSerializable(typeof(BackendConfig))]
[JsonSerializable(typeof(TrustPolicyFile))]
[JsonSerializable(typeof(TrustPolicyEntry))]
[JsonSerializable(typeof(NotificationRequest))]
[JsonSerializable(typeof(IgnoreFile))]
[JsonSerializable(typeof(IgnoreEntry))]
[JsonSerializable(typeof(NotifyStateFile))]
[JsonSerializable(typeof(NotifyStateEntry))]
[JsonSerializable(typeof(List<InterceptEvent>))]
[JsonSerializable(typeof(List<RegistryValueBackup>))]
[JsonSerializable(typeof(List<string>))]
// ---- 命名管道契约（与主程序对齐；AOT 源生成必需）----
[JsonSerializable(typeof(InterceptPipeEnvelope))]
[JsonSerializable(typeof(InterceptPipeRequest))]
[JsonSerializable(typeof(InterceptPipeResponse))]
[JsonSerializable(typeof(InterceptBackendNotification))]
[JsonSerializable(typeof(InterceptSnapshot))]
[JsonSerializable(typeof(InterceptItemDto))]
[JsonSerializable(typeof(InterceptEventDto))]
[JsonSerializable(typeof(InterceptIgnoredDto))]
[JsonSerializable(typeof(List<InterceptItemDto>))]
[JsonSerializable(typeof(List<InterceptEventDto>))]
[JsonSerializable(typeof(List<InterceptIgnoredDto>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(System.Collections.Generic.List<System.Guid>))]
internal sealed partial class BackEndJsonContext : JsonSerializerContext
{
}
