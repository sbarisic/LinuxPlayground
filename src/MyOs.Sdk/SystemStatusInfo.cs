namespace MyOs.Sdk;

public sealed class SystemStatusInfo
{
    public SystemStatusInfo(
        double uptimeSeconds,
        int totalServices,
        int runningServices,
        IReadOnlyList<MountStatusInfo> mounts)
    {
        UptimeSeconds = uptimeSeconds;
        TotalServices = totalServices;
        RunningServices = runningServices;
        Mounts = mounts;
    }

    public double UptimeSeconds { get; }

    public int TotalServices { get; }

    public int RunningServices { get; }

    public IReadOnlyList<MountStatusInfo> Mounts { get; }
}
