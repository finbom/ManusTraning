using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ManusReader.Models;

namespace ManusReader.Services;

public static class WordScriptParser
{
    // Returns true if all letters in the string are uppercase (Swedish included).
    // Allows spaces, digits, and punctuation but no lowercase letters.
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
            if (string.IsNullOrWhiteSpace(para)) continue;

            // Stage direction
            if (IsStageDirection(para))
            {
                lines.Add(new ScriptLine
                {
                    LineNumber = lineNum++,
                    RoleName = "[Scenanvisning]",
                    Text = para,
                    IsStageDirection = true,
                    ShouldRead = false
                });
                continue;
            }

            // "NAME: dialog text" on same line
            var colonIdx = para.IndexOf(':');
            if (colonIdx > 0 && colonIdx < para.Length - 1)
            {
                var candidate = para[..colonIdx].Trim();
                var dialog = para[(colonIdx + 1)..].Trim();
                if (IsAllCaps(candidate) && candidate.Split(' ').Length <= 5)
                {
                    currentRole = candidate;
                    roles.Add(currentRole);
                    lines.Add(new ScriptLine
                    {
                        LineNumber = lineNum++,
                        RoleName = currentRole,
                        Text = dialog,
                        IsStageDirection = false,
                        ShouldRead = true
                    });
                    continue;
                }
            }

            // Pure all-caps line = role name on its own row
            if (IsAllCaps(para) && para.Split(' ').Length <= 5)
            {
                currentRole = para.Trim();
                roles.Add(currentRole);
                continue;
            }

            // Dialog belonging to currentRole
            if (currentRole != null)
            {
                // Append to last line if same role continues, otherwise new line
                if (lines.Count > 0 && lines[^1].RoleName == currentRole && !lines[^1].IsStageDirection)
                    lines[^1].Text += " " + para;
                else
                    lines.Add(new ScriptLine
                    {
                        LineNumber = lineNum++,
                        RoleName = currentRole,
                        Text = para,
                        IsStageDirection = false,
                        ShouldRead = true
                    });
            }
            else
            {
                // Text before any role — treat as stage direction
                lines.Add(new ScriptLine
                {
                    LineNumber = lineNum++,
                    RoleName = "[Scenanvisning]",
                    Text = para,
                    IsStageDirection = true,
                    ShouldRead = false
                });
            }
        }

        return (lines, roles);
    }

    private static List<string> ReadParagraphs(string filePath)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return [];

        return body.Descendants<Paragraph>()
            .Select(p => p.InnerText.Trim())
            .ToList();
    }
}
