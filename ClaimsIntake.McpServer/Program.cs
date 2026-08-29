var useStdio = args.Contains("--stdio");
Console.Error.WriteLine($"[MCP] useStdio={useStdio}, args=[{string.Join(", ", args)}]");

if (useStdio)
{
    Console.Error.WriteLine("[MCP] Starting stdio transport...");
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    var host = builder.Build();
    await host.RunAsync();
}
else
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();
    app.MapMcp();
    app.Run();
}
