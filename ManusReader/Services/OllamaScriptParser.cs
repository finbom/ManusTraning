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

    // Always our own pinned Ollama (see ManagedOllama).
    public string BaseUrl { get; set; } = ManagedOllama.BaseUrl;
    public string Model { get; set; } = ManagedOllama.Model;

    // ------------------------------------------------------------------ main entry point

    public async Task<ParseResult> ParseAsync(
        string rawText, IProgress<ParseProgress>? progress = null)
    {
        var allLines = new List<ScriptLine>();
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int lineNum = 0;
        int failedChunks = 0;
        int salvagedLines = 0;

        var sourceLines = rawText
            .Split('\n')
            .Count(l => !string.IsNullOrWhiteSpace(l));

        var chunks = SplitIntoChunks(rawText, maxLines: 35).ToList();

        for (int i = 0; i < chunks.Count; i++)
        {
            // Report i completed chunks before starting chunk i+1.
            progress?.Report(new ParseProgress(i, chunks.Count));

            var chunk = chunks[i];
            var chunkSourceLines = chunk.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));
            var chunkHasContent = chunkSourceLines > 0;

            List<ParsedLine> parsed;
            try
            {
                parsed = await ParseChunkAsync(chunk);

                // Retry once if the model returned nothing for a non-empty chunk.
                if (parsed.Count == 0 && chunkHasContent)
                    parsed = await ParseChunkAsync(chunk);
            }
            catch
            {
                parsed = [];
            }

            // Sanity check: the model sometimes returns valid JSON but silently
            // drops most lines. If it produced far fewer lines than the source
            // chunk, compare against the rule-based parser and keep whichever
            // captured more — so content is never silently lost.
            var looksDeficient = chunkHasContent &&
                                 parsed.Count < chunkSourceLines * 0.4;
            if (looksDeficient)
            {
                var (rescued, rescuedRoles) = TextScriptParser.Parse(chunk);
                if (rescued.Count > parsed.Count)
                {
                    failedChunks++;
                    foreach (var r in rescued)
                    {
                        r.LineNumber = lineNum++;
                        allLines.Add(r);
                    }
                    salvagedLines += rescued.Count;
                    foreach (var rr in rescuedRoles) roles.Add(rr);
                    continue;
                }
            }

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

        progress?.Report(new ParseProgress(chunks.Count, chunks.Count));

        var unknownLines = allLines.Count(l => l.IsUnknown);
        var stats = new ParseStats(
            SourceLines: sourceLines,
            ParsedLines: allLines.Count,
            Chunks: chunks.Count,
            FailedChunks: failedChunks,
            SalvagedLines: salvagedLines,
            UnknownLines: unknownLines,
            Roles: roles.Count);

        return new ParseResult(allLines, roles, stats);
    }

    // Progress update from AI parsing: how many chunks are completed out of the total.
    public record ParseProgress(int Completed, int Total);

    // Result of a full parse, including diagnostics shown to the user afterwards.
    public record ParseResult(List<ScriptLine> Lines, HashSet<string> Roles, ParseStats Stats);

    public record ParseStats(
        int SourceLines, int ParsedLines, int Chunks,
        int FailedChunks, int SalvagedLines, int UnknownLines, int Roles);

    // ------------------------------------------------------------------ chunk parsing

    private async Task<List<ParsedLine>> ParseChunkAsync(string chunk)
    {
        var prompt = $$"""
            Du är expert på teatermanus (svenska och engelska). Extrahera VARJE replik ur texten.

            ABSOLUT VIKTIGAST:
            - Hoppa INTE över någon replik. Sammanfatta INTE. Korta INTE ner.
            - Behåll repliktexten ORDAGRANT, exakt som i originalet.
            - Varje rad med dialog i texten ska bli ett objekt i "lines".

            Formateringsregler:
            - Text inom **dubbla asterisker** är fetstil i originalet.
            - Om en rad BÖRJAR med **fetstilstext** på 1–4 ord är det ett ROLLNAMN.
              Resten av samma rad (efter eventuellt kolon/mellanslag) är repliken.
              Exempel: "**ANNA** Du behöver inte oroa dig." → role: ANNA, text: "Du behöver inte oroa dig."
              Exempel: "**LISA:**" (ensam rad) → role: LISA, nästa rad är repliken.
            - "NAMN: replik" eller "NAME: line" → role: NAMN, text: replik.
            - Är fetstilstexten längre än 4 ord är det troligen inte ett rollnamn.

            Returnera ENBART giltig JSON i detta exakta format:
            {"lines":[{"role":"ROLLNAMN","text":"repliktext","stage":false,"confidence":"high"}]}

            Regler:
            - "role": rollnamnet i VERSALER. Scenanvisning → "[Scenanvisning]". Osäker → "unknown"
            - "text": repliktext utan rollnamnet
            - "stage": true om scenanvisning (text inom parentes), annars false
            - "confidence": "high" = säker, "medium" = trolig, "low" = osäker
            - Om confidence är "medium" eller "low" MÅSTE role vara "unknown"
            - Skippa endast helt tomma rader och sidnummer

            Text:
            {{chunk}}
            """;

        var body = JsonSerializer.Serialize(new
        {
            model = Model,
            messages = new[] { new { role = "user", content = prompt } },
            stream = false,
            format = "json",     // force grammatically valid JSON output
            options = new
            {
                temperature = 0,  // deterministic, less likely to omit lines
                num_ctx = 8192,   // larger context so big chunks aren't truncated
                num_predict = -1  // no output-length cap
            }
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
