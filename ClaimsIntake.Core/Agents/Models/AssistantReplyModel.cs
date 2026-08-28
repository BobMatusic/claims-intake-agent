namespace ClaimsIntake.Core.Agents.Models;

public record AssistantReplyModel
{
    public required string Text { get; init; }
    public required IReadOnlyList<string> ToolsUsed { get; init; }
    public required string AgentName { get; init; }
}
