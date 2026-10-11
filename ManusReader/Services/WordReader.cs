using System.Text;

namespace ManusReader.Services;

public static class WordReader
{
    // Returns the document text, one line per visual line (paragraphs are split at
    // line breaks), with a leading bold run wrapped in **asterisks** so that AI
    // models can recognise character names (bold short text at line start).
    public static string ReadText(string filePath)
    {
        var sb = new StringBuilder();
        foreach (var line in WordScriptParser.ReadLines(filePath))
        {
            sb.AppendLine(line.BoldPrefix.Length > 0
                ? $"**{line.BoldPrefix}** {line.Rest}".TrimEnd()
                : line.Text);
        }
        return sb.ToString();
    }
}
