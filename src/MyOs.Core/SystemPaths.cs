namespace MyOs;

public static class SystemPaths
{
    public const string Dev = "/dev";
    public const string Proc = "/proc";
    public const string Sys = "/sys";
    public const string Run = "/run";
    public const string Tmp = "/tmp";

    public const string System = "/system";
    public const string Apps = "/Apps";

    public const string ServiceManager = "/system/ServiceManager";
    public const string Shell = "/system/Shell";
    public const string DeviceManager = "/system/devd";
    public const string InitControl = "/system/initctl";
    public const string SystemServices = "/system/services";

    public const string MyOsRun = "/run/myos";
    public const string ServiceSocket = "/run/myos/service.sock";
    public const string DeviceSocket = "/run/myos/device.sock";

    public const string Serial = "/dev/ttyS0";
    public const string ProcMounts = "/proc/mounts";
    public const string ProcUptime = "/proc/uptime";
    public const string SysClass = "/sys/class";
}
