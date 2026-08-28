using ClaimsIntake.Core.Agents.Models;

namespace ClaimsIntake.Core.Agents.Interfaces;

public interface IClaimAssistant : IAsyncDisposable
{
    Task<AssistantReplyModel> AskAsync(string question, CancellationToken ct = default);
}
