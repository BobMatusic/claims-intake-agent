using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ClaimsIntake.McpServer.Tools;

[McpServerToolType]
public static class DiagnosticTools
{
    [McpServerTool, Description("Health check — returns a confirmation that the MCP server is running.")]
    public static string Ping() => "fungujem";
}
