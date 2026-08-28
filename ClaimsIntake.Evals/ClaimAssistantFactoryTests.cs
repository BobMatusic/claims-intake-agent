using ClaimsIntake.Core.Agents.Factory;
using ClaimsIntake.Core.Models;
using ClaimsIntake.Core.Policies;
using ClaimsIntake.Core.Services;
using Microsoft.Extensions.AI;

namespace ClaimsIntake.Evals;

/// <summary>
/// The adjuster's assistant must never be able to change the outcome of a claim.
///
/// That guarantee does not come from the system prompt — the model is free to ignore
/// instructions — it comes from the set of tools the factory hands to the agent. Every tool is a
/// read-only lookup, so there is no call the model could make that approves, rejects or writes
/// anything. The boundary holds because of what is absent from the list.
///
/// Nothing in the compiler enforces that. These tests pin the list down, so widening what the
/// assistant can do becomes a deliberate act with a red test in front of it, rather than one
/// more entry that slips through review.
/// </summary>
public class ClaimAssistantFactoryTests
{
    /// <summary>
    /// The exact tools the assistant is allowed to have — read-only lookups only.
    ///
    /// Before adding a name here, confirm the tool cannot mutate state: no approving, rejecting,
    /// creating or updating anything. If it can, it does not belong on the assistant at all —
    /// decisions are made by DecisionEngine in code and by the adjuster at the approval gate.
    /// </summary>
    private static readonly string[] ApprovedReadOnlyTools =
    [
        "GetPolicyDetail",
        "GetClaimsHistory",
        "GetClaimDetail",
        "FindClaimsByVehicle",
        "SearchPolicyConditions",
    ];

    /// <summary>
    /// Name fragments that imply a tool changes something. A heuristic, not a proof — but it
    /// catches the failure mode where someone adds a mutating tool and "fixes" the first test by
    /// pasting the new name into the approved list without thinking about what it does.
    /// </summary>
    private static readonly string[] MutatingVerbs =
    [
        "Approve", "Reject", "Create", "Update", "Delete", "Set", "Write", "Submit", "Pay",
    ];

    [Fact]
    public void Assistant_is_given_exactly_the_approved_read_only_tools()
    {
        var actual = BuildTools().Select(t => t.Name).Order().ToList();

        Assert.Equal(ApprovedReadOnlyTools.Order().ToList(), actual);
    }

    [Fact]
    public void No_assistant_tool_can_change_a_claim()
    {
        foreach (var tool in BuildTools())
        {
            var verb = MutatingVerbs.FirstOrDefault(v =>
                tool.Name.StartsWith(v, StringComparison.Ordinal));

            Assert.True(verb is null,
                $"Tool '{tool.Name}' looks like it mutates state. The assistant answers questions; " +
                "it never decides. Approving and rejecting belong to DecisionEngine and the adjuster.");
        }
    }

    /// <summary>
    /// Tool descriptions are not documentation — they are the prompt the model reads to pick a
    /// tool. A missing [Description] leaves the model guessing from the method name alone.
    /// </summary>
    [Fact]
    public void Every_tool_describes_itself_to_the_model()
    {
        foreach (var tool in BuildTools())
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description),
                $"Tool '{tool.Name}' has no [Description] — the model cannot tell when to call it.");
        }
    }

    /// <summary>
    /// Builds the real tool list the factory would hand to the agent. Only the tool metadata is
    /// inspected, so the dependencies never need to reach Azure — no tool is ever invoked.
    /// </summary>
    private static IList<AITool> BuildTools() =>
        ClaimAssistantFactory.BuildTools(
            new PolicyService(new PolicyData()),
            new PolicyConditionsSearch(new PolicyConditionsIndexer(new UnreachableEmbeddingGenerator())),
            SampleContext);

    private static CaseContext SampleContext => new()
    {
        ContractNumber = "SK7788990011",
        PolicyHolder = "Ján Novák",
        IncidentDate = new DateOnly(2026, 3, 12),
        IncidentDescription = "Nabúral som do stĺpa na parkovisku.",
        VehicleRegistration = "NR-123AB",
        Payout = 400m,
        InvoiceTotal = 500m,
        Deductible = 100m,
        Limit = 8_000m,
        Outcome = ClaimOutcome.RequiresApproval,
        HardBlocks = [],
        SoftSignals = [],
        Exclusions = []
    };

    /// <summary>
    /// Stands in for the embedding model. Building the tool list must never call it — if these
    /// tests ever start hitting Azure, this throws instead of quietly costing money.
    /// </summary>
    private sealed class UnreachableEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Building the tool list must not call the embedding model.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
