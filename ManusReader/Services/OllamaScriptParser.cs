using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ManusReader.Models;

namespace ManusReader.Services;

public class OllamaScriptParser
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.2";

    // ------------------------------------------------------------------ availability

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var resp = await Http.GetAsync($"{BaseUrl}/api/tags");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<List<string>> GetAvailableModelsAsync()
    {
        try
        {
            var resp = await Http.GetAsync($"{BaseUrl}/api/tags");
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("models")
                .EnumerateArray()
                .Select(m => m.GetProperty("name").GetString() ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
        }
        catch { return []; }
    }

    // ------------------------------------------------------------------ main entry point

    public async Task<(List<ScriptLine> Lines, HashSet<string> Roles)> ParseAsync(
        string rawText, IProgress<string>? progress = null)
    {
        var allLines = new List<ScriptLine>();
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int lineNum = 0;

        var chunks = SplitIntoChunks(rawText, maxLines: 60).ToList();

        for (int i = 0; i < chunks.Count; i++)
        {
            progress?.Report($"Analyserar del {i + 1} av {chunks.Count} med AI...");
            var parsed = await ParseChunkAsync(chunks[i]);

            foreach (var p in parsed)
            {
                var isUnknown = p.Role == "unknown";
                var isStage = p.IsStageDirection || p.Role == "[Scenanvisning]";

                allLines.Add(new ScriptLine
                {
                    LineNumber = lineNum++,
                    RoleName = p.Role,
                    Text = p.Text,
                    IsStageDirection = isStage,
                    IsUnknown = isUnknown,
                    ShouldRead = !isStage && !isUnknown
                });

                if (!isStage && !isUnknown)
                    roles.Add(p.Role);
            }
        }

        return (allLines, roles);
    }

    // ------------------------------------------------------------------ chunk parsing

    private async Task<List<ParsedLine>> ParseChunkAsync(string chunk)
    {
        var prompt = $$"""
            Du är expert på teatermauser. Analysera texten nedan och extrahera ALLA repliker.

            Returnera ENBART giltig JSON i detta exakta format (ingen preamble, inget kodblock):
            {"lines":[{"role":"ROLLNAMN","text":"repliktext","stage":false,"confidence":"high"}]}

            Regler:
            - "role": rollnamnet i VERSALER. Scenanvisning → "[Scenanvisning]". Osäker → "unknown"
            - "text": exakt repliktext
            - "stage": true om scenanvisning, annars false
            - "confidence": "high" = säker, "medium" = trolig, "low" = osäker
            - Om confidence är "medium" eller "low" MÅSTE role vara "unknown"
            - Skippa tomma rader, sidnummer och rubriker

            Text:
            {{chunk}}
            """;

        var body = JsonSerializer.Serialize(new
        {
            model = Model,
            messages = new[] { new { role = "user", content = prompt } },
            stream = false
        });

        var response = await Http.PostAsync(
            $"{BaseUrl}/api/chat",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        return ParseJson(content);
    }

    // ------------------------------------------------------------------ JSON parsing

    private static List<ParsedLine> ParseJson(string text)
    {
        // Strip markdown code fences if present
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var nl = text.IndexOf('\n');
            var end = text.LastIndexOf("```");
            if (nl >= 0 && end > nl) text = text[(nl + 1)..end].Trim();
        }

        // Extract first { ... } block in case of extra surrounding text
        var start = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        if (start < 0 || last < 0 || last <= start) return [];
        text = text[start..(last + 1)];

        try
        {
            using var doc = JsonDocument.Parse(text);
            var result = new List<ParsedLine>();

            foreach (var item in doc.RootElement.GetProperty("lines").EnumerateArray())
            {
                var role = item.TryGetProperty("role", out var r) ? r.GetString() ?? "unknown" : "unknown";
                var lineText = item.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                var stage = item.TryGetProperty("stage", out var s) && s.GetBoolean();
                var conf = item.TryGetProperty("confidence", out var c) ? c.GetString() : "high";

                if (conf is "low" or "medium") role = "unknown";
                if (string.IsNullOrWhiteSpace(lineText)) continue;

                result.Add(new ParsedLine(role, lineText, stage));
            }

            return result;
        }
        catch { return []; }
    }

    // ------------------------------------------------------------------ helpers

    private static IEnumerable<string> SplitIntoChunks(string text, int maxLines)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i += maxLines)
            yield return string.Join('\n', lines.Skip(i).Take(maxLines));
    }

    private record ParsedLine(string Role, string Text, bool IsStageDirection);
}
