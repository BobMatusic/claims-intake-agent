using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace ClaimsIntake.Core.Policies;

public class PolicyConditionsSearch
{
    private const int ResultLimit = 4;
    private static readonly ActivitySource ActivitySource = new("ClaimsIntake");

    private readonly PolicyConditionsIndexer _indexer;

    public PolicyConditionsSearch(PolicyConditionsIndexer indexer)
    {
        _indexer = indexer;
    }

    [Description("Prehľadá celé poistné podmienky a vráti ustanovenia relevantné k zadanému dotazu. Pokrýva územnú platnosť, povinnosti, spôsob plnenia, výluky — celý text podmienok.")]
    public async Task<string> SearchPolicyConditions(
        [Description("Konkrétna otázka alebo fakt na overenie voči podmienkam.")] string query,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.search");
        activity?.SetTag("rag.query", query);
        activity?.SetTag("rag.scope", "all");

        var results = await _indexer.SearchAsync(query, ResultLimit, cancellationToken: cancellationToken);
        SetRetrievalTags(activity, results);

        if (results.Count == 0)
            return "Žiadne relevantné ustanovenie sa nenašlo.";

        return string.Join("\n\n", results.Select(r =>
            $"[{r.Chunk.ParagraphNumber}] Článok {r.Chunk.ArticleNumber} — {r.Chunk.ArticleTitle}\n{r.Chunk.Text}"));
    }

    [Description("Vyhľadá výluky z poistenia relevantné k zadanému rizikovému faktu. Prehľadáva výhradne článok 7 — výluky z poistenia.")]
    public async Task<string> SearchExclusions(
        [Description("Jeden konkrétny rizikový fakt z popisu škody, napríklad 'vozidlo viedla iná osoba než poistník'. Nezadávaj celý popis udalosti.")] string riskFact,
        CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("rag.search");
        activity?.SetTag("rag.query", riskFact);
        activity?.SetTag("rag.scope", "exclusions");

        var results = await _indexer.SearchAsync(riskFact, ResultLimit, articleNumber: 7, cancellationToken: cancellationToken);
        SetRetrievalTags(activity, results);

        if (results.Count == 0)
            return "Žiadna relevantná výluka sa nenašla.";

        return string.Join("\n\n", results.Select(r =>
            $"[{r.Chunk.ParagraphNumber}] Článok {r.Chunk.ArticleNumber} — {r.Chunk.ArticleTitle}\n{r.Chunk.Text}"));
    }

    private static void SetRetrievalTags(Activity? activity, IReadOnlyList<ScoredChunk> results)
    {
        activity?.SetTag("rag.results_count", results.Count);
        if (results.Count == 0) return;

        var scores = results.Select(r => r.Score).ToList();
        activity?.SetTag("rag.score_max", scores[0].ToString("F4", CultureInfo.InvariantCulture));
        activity?.SetTag("rag.score_min", scores[^1].ToString("F4", CultureInfo.InvariantCulture));
        activity?.SetTag("rag.chunks", string.Join(",", results.Select(r => r.Chunk.ParagraphNumber)));
    }
}
