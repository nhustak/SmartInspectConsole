using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartInspectConsole.Mcp;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        var baseUrl = Environment.GetEnvironmentVariable("SMARTINSPECTCONSOLE_API_BASE_URL")
            ?? "http://127.0.0.1:42331/";

        // MCP stdio must keep stdout reserved for protocol messages only.
        builder.Logging.ClearProviders();

        builder.Services.AddHttpClient<SmartInspectLocalApiClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        });

        builder.Services.AddSingleton<SmartInspectMcpTools>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<SmartInspectMcpTools>();

        await builder.Build().RunAsync();
    }
}
