namespace MyOs.Sdk;

public sealed class ServiceOperationResult
{
    public ServiceOperationResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public bool Success { get; }

    public string Message { get; }
}
