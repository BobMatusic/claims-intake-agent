namespace ClaimsIntake.Core.Models;

public record ToolInvocation
{
    public string Name { get; init; } = string.Empty;
    public string Input { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
}
