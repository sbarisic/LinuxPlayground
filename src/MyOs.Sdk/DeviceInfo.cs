namespace MyOs.Sdk;

public sealed class DeviceInfo
{
    public DeviceInfo(string name, string deviceClass, string sysPath, string? dev)
    {
        Name = name;
        Class = deviceClass;
        SysPath = sysPath;
        Dev = dev;
    }

    public string Name { get; }

    public string Class { get; }

    public string SysPath { get; }

    public string? Dev { get; }
}
