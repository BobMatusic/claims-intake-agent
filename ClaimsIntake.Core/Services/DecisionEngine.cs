using ClaimsIntake.Core.Models;

namespace ClaimsIntake.Core.Services;

public class DecisionEngine
{
    private const decimal HighAmountThreshold = 10_000m;

    public ClaimDecision Evaluate(CaseFile caseFile, PolicyVerification policy, ClaimsHistory history)
    {
        var hardBlocks = new List<string>();
        var softSignals = new List<string>();

        if (caseFile.Report.ContainsSuspiciousInstructions)
            hardBlocks.Add("Dokument obsahuje text, ktorý sa pokúša ovplyvniť spracovanie.");

        if (!policy.IsActive)
            hardBlocks.Add($"Zmluva {caseFile.Report.ContractNumber} nebola nájdená alebo nie je aktívna.");

        if (caseFile.Report.IncidentDate is null)
        {
            hardBlocks.Add("V hlásení chýba dátum vzniku udalosti.");
        }
        else
        {
            if (!policy.CoversDate(caseFile.Report.IncidentDate.Value))
                hardBlocks.Add($"Udalosť z {caseFile.Report.IncidentDate:d.M.yyyy} je mimo platnosti zmluvy.");

            if (history.HasClaimOn(caseFile.Report.IncidentDate.Value))
                hardBlocks.Add("Nárok s rovnakou zmluvou a dátumom udalosti je už evidovaný.");
        }

        if (!policy.CoversClaimType(caseFile.Report.ClaimType))
            hardBlocks.Add($"Produkt zmluvy nekryje typ škody {caseFile.Report.ClaimType}.");

        if (string.IsNullOrWhiteSpace(caseFile.Report.PolicyHolder))
            softSignals.Add("V hlásení chýba meno poistníka.");

        if (string.IsNullOrWhiteSpace(caseFile.Report.IncidentDescription))
            softSignals.Add("V hlásení chýba popis škody.");
        else if (caseFile.Report.IncidentDescription.Length < 20)
            softSignals.Add("Popis škody je príliš stručný na posúdenie.");

        if (caseFile.Invoices.Count == 0)
            softSignals.Add("K hláseniu nie je priložená žiadna faktúra.");

        var unreadableInvoices = caseFile.Invoices.Count(i => i.Amount is null);
        if (unreadableInvoices > 0)
            softSignals.Add($"Pri {unreadableInvoices} faktúrach sa nepodarilo rozpoznať sumu.");

        foreach (var invoice in caseFile.Invoices)
        {
            if (invoice.IssueDate is not null && caseFile.Report.IncidentDate is not null)
            {
                if (invoice.IssueDate < caseFile.Report.IncidentDate)
                    softSignals.Add($"Faktúra {invoice.InvoiceNumber} je vystavená pred dátumom udalosti.");
                else if (invoice.IssueDate > caseFile.Report.IncidentDate.Value.AddDays(90))
                    softSignals.Add($"Faktúra {invoice.InvoiceNumber} je vystavená viac než 90 dní po udalosti.");
            }

            if (!string.IsNullOrWhiteSpace(invoice.VehicleRegistration) &&
                !string.Equals(invoice.VehicleRegistration, caseFile.Report.VehicleRegistration,
                    StringComparison.OrdinalIgnoreCase))
                softSignals.Add($"ŠPZ na faktúre {invoice.InvoiceNumber} nesúhlasí s hlásením.");
        }

        var invoiceTotal = caseFile.Invoices.Where(i => i.Amount.HasValue).Sum(i => i.Amount!.Value);

        if (policy.IsActive)
        {
            if (invoiceTotal > policy.Limit)
                softSignals.Add($"Súčet faktúr {invoiceTotal:N2} € presahuje limit zmluvy {policy.Limit:N2} €.");

            var recentClaims = history.CountWithinMonths(12);
            if (recentClaims >= 3)
                softSignals.Add($"Na zmluve je {recentClaims} nárokov za posledných 12 mesiacov.");
        }

        var limit = policy.IsActive ? policy.Limit : 0m;
        var deductible = policy.IsActive ? policy.Deductible : 0m;
        var payout = policy.IsActive
            ? Math.Max(0m, Math.Min(invoiceTotal, limit) - deductible)
            : 0m;

        if (policy.IsActive && payout > HighAmountThreshold)
            softSignals.Add($"Navrhované plnenie {payout:N2} € presahuje hranicu pre automatické schválenie.");

        var outcome = (hardBlocks.Count > 0 || softSignals.Count > 0)
            ? ClaimOutcome.RequiresApproval
            : ClaimOutcome.AutoApproved;

        return new ClaimDecision
        {
            Outcome = outcome,
            Payout = payout,
            InvoiceTotal = invoiceTotal,
            Deductible = deductible,
            Limit = limit,
            HardBlocks = hardBlocks,
            SoftSignals = softSignals
        };
    }
}
