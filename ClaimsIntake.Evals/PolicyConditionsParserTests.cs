using ClaimsIntake.Core.Policies;

namespace ClaimsIntake.Evals;

public class PolicyConditionsParserTests
{
    private static readonly IReadOnlyList<PolicyChunk> Chunks =
        PolicyConditionsParser.Parse(File.ReadAllText(Path.Combine("TestData", "vpp-kasko.md")));

    [Fact]
    public void Article_7_has_14_paragraphs()
    {
        var article7 = Chunks.Where(c => c.ArticleNumber == 7).ToList();
        Assert.Equal(14, article7.Count);
    }

    [Fact]
    public void Paragraph_7_3_contains_authorized_driver()
    {
        var chunk = Chunks.Single(c => c.ParagraphNumber == "7.3");
        Assert.Contains("oprávnený vodič", chunk.Text);
    }

    [Fact]
    public void Every_chunk_has_non_empty_text()
    {
        Assert.All(Chunks, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
    }

    [Fact]
    public void First_chunk_is_1_1()
    {
        Assert.Equal("1.1", Chunks[0].ParagraphNumber);
        Assert.Equal(1, Chunks[0].ArticleNumber);
        Assert.Equal("Úvodné ustanovenia", Chunks[0].ArticleTitle);
    }

    [Fact]
    public void Total_chunk_count()
    {
        Assert.Equal(47, Chunks.Count);
    }
}
