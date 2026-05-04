using ImprivataProxy.Sources.ActiveDirectory;

namespace ImprivataProxy.Sources.Contracts;

/// <summary>
/// ADR-0002 §4.1:抽象 "定期从外部身份目录拉取用户列表" 的能力。
/// 今天由 <see cref="AdSyncRunner"/> 实现(LDAPS 分页 search);
/// 未来可替换成 SCIM 2.0 / Microsoft Graph delta 等实现。
/// </summary>
public interface IUserDirectorySync
{
    Task<SyncResult> RunOnceAsync(CancellationToken ct);
}
