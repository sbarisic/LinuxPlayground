using MyOs.Sdk;

namespace MyOs.Apps.HWorld;

internal static class Program
{
    private static async Task Main()
    {
        Console.WriteLine("Hello from HWorld.");
        Console.WriteLine($"HWorld pid={Environment.ProcessId}");

        try
        {
            ServiceManagerClient serviceManager = new();
            IReadOnlyList<ServiceInfo> services = await serviceManager.ListServicesAsync();
            Console.WriteLine($"HWorld sees {services.Count} service(s).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HWorld could not query services: {ex.Message}");
        }
    }
}
