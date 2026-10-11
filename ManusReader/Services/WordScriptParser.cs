using System.Collections.Generic;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ManusReader.Models;
using static ManusReader.Services.ScriptHeuristics;

namespace ManusReader.Services;

public static partial class WordScriptParser
{
    // One visual line of the document. Scripts often keep a whole speech in one
    // paragraph – role name, direction and dialogue separated by line breaks
    // (Shift+Enter) – so paragraphs are split at <w:br/> into lines.
    //   BoldPrefix: leading bold text (a role-name candidate), Rest: what follows.
    //   AllItalic:  every character is italic – scripts set stage directions so.
    public record WordLine(string Text, string BoldPrefix, string Rest, bool AllItalic);

    // Returns true if a run is explicitly bold (handles <w:b/> and <w:b w:val="1"/>).
    private static bool IsBoldRun(Run run)
    {
        var bold = run.RunProperties?.Bold;
        if (bold == null) return false;
        return bold.Val == null || bold.Val.Value;
    }

    private static bool IsItalicRun(Run run)
    {
        var italic = run.RunProperties?.Italic;
        if (italic == null) return false;
        return italic.Val == null || italic.Val.Value;
    }

    public static (List<ScriptLine> Lines, HashSet<string> Roles) Parse(string filePath) =>
        ParseLines(ReadLines(filePath));

    // The parsing rules, shared with TextScriptParser (which feeds it lines of
    // AI-style text). frontMatter: treat lines before the first act/scene heading
    // as title page/cast list. unknownBeforeRole: text before any role becomes an
    // orange "unknown" line instead of a direction (used for AI salvage).
    public static (List<ScriptLine> Lines, HashSet<string> Roles) ParseLines(
        List<WordLine> src, bool frontMatter = true, bool unknownBeforeRole = false)
    {
        var lines = new List<ScriptLine>();
        var roles = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        int lineNum = 0;
        string? currentRole = null;

        bool IsDirection(WordLine l) => IsStageDirection(l.Text) || l.AllItalic;

        // A name on its own line is only a role if dialogue follows (possibly after
        // directions such as "(sjunger A cappella)"). This rejects headings like
        // "VITA HUSET I WASHINGTON", which are followed by a direction and a role.
        bool FollowedByDialogue(int i)
        {
            for (int j = i + 1; j < src.Count; j++)
            {
                if (IsDirection(src[j])) continue;
                return src[j].BoldPrefix.Length == 0 || src[j].Rest.Length > 0;
            }
            return false;
        }

        // Title page and cast list ("ANNIE 11 år, …", "DRAKE, butler") come before
        // the first act/scene heading. Show them, but don't parse them as dialogue.
        var scriptStart = frontMatter ? FirstHeadingIndex(src) : 0;

        // Scripts with bold role names set lyrics in plain caps – then a plain caps
        // line must not be taken for a role.
        var usesBoldRoles = src.Count(l => l.BoldPrefix.Length > 0 && LooksLikeRoleName(l.BoldPrefix)) >= 2;

        void AddDirection(string text) => lines.Add(new ScriptLine
        {
            LineNumber = lineNum++,
            RoleName = "[Scenanvisning]",
            Text = text,
            IsStageDirection = true,
            ShouldRead = false
        });

        void AddDialog(string role, string text)
        {
            if (lines.Count > 0 && lines[^1].RoleName == role && !lines[^1].IsStageDirection)
                lines[^1].Text += " " + text;
            else
                lines.Add(new ScriptLine { LineNumber = lineNum++, RoleName = role, Text = text, ShouldRead = true });
        }

        for (int i = 0; i < src.Count; i++)
        {
            var l = src[i];
            var text = l.Text;

            if (IsDirection(l) || i < scriptStart)
            {
                AddDirection(text);
                continue;
            }

            // Bold prefix = role name (primary detection — takes priority)
            if (l.BoldPrefix.Length > 0)
            {
                if (LooksLikeRoleName(l.BoldPrefix) && (l.Rest.Length > 0 || FollowedByDialogue(i)))
                {
                    currentRole = l.BoldPrefix.TrimEnd(':', ' ').Trim().ToUpper();
                    roles.Add(currentRole);
                    if (l.Rest.Length > 0) AddDialog(currentRole, l.Rest);
                    // Otherwise the next line is the dialogue.
                    continue;
                }

                if (l.Rest.Length == 0)
                {
                    // A bold line that isn't a role: a heading ("SCEN 2:3", song
                    // number). Show it, don't read it, and start afresh after it.
                    AddDirection(text);
                    currentRole = null;
                    continue;
                }
            }

            // "NAME: dialog text" on same line
            var colonIdx = text.IndexOf(':');
            if (colonIdx > 0 && colonIdx < text.Length - 1)
            {
                var candidate = text[..colonIdx].Trim();
                var dialog = text[(colonIdx + 1)..].Trim();
                if (IsAllCaps(candidate) && LooksLikeRoleName(candidate))
                {
                    currentRole = candidate;
                    roles.Add(currentRole);
                    AddDialog(currentRole, dialog);
                    continue;
                }
            }

            // Pure all-caps line = role name on its own row
            if (!usesBoldRoles && IsAllCaps(text) && LooksLikeRoleName(text) && FollowedByDialogue(i))
            {
                currentRole = text.Trim();
                roles.Add(currentRole);
                continue;
            }

            // Dialog (or song lyrics) belonging to currentRole
            if (currentRole != null)
                AddDialog(currentRole, text);
            else if (unknownBeforeRole)
                lines.Add(new ScriptLine
                {
                    LineNumber = lineNum++, RoleName = "unknown", Text = text, IsUnknown = true, ShouldRead = false
                });
            else
                AddDirection(text);
        }

        return (lines, roles);
    }

    // Index of the first "AKT 1" / "SCEN 1:1" / "PROLOG" heading, or 0 if the
    // document has none (then everything is script).
    private static int FirstHeadingIndex(List<WordLine> src)
    {
        var idx = src.FindIndex(l => IsActOrSceneHeading(l.Text));
        return idx < 0 ? 0 : idx;
    }

    // Ages from a cast list before the script: a bold name followed by "11 år".
    // Returns role name → age, keyed both by the full name and, when unique, by
    // its last word ("MISS AGATHA HANNIGAN" → also "HANNIGAN").
    public static Dictionary<string, int> ReadCastAges(string filePath)
    {
        var src = ReadLines(filePath);
        var end = FirstHeadingIndex(src);
        var ages = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var l in src.Take(end))
        {
            if (l.BoldPrefix.Length == 0 || !LooksLikeRoleName(l.BoldPrefix)) continue;
            var m = CastAge().Match(l.Rest);
            if (!m.Success) continue;
            ages[l.BoldPrefix.Trim().ToUpper()] = int.Parse(m.Groups[1].Value);
        }

        var byLastWord = ages
            .GroupBy(a => a.Key.Split(' ')[^1], System.StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToList();
        foreach (var g in byLastWord)
            ages.TryAdd(g.Key, g.First().Value);

        return ages;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[,\s]*(\d{1,3})\s*år\b")]
    private static partial System.Text.RegularExpressions.Regex CastAge();

    public static List<WordLine> ReadLines(string filePath)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return [];

        var result = new List<WordLine>();

        foreach (var para in body.Descendants<Paragraph>())
        {
            // Segments of the current line: text + bold/italic.
            var segments = new List<(string Text, bool Bold, bool Italic)>();

            void Flush()
            {
                var line = Build(segments);
                if (line != null) result.Add(line);
                segments.Clear();
            }

            foreach (var run in para.Descendants<Run>())
            {
                var bold = IsBoldRun(run);
                var italic = IsItalicRun(run);
                foreach (var child in run.ChildElements)
                {
                    switch (child)
                    {
                        case Text t: segments.Add((t.Text, bold, italic)); break;
                        case TabChar: segments.Add((" ", bold, italic)); break;
                        case Break or CarriageReturn: Flush(); break;
                    }
                }
            }
            Flush();
        }

        return result;
    }

    private static WordLine? Build(List<(string Text, bool Bold, bool Italic)> segments)
    {
        var text = string.Concat(segments.Select(s => s.Text)).Trim();
        if (text.Length == 0) return null;

        // Leading bold run(s) = role-name candidate. Whitespace doesn't break it.
        var boldSb = new StringBuilder();
        int k = 0;
        for (; k < segments.Count; k++)
        {
            var s = segments[k];
            if (s.Bold) boldSb.Append(s.Text);
            else if (string.IsNullOrWhiteSpace(s.Text) && boldSb.Length == 0) continue;
            else break;
        }
        var boldText = boldSb.ToString().Trim();
        // Strip leading colon/space from the rest (e.g. ": dialog text")
        var rest = string.Concat(segments.Skip(k).Select(s => s.Text)).Trim().TrimStart(':').Trim();

        var allItalic = segments.Where(s => !string.IsNullOrWhiteSpace(s.Text)).All(s => s.Italic);

        return boldText.Length > 0
            ? new WordLine(text, boldText, rest, allItalic)
            : new WordLine(text, "", text, allItalic);
    }
}
