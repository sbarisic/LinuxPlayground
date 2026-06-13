namespace MyOs.Sdk;

public sealed class AppRunResult
{
    public AppRunResult(bool success, string name, int? pid, string message)
    {
        Success = success;
        Name = name;
        Pid = pid;
        Message = message;
    }

    public bool Success { get; }

    public string Name { get; }

    public int? Pid { get; }

    public string Message { get; }
}
