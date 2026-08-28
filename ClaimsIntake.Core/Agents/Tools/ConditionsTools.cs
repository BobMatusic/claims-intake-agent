using System.ComponentModel;
using ClaimsIntake.Core.Policies;

namespace ClaimsIntake.Core.Agents.Tools;

public class ConditionsTools
{
    private readonly PolicyConditionsSearch _search;

    public ConditionsTools(PolicyConditionsSearch search)
    {
        _search = search;
    }

    [Description("Prehľadá celé poistné podmienky a vráti ustanovenia relevantné k zadanému dotazu. Pokrýva územnú platnosť, povinnosti, spôsob plnenia, výluky — celý text podmienok.")]
    public async Task<string> SearchPolicyConditions(
        [Description("Konkrétna otázka alebo fakt na overenie voči podmienkam, napr. 'územná platnosť v Srbsku' alebo 'povinnosti po nehode'.")] string query,
        CancellationToken cancellationToken = default)
    {
        return await _search.SearchPolicyConditions(query, cancellationToken);
    }
}
