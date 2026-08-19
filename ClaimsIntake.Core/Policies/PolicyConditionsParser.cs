using System.Text;
using System.Text.RegularExpressions;

namespace ClaimsIntake.Core.Policies;

public static partial class PolicyConditionsParser
{
    [GeneratedRegex(@"^##\s+Článok\s+(\d+)\s+—\s+(.+)$")]
    private static partial Regex ArticleHeaderPattern();

    [GeneratedRegex(@"^\*\*(\d+\.\d+)\*\*\s*(.*)$")]
    private static partial Regex ParagraphStartPattern();

    public static IReadOnlyList<PolicyChunk> Parse(string markdown)
    {
        var chunks = new List<PolicyChunk>();

        var currentArticleNumber = 0;
        var currentArticleTitle = string.Empty;

        string? currentParagraphNumber = null;
        var currentText = new StringBuilder();

        void FlushParagraph()
        {
            if (currentParagraphNumber is null)
                return;

            var text = currentText.ToString().Trim();
            if (text.Length > 0)
            {
                chunks.Add(new PolicyChunk
                {
                    ParagraphNumber = currentParagraphNumber,
                    ArticleNumber = currentArticleNumber,
                    ArticleTitle = currentArticleTitle,
                    Text = text
                });
            }

            currentParagraphNumber = null;
            currentText.Clear();
        }

        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.TrimEnd();

            var articleMatch = ArticleHeaderPattern().Match(trimmed);
            if (articleMatch.Success)
            {
                FlushParagraph();
                currentArticleNumber = int.Parse(articleMatch.Groups[1].Value);
                currentArticleTitle = articleMatch.Groups[2].Value.Trim();
                continue;
            }

            var paragraphMatch = ParagraphStartPattern().Match(trimmed);
            if (paragraphMatch.Success)
            {
                FlushParagraph();
                currentParagraphNumber = paragraphMatch.Groups[1].Value;
                currentText.AppendLine(paragraphMatch.Groups[2].Value);
                continue;
            }

            if (currentParagraphNumber is not null && trimmed != "---")
                currentText.AppendLine(trimmed);
        }

        FlushParagraph();
        return chunks;
    }
}
