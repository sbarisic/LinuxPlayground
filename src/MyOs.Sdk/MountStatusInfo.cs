namespace MyOs.Sdk;

public sealed class MountStatusInfo
{
    public MountStatusInfo(string path, bool mounted)
    {
        Path = path;
        Mounted = mounted;
    }

    public string Path { get; }

    public bool Mounted { get; }
}
