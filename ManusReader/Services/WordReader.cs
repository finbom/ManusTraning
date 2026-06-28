using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ManusReader.Services;

public static class WordReader
{
    // Returns the document text with bold runs wrapped in **asterisks** so that
    // AI models can recognise character names (bold short text at line start).
    public static string ReadText(string filePath)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return "";

        var sb = new StringBuilder();
        foreach (var para in body.Descendants<Paragraph>())
        {
            var boldSb = new StringBuilder();  // accumulate consecutive bold text
            var lineSb = new StringBuilder();

            foreach (var run in para.Descendants<Run>())
            {
                var text = run.InnerText;
                if (string.IsNullOrEmpty(text)) continue;

                if (IsBoldRun(run))
                {
                    boldSb.Append(text);
                }
                else
                {
                    if (boldSb.Length > 0)
                    {
                        lineSb.Append($"**{boldSb}**");
                        boldSb.Clear();
                    }
                    lineSb.Append(text);
                }
            }

            // Flush any remaining bold text at end of paragraph
            if (boldSb.Length > 0)
                lineSb.Append($"**{boldSb}**");

            var line = lineSb.ToString().Trim();
            if (!string.IsNullOrEmpty(line))
                sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private static bool IsBoldRun(Run run)
    {
        var bold = run.RunProperties?.Bold;
        if (bold == null) return false;
        return bold.Val == null || bold.Val.Value;
    }
}
