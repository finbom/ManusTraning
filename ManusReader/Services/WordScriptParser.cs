using System.Collections.Generic;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ManusReader.Models;

namespace ManusReader.Services;

public static class WordScriptParser
{
    private record WordParagraph(string FullText, string BoldPrefix, string TrailingText);

    // Returns true if a run is explicitly bold (handles <w:b/> and <w:b w:val="1"/>).
    private static bool IsBoldRun(Run run)
    {
        var bold = run.RunProperties?.Bold;
        if (bold == null) return false;
        return bold.Val == null || bold.Val.Value;
    }

    // Returns true if all letters in the string are uppercase (Swedish included).
    private static bool IsAllCaps(string text)
    {
        var letters = text.Where(char.IsLetter).ToArray();
        return letters.Length > 0 && letters.Length <= 50 && letters.All(char.IsUpper);
    }

    private static bool IsStageDirection(string text) =>
        text.StartsWith('(') && text.EndsWith(')');

    public static (List<ScriptLine> Lines, HashSet<string> Roles) Parse(string filePath)
    {
        var paragraphs = ReadParagraphs(filePath);
        var lines = new List<ScriptLine>();
        var roles = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        int lineNum = 0;
        string? currentRole = null;

        foreach (var para in paragraphs)
        {
            var text = para.FullText;

            // Stage direction: (text in parens)
            if (IsStageDirection(text))
            {
                lines.Add(new ScriptLine
                {
                    LineNumber = lineNum++,
                    RoleName = "[Scenanvisning]",
                    Text = text,
                    IsStageDirection = true,
                    ShouldRead = false
                });
                continue;
            }

            // Bold prefix = role name (primary detection — takes priority)
            if (!string.IsNullOrEmpty(para.BoldPrefix))
            {
                currentRole = para.BoldPrefix.TrimEnd(':', ' ').Trim().ToUpper();
                roles.Add(currentRole);

                if (!string.IsNullOrEmpty(para.TrailingText))
                {
                    lines.Add(new ScriptLine
                    {
                        LineNumber = lineNum++,
                        RoleName = currentRole,
                        Text = para.TrailingText,
                        ShouldRead = true
                    });
                }
                // If TrailingText is empty the next paragraph is the dialogue.
                continue;
            }

            // "NAME: dialog text" on same line
            var colonIdx = text.IndexOf(':');
            if (colonIdx > 0 && colonIdx < text.Length - 1)
            {
                var candidate = text[..colonIdx].Trim();
                var dialog = text[(colonIdx + 1)..].Trim();
                if (IsAllCaps(candidate) && candidate.Split(' ').Length <= 5)
                {
                    currentRole = candidate;
                    roles.Add(currentRole);
                    lines.Add(new ScriptLine
                    {
                        LineNumber = lineNum++,
                        RoleName = currentRole,
                        Text = dialog,
                        ShouldRead = true
                    });
                    continue;
                }
            }

            // Pure all-caps line = role name on its own row
            if (IsAllCaps(text) && text.Split(' ').Length <= 5)
            {
                currentRole = text.Trim();
                roles.Add(currentRole);
                continue;
            }

            // Dialog belonging to currentRole
            if (currentRole != null)
            {
                if (lines.Count > 0 && lines[^1].RoleName == currentRole && !lines[^1].IsStageDirection)
                    lines[^1].Text += " " + text;
                else
                    lines.Add(new ScriptLine
                    {
                        LineNumber = lineNum++,
                        RoleName = currentRole,
                        Text = text,
                        ShouldRead = true
                    });
            }
            else
            {
                lines.Add(new ScriptLine
                {
                    LineNumber = lineNum++,
                    RoleName = "[Scenanvisning]",
                    Text = text,
                    IsStageDirection = true,
                    ShouldRead = false
                });
            }
        }

        return (lines, roles);
    }

    private static List<WordParagraph> ReadParagraphs(string filePath)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return [];

        var result = new List<WordParagraph>();

        foreach (var para in body.Descendants<Paragraph>())
        {
            var fullText = para.InnerText.Trim();
            if (string.IsNullOrWhiteSpace(fullText)) continue;

            // Collect runs and split into leading bold / trailing rest.
            var boldSb = new StringBuilder();
            var restSb = new StringBuilder();
            bool inBoldSection = true;

            foreach (var run in para.Descendants<Run>())
            {
                var runText = run.InnerText;
                if (string.IsNullOrEmpty(runText)) continue;

                if (inBoldSection && IsBoldRun(run))
                    boldSb.Append(runText);
                else
                {
                    inBoldSection = false;
                    restSb.Append(runText);
                }
            }

            var boldText = boldSb.ToString().Trim();
            // Strip leading colon/space from the trailing part (e.g. ": dialog text")
            var restText = restSb.ToString().Trim().TrimStart(':').Trim();

            // Only treat bold prefix as a role name if it is short (1–4 words).
            var wordCount = boldText.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length;
            if (!string.IsNullOrEmpty(boldText) && wordCount <= 4)
                result.Add(new WordParagraph(fullText, boldText, restText));
            else
                result.Add(new WordParagraph(fullText, "", fullText));
        }

        return result;
    }
}
