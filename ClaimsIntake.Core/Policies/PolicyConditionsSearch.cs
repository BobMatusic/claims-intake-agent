using System.ComponentModel;

namespace ClaimsIntake.Core.Policies;

public class PolicyConditionsSearch
{
    private const int ResultLimit = 4;

    private readonly PolicyConditionsIndexer _indexer;

    public PolicyConditionsSearch(PolicyConditionsIndexer indexer)
    {
        _indexer = indexer;
    }

    [Description("Vyhľadá výluky z poistenia relevantné k zadanému rizikovému faktu. Prehľadáva výhradne článok 7 — výluky z poistenia.")]
    public async Task<string> SearchExclusions(
        [Description("Jeden konkrétny rizikový fakt z popisu škody, napríklad 'vozidlo viedla iná osoba než poistník'. Nezadávaj celý popis udalosti.")] string riskFact,
        CancellationToken cancellationToken = default)
    {
        var chunks = await _indexer.SearchAsync(riskFact, ResultLimit, articleNumber: 7, cancellationToken: cancellationToken);

        if (chunks.Count == 0)
            return "Žiadna relevantná výluka sa nenašla.";

        return string.Join("\n\n", chunks.Select(c =>
            $"[{c.ParagraphNumber}] Článok {c.ArticleNumber} — {c.ArticleTitle}\n{c.Text}"));
    }
}
