using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ManusReader.Models;

namespace ManusReader.Services;

// Rule-based parser for plain script text where bold role names are marked with
// **asterisks** (as produced by WordReader). Used directly and as a salvage net
// when AI parsing fails on a chunk, so that no source lines are silently lost.
// The rules themselves are WordScriptParser's – this only turns text into lines.
public static partial class TextScriptParser
{
    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldMarker();

    // A bold prefix at the start of a line, optionally followed by ":" then rest.
    [GeneratedRegex(@"^\s*\*\*(?<role>[^*]+?)\*\*\s*:?\s*(?<rest>.*)$")]
    private static partial Regex BoldPrefix();

    // Removes **markers** leaving the inner text.
    private static string StripBold(string text) =>
        BoldMarker().Replace(text, "$1").Trim();

    public static (List<ScriptLine> Lines, HashSet<string> Roles) Parse(string rawText)
    {
        var src = new List<WordScriptParser.WordLine>();
        foreach (var raw in rawText.Split('\n'))
        {
            var line = raw.Trim();
            var plain = StripBold(line);
            if (string.IsNullOrWhiteSpace(plain)) continue;

            var bp = BoldPrefix().Match(line);
            src.Add(bp.Success
                ? new(plain, bp.Groups["role"].Value.Trim(), StripBold(bp.Groups["rest"].Value), false)
                : new(plain, "", plain, false));
        }

        // Text may be a chunk from the middle of a script: no front matter there.
        return WordScriptParser.ParseLines(src, frontMatter: false, unknownBeforeRole: true);
    }
}
