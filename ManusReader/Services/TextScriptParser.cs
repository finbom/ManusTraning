using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ManusReader.Models;

namespace ManusReader.Services;

// Rule-based parser for plain script text where bold role names are marked with
// **asterisks** (as produced by WordReader). Used directly and as a salvage net
// when AI parsing fails on a chunk, so that no source lines are silently lost.
public static partial class TextScriptParser
{
    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldMarker();

    // A bold prefix at the start of a line, optionally followed by ":" then rest.
    [GeneratedRegex(@"^\s*\*\*(?<role>[^*]+?)\*\*\s*:?\s*(?<rest>.*)$")]
    private static partial Regex BoldPrefix();

    private static bool IsAllCaps(string text)
    {
        var letters = text.Where(char.IsLetter).ToArray();
        return letters.Length > 0 && letters.Length <= 50 && letters.All(char.IsUpper);
    }

    private static bool IsStageDirection(string text) =>
        text.StartsWith('(') && text.EndsWith(')');

    // Removes **markers** leaving the inner text.
    private static string StripBold(string text) =>
        BoldMarker().Replace(text, "$1").Trim();

    public static (List<ScriptLine> Lines, HashSet<string> Roles) Parse(string rawText)
    {
        var lines = new List<ScriptLine>();
        var roles = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        int lineNum = 0;
        string? currentRole = null;

        foreach (var raw in rawText.Split('\n'))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var plain = StripBold(line);
            if (string.IsNullOrWhiteSpace(plain)) continue;

            // Stage direction: (text in parens)
            if (IsStageDirection(plain))
            {
                lines.Add(new ScriptLine
                {
                    LineNumber = lineNum++,
                    RoleName = "[Scenanvisning]",
                    Text = plain,
                    IsStageDirection = true,
                    ShouldRead = false
                });
                continue;
            }

            // Bold prefix = role name (primary detection)
            var bp = BoldPrefix().Match(line);
            if (bp.Success)
            {
                var role = bp.Groups["role"].Value.Trim();
                var wordCount = role.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length;
                if (wordCount is >= 1 and <= 4)
                {
                    currentRole = role.TrimEnd(':', ' ').ToUpper();
                    roles.Add(currentRole);

                    var rest = bp.Groups["rest"].Value.Trim();
                    if (!string.IsNullOrEmpty(rest))
                        lines.Add(NewDialog(ref lineNum, currentRole, StripBold(rest)));
                    continue;
                }
            }

            // "NAME: dialog text" on same line
            var colonIdx = plain.IndexOf(':');
            if (colonIdx > 0 && colonIdx < plain.Length - 1)
            {
                var candidate = plain[..colonIdx].Trim();
                var dialog = plain[(colonIdx + 1)..].Trim();
                if (IsAllCaps(candidate) && candidate.Split(' ').Length <= 5)
                {
                    currentRole = candidate;
                    roles.Add(currentRole);
                    lines.Add(NewDialog(ref lineNum, currentRole, dialog));
                    continue;
                }
            }

            // Pure all-caps line = role name on its own row
            if (IsAllCaps(plain) && plain.Split(' ').Length <= 5)
            {
                currentRole = plain;
                roles.Add(currentRole);
                continue;
            }

            // Dialog belonging to currentRole
            if (currentRole != null)
            {
                if (lines.Count > 0 && lines[^1].RoleName == currentRole && !lines[^1].IsStageDirection)
                    lines[^1].Text += " " + plain;
                else
                    lines.Add(NewDialog(ref lineNum, currentRole, plain));
            }
            else
            {
                // Text before any role is recognised — keep it, flagged as unknown
                // so the user can assign a role instead of losing the line.
                lines.Add(new ScriptLine
                {
                    LineNumber = lineNum++,
                    RoleName = "unknown",
                    Text = plain,
                    IsUnknown = true,
                    ShouldRead = false
                });
            }
        }

        return (lines, roles);
    }

    private static ScriptLine NewDialog(ref int lineNum, string role, string text) => new()
    {
        LineNumber = lineNum++,
        RoleName = role,
        Text = text,
        ShouldRead = true
    };
}
