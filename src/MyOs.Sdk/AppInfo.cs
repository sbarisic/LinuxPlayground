namespace MyOs.Sdk;

public sealed class AppInfo
{
    public AppInfo(string name, string id, string version, string kind, string bundle, string entry)
    {
        Name = name;
        Id = id;
        Version = version;
        Kind = kind;
        Bundle = bundle;
        Entry = entry;
    }

    public string Name { get; }

    public string Id { get; }

    public string Version { get; }

    public string Kind { get; }

    public string Bundle { get; }

    public string Entry { get; }
}
