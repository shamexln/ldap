namespace ImprivataProxy.Sources.ActiveDirectory;

public sealed record SyncResult(int Added, int Updated, int Unchanged, int Disabled, long DurationMs);
