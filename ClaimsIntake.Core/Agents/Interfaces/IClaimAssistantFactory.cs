using ClaimsIntake.Core.Models;

namespace ClaimsIntake.Core.Agents.Interfaces;

public interface IClaimAssistantFactory
{
    Task<IClaimAssistant> CreateAsync(CaseContext caseContext, CancellationToken ct = default);
}
