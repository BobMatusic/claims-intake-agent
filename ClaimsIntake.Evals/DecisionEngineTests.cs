using ClaimsIntake.Core.Models;
using ClaimsIntake.Core.Services;

namespace ClaimsIntake.Evals;

public class DecisionEngineTests
{
    private static readonly DecisionEngine Engine = new();

    private static CaseFile MakeCase(
        string? contractNumber = "SK123",
        string? policyHolder = "Ján Novák",
        DateOnly? incidentDate = null,
        string? description = "Nabúral som do stĺpa na parkovisku.",
        bool suspicious = false,
        List<Invoice>? invoices = null)
    {
        return new CaseFile
        {
            Report = new ClaimReport
            {
                ContractNumber = contractNumber,
                PolicyHolder = policyHolder,
                IncidentDate = incidentDate ?? new DateOnly(2026, 3, 12),
                IncidentDescription = description,
                ClaimType = "AUTO",
                ContainsSuspiciousInstructions = suspicious
            },
            Invoices = invoices ?? []
        };
    }

    private static PolicyVerification ActivePolicy => new()
    {
        ContractNumber = "SK123",
        IsActive = true,
        CoveredFrom = new DateOnly(2020, 1, 1),
        CoveredUntil = new DateOnly(2030, 12, 31),
        Limit = 8_000m,
        Deductible = 100m
    };

    private static PolicyVerification InactivePolicy => new()
    {
        ContractNumber = "XX999", IsActive = false
    };

    private static ClaimsHistory EmptyHistory => new()
    {
        ContractNumber = "SK123"
    };

    [Fact]
    public void Complete_claim_without_signals_is_auto_approved()
    {
        var invoices = new List<Invoice> { new() { Amount = 500m } };
        var decision = Engine.Evaluate(MakeCase(invoices: invoices), ActivePolicy, EmptyHistory);

        Assert.Equal(ClaimOutcome.AutoApproved, decision.Outcome);
        Assert.Empty(decision.HardBlocks);
        Assert.Empty(decision.SoftSignals);
    }

    [Fact]
    public void Inactive_policy_is_hard_block()
    {
        var decision = Engine.Evaluate(MakeCase(), InactivePolicy, EmptyHistory);

        Assert.Equal(ClaimOutcome.Escalated, decision.Outcome);
        Assert.Contains(decision.HardBlocks, b => b.Contains("aktívna", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Suspicious_document_is_hard_block()
    {
        var decision = Engine.Evaluate(MakeCase(suspicious: true), ActivePolicy, EmptyHistory);

        Assert.Equal(ClaimOutcome.Escalated, decision.Outcome);
        Assert.Contains(decision.HardBlocks, b => b.Contains("ovplyvniť", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Suspicious_and_inactive_are_both_hard_blocks()
    {
        var decision = Engine.Evaluate(MakeCase(suspicious: true), InactivePolicy, EmptyHistory);

        Assert.Equal(ClaimOutcome.Escalated, decision.Outcome);
        Assert.True(decision.HardBlocks.Count >= 2);
    }

    [Fact]
    public void Missing_date_is_hard_block()
    {
        var cf = MakeCase(incidentDate: new DateOnly(1, 1, 1));
        cf = cf with
        {
            Report = cf.Report with { IncidentDate = null }
        };

        var decision = Engine.Evaluate(cf, ActivePolicy, EmptyHistory);

        Assert.Equal(ClaimOutcome.Escalated, decision.Outcome);
        Assert.Contains(decision.HardBlocks, b => b.Contains("dátum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void One_signal_requires_approval()
    {
        var invoices = new List<Invoice> { new() { Amount = 500m } };
        var decision = Engine.Evaluate(MakeCase(policyHolder: null, invoices: invoices), ActivePolicy, EmptyHistory);

        Assert.Equal(ClaimOutcome.RequiresApproval, decision.Outcome);
        Assert.Single(decision.SoftSignals);
        Assert.Contains(decision.SoftSignals, s => s.Contains("meno", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Two_signals_require_approval()
    {
        var invoices = new List<Invoice> { new() { Amount = 500m } };
        var decision = Engine.Evaluate(MakeCase(policyHolder: null, description: "škoda", invoices: invoices), ActivePolicy, EmptyHistory);

        Assert.Equal(ClaimOutcome.RequiresApproval, decision.Outcome);
        Assert.True(decision.SoftSignals.Count >= 2);
        Assert.Contains(decision.SoftSignals, s => s.Contains("meno", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision.SoftSignals, s => s.Contains("stručný", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_invoice_is_soft_signal()
    {
        var decision = Engine.Evaluate(MakeCase(), ActivePolicy, EmptyHistory);

        Assert.Contains(decision.SoftSignals, s => s.Contains("faktúra", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Payout_is_invoice_total_minus_deductible()
    {
        var invoices = new List<Invoice>
        {
            new() { Amount = 300m },
            new() { Amount = 200m }
        };
        var decision = Engine.Evaluate(MakeCase(invoices: invoices), ActivePolicy, EmptyHistory);

        Assert.Equal(500m, decision.InvoiceTotal);
        Assert.Equal(400m, decision.Payout);
        Assert.Equal(100m, decision.Deductible);
    }

    [Fact]
    public void Payout_cannot_be_negative()
    {
        var invoices = new List<Invoice> { new() { Amount = 50m } };
        var decision = Engine.Evaluate(MakeCase(invoices: invoices), ActivePolicy, EmptyHistory);

        Assert.Equal(0m, decision.Payout);
    }

    [Fact]
    public void Payout_is_capped_by_limit()
    {
        var policy = ActivePolicy with { Limit = 1000m };
        var invoices = new List<Invoice> { new() { Amount = 5000m } };

        var decision = Engine.Evaluate(MakeCase(invoices: invoices), policy, EmptyHistory);

        Assert.Equal(900m, decision.Payout);
    }

    [Fact]
    public void High_claim_frequency_is_soft_signal()
    {
        var invoices = new List<Invoice> { new() { Amount = 500m } };
        var history = new ClaimsHistory { ContractNumber = "SK123", ClaimsInLastYear = 4 };

        var decision = Engine.Evaluate(MakeCase(invoices: invoices), ActivePolicy, history);

        Assert.Contains(decision.SoftSignals, s => s.Contains("nárokov", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_claim_is_hard_block()
    {
        var claimDate = new DateOnly(2026, 3, 12);
        var history = new ClaimsHistory
        {
            ContractNumber = "SK123",
            ClaimDates = [claimDate]
        };

        var decision = Engine.Evaluate(MakeCase(incidentDate: claimDate), ActivePolicy, history);

        Assert.Equal(ClaimOutcome.Escalated, decision.Outcome);
        Assert.Contains(decision.HardBlocks, b => b.Contains("evidovaný", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Registration_plate_mismatch_between_report_and_invoice()
    {
        var invoices = new List<Invoice>
        {
            new() { Amount = 500m, InvoiceNumber = "F001", VehicleRegistration = "BA999ZZ" }
        };
        var cf = MakeCase(invoices: invoices) with
        {
            Report = MakeCase().Report with { VehicleRegistration = "BA111AA" }
        };

        var decision = Engine.Evaluate(cf, ActivePolicy, EmptyHistory);

        Assert.Contains(decision.SoftSignals, s => s.Contains("ŠPZ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Invoice_before_incident_date_is_soft_signal()
    {
        var incidentDate = new DateOnly(2026, 3, 12);
        var invoices = new List<Invoice>
        {
            new() { Amount = 500m, InvoiceNumber = "F001", IssueDate = new DateOnly(2026, 2, 1) }
        };

        var decision = Engine.Evaluate(MakeCase(incidentDate: incidentDate, invoices: invoices), ActivePolicy, EmptyHistory);

        Assert.Contains(decision.SoftSignals, s => s.Contains("pred dátumom", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Invoice_after_90_days_is_soft_signal()
    {
        var incidentDate = new DateOnly(2026, 3, 12);
        var invoices = new List<Invoice>
        {
            new() { Amount = 500m, InvoiceNumber = "F001", IssueDate = new DateOnly(2026, 7, 1) }
        };

        var decision = Engine.Evaluate(MakeCase(incidentDate: incidentDate, invoices: invoices), ActivePolicy, EmptyHistory);

        Assert.Contains(decision.SoftSignals, s => s.Contains("90 dní", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(null, "popis", "chýba meno")]
    [InlineData("Ján", "x", "stručný popis")]
    [InlineData("Ján", "popis", "chýba faktúra")]
    public void Signal_must_never_be_auto_approved(string? holder, string description, string reason)
    {
        var invoices = description == "popis"
            ? new List<Invoice>()
            : new List<Invoice> { new() { Amount = 500m } };

        var decision = Engine.Evaluate(
            MakeCase(policyHolder: holder, description: description, invoices: invoices),
            ActivePolicy,
            EmptyHistory);

        Assert.True(decision.SoftSignals.Count > 0, $"Očakávaný signál: {reason}");
        Assert.NotEqual(ClaimOutcome.AutoApproved, decision.Outcome);
    }
}
