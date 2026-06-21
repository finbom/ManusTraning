using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ManusReader.Models;

namespace ManusReader.Services
{
    public static class ScriptParser
    {
        // Matches "ROLLNAMN: repliktext" – role name must start with uppercase and be at most 40 chars
        private static readonly Regex RoleLineRegex = new(
            @"^([A-ZÅÄÖÉÀÜ][A-ZÅÄÖÉÀÜa-zåäöéàü0-9 \-\.]{0,39}):\s*(.*)$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        public static (List<ScriptLine> lines, HashSet<string> roles) Parse(string filePath)
        {
            var text = File.ReadAllText(filePath);
            var lines = new List<ScriptLine>();
            var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int lineNum = 0;

            foreach (var rawLine in text.Split('\n'))
            {
                var trimmed = rawLine.TrimEnd('\r').Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                // Stage direction: line surrounded by parentheses
                if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
                {
                    lines.Add(new ScriptLine
                    {
                        LineNumber = lineNum++,
                        RoleName = "[Scenanvisning]",
                        Text = trimmed,
                        IsStageDirection = true,
                        ShouldRead = false
                    });
                    continue;
                }

                var match = RoleLineRegex.Match(trimmed);
                if (match.Success)
                {
                    var role = match.Groups[1].Value.Trim();
                    var text2 = match.Groups[2].Value.Trim();

                    // Skip lines where the "role" is a long sentence (likely not a role)
                    if (role.Split(' ').Length > 4)
                    {
                        if (lines.Count > 0 && !lines[^1].IsStageDirection)
                            lines[^1].Text += " " + trimmed;
                        continue;
                    }

                    roles.Add(role);
                    lines.Add(new ScriptLine
                    {
                        LineNumber = lineNum++,
                        RoleName = role,
                        Text = text2,
                        IsStageDirection = false,
                        ShouldRead = true
                    });
                }
                else
                {
                    // Continuation of previous line or standalone text
                    if (lines.Count > 0 && !lines[^1].IsStageDirection)
                        lines[^1].Text += " " + trimmed;
                    else
                        lines.Add(new ScriptLine
                        {
                            LineNumber = lineNum++,
                            RoleName = "[Övrigt]",
                            Text = trimmed,
                            IsStageDirection = true,
                            ShouldRead = false
                        });
                }
            }

            return (lines, roles);
        }
    }
}
