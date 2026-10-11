using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace ManusReader.Services;

// Shared rules for telling role names apart from headings, song lyrics and
// stage directions. Used by the Word and text parsers.
public static partial class ScriptHeuristics
{
    // True if all letters are uppercase (Swedish included).
    public static bool IsAllCaps(string text)
    {
        var letters = text.Where(char.IsLetter).ToArray();
        return letters.Length > 0 && letters.Length <= 50 && letters.All(char.IsUpper);
    }

    public static bool IsStageDirection(string text) =>
        text.StartsWith('(') && text.EndsWith(')');

    // Whether a short text can be a role name ("ROOSEVELT", "PERKINS & ICKES",
    // "MRS. HOWE"). Rejects what scripts put in the same bold/caps style:
    // headings ("SCEN 2:3", "# 21 - CABINET TOMORROW") and song lyrics
    // ("DET KOMMER EN DAG, IMORGON.", "OCH LÄMNAR MEJ ALDRIG MER!").
    public static bool LooksLikeRoleName(string text)
    {
        var name = text.Trim().TrimEnd(':').Trim();
        if (name.Length == 0 || !name.Any(char.IsLetter)) return false;
        if (name.StartsWith('#') || name.Any(char.IsDigit)) return false;
        if (".,!?…;".Contains(name[^1])) return false;
        if (name.Contains(", ")) return false;

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Count(w => w is not ("&" or "och" or "OCH" or "and" or "AND"));
        return words is >= 1 and <= 4;
    }

    [GeneratedRegex(@"^(AKT|AKTEN|SCEN|PROLOG|EPILOG|ACT|SCENE)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ActOrScene();

    // "AKT 1", "SCEN 1:1A", "SCEN1:2A", "PROLOG" …
    public static bool IsActOrSceneHeading(string text)
    {
        var t = text.Trim();
        return t.Length <= 40 && IsAllCaps(t) && ActOrScene().IsMatch(t.Replace("SCEN1", "SCEN 1"));
    }

    [GeneratedRegex(@"\([^()]*\)")]
    private static partial Regex Parenthesised();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    // The part of a line that should be read aloud: inline stage directions such
    // as "(Drämmer näven i bordet)" are shown in the script but not spoken.
    public static string ForSpeech(string text)
    {
        var spoken = Parenthesised().Replace(text, " ");
        return MultiSpace().Replace(spoken, " ").Trim();
    }
}
