namespace MyOs.Sdk;

public sealed class ServiceInfo
{
    public ServiceInfo(
        string name,
        string exec,
        string state,
        int? pid,
        string restart,
        bool critical,
        string? lastExit)
    {
        Name = name;
        Exec = exec;
        State = state;
        Pid = pid;
        Restart = restart;
        Critical = critical;
        LastExit = lastExit;
    }

    public string Name { get; }

    public string Exec { get; }

    public string State { get; }

    public int? Pid { get; }

    public string Restart { get; }

    public bool Critical { get; }

    public string? LastExit { get; }
}
