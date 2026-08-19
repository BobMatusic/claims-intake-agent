using System.ComponentModel;

namespace ClaimsIntake.Core.Policies;

public record RiskFacts
{
    [Description("Kto viedol vozidlo v čase udalosti a v akom vzťahu je k poistníkovi. Uveď aj to, či mal poistník o jazde vedomosť a či na ňu dal súhlas. Ak hlásenie o vodičovi nič nehovorí, nechaj prázdne.")]
    public string? Driver { get; init; }

    [Description("Údaje o vodičskom oprávnení vodiča — či ho mal, akej skupiny, či nebolo odňaté alebo pozastavené. Ak sa v hlásení nespomína, nechaj prázdne.")]
    public string? DrivingLicence { get; init; }

    [Description("Alkohol, omamné či psychotropné látky alebo lieky u vodiča, vrátane výsledku dychovej skúšky alebo odmietnutia vyšetrenia. Ak sa nespomína, nechaj prázdne.")]
    public string? Intoxication { get; init; }

    [Description("Účel jazdy — súkromná jazda, pracovná cesta, preprava osôb alebo vecí za odplatu, taxislužba, autoškola, prenájom vozidla. Ak sa nespomína, nechaj prázdne.")]
    public string? JourneyPurpose { get; init; }

    [Description("Účasť na pretekoch, súťaži, testovacej alebo tréningovej jazde. Ak sa nespomína, nechaj prázdne.")]
    public string? Competition { get; init; }

    [Description("Technický stav vozidla — platnosť technickej a emisnej kontroly, známe závady, prevádzkyschopnosť. Ak sa nespomína, nechaj prázdne.")]
    public string? VehicleCondition { get; init; }

    [Description("Zabezpečenie vozidla — uzamknutie, alarm, kde boli kľúče a doklady. Relevantné najmä pri odcudzení. Ak sa nespomína, nechaj prázdne.")]
    public string? VehicleSecurity { get; init; }

    [Description("Povaha vzniku škody — vonkajší náraz, živel, požiar, odcudzenie, vandalizmus, alebo naopak opotrebenie, únava materiálu, korózia či výrobná chyba. Ak sa nespomína, nechaj prázdne.")]
    public string? DamageOrigin { get; init; }

    [Description("Krajina a miesto, kde udalosť nastala. Ak sa nespomína, nechaj prázdne.")]
    public string? Location { get; init; }

    [Description("Úmyselné konanie, trestná činnosť alebo útek pred políciou v súvislosti s udalosťou. Ak sa nespomína, nechaj prázdne.")]
    public string? IntentOrCrime { get; init; }
}

public record ExclusionCheckResult
{
    [Description("Zoznam nájdených výluk. Prázdny, ak žiadna nebola identifikovaná.")]
    public IReadOnlyList<ExclusionFinding> Findings { get; init; } = [];
}

public record ExclusionFinding
{
    [Description("Číslo odseku výluky z článku 7, napríklad 7.3.")]
    public required string ParagraphNumber { get; init; }

    [Description("Doslovná veta z hlásenia poistnej udalosti, ktorá túto výluku spúšťa. Skopíruj ju presne z textu hlásenia, neparafrázuj.")]
    public required string EvidenceFromClaim { get; init; }

    [Description("Prečo sa výluka na tento prípad vzťahuje. Jedna až dve vety, tvrdenie, nie podmienka. Ak si nie si istý, nález vôbec neuvádzaj.")]
    public required string Reasoning { get; init; }
}

public record ResolvedExclusion
{
    public required string ParagraphNumber { get; init; }
    public required string ArticleTitle { get; init; }
    public required string EvidenceFromClaim { get; init; }
    public required string Reasoning { get; init; }
    public required string Text { get; init; }
}
