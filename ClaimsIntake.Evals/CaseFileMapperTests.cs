using ClaimsIntake.Core.Extraction;

namespace ClaimsIntake.Evals;

public class CaseFileMapperTests
{
    [Theory]
    [InlineData("12.3.2026", 2026, 3, 12)]
    [InlineData("12. 3. 2026", 2026, 3, 12)]
    [InlineData("01.01.2025", 2025, 1, 1)]
    [InlineData("2026-03-12", 2026, 3, 12)]
    public void Parses_date(string raw, int year, int month, int day)
    {
        var result = CaseFileMapper.TryParseDate(raw);

        Assert.NotNull(result);
        Assert.Equal(new DateOnly(year, month, day), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("včera")]
    [InlineData("nejaký nezmysel")]
    public void Invalid_date_returns_null(string? raw)
    {
        Assert.Null(CaseFileMapper.TryParseDate(raw));
    }

    [Fact]
    public void Maps_report_with_normalized_registration_plate()
    {
        var extraction = new CaseFileExtraction
        {
            Report = new ClaimReportExtraction
            {
                ContractNumber = " SK1234567890 ",
                VehicleRegistration = "BA - 123 AB",
                ClaimType = "auto"
            }
        };

        var caseFile = CaseFileMapper.Map(extraction);

        Assert.Equal("SK1234567890", caseFile.Report.ContractNumber);
        Assert.Equal("BA123AB", caseFile.Report.VehicleRegistration);
        Assert.Equal("AUTO", caseFile.Report.ClaimType);
    }

    [Fact]
    public void Maps_invoices()
    {
        var extraction = new CaseFileExtraction
        {
            Report = new ClaimReportExtraction(),
            Invoices = [
                new InvoiceExtraction
                {
                    InvoiceNumber = "F2026001",
                    Supplier = "Autoservis ABC",
                    IssueDateRaw = "15.3.2026",
                    Amount = 450.00m,
                    WorkDescription = "Výmena nárazníka"
                }
            ]
        };

        var caseFile = CaseFileMapper.Map(extraction);

        Assert.Single(caseFile.Invoices);
        var inv = caseFile.Invoices[0];
        Assert.Equal("F2026001", inv.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 3, 15), inv.IssueDate);
        Assert.Equal(450.00m, inv.Amount);
    }

    [Fact]
    public void Empty_extraction_returns_empty_report()
    {
        var extraction = new CaseFileExtraction { Report = null };

        var caseFile = CaseFileMapper.Map(extraction);

        Assert.NotNull(caseFile.Report);
        Assert.Empty(caseFile.Invoices);
    }
}
