using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ManusReader.Services;

// ManusReader's own copy of Ollama (MIT) for the AI script import. A pinned
// version is downloaded on demand to %LOCALAPPDATA%\ManusReader\ollama and run on
// a private port with its own model folder, so it never mixes with an Ollama the
// user may have installed – we always know exactly which version and model run.
public static class ManagedOllama
{
    public const string Version = "0.32.6";
    public const string Model = "llama3.2:3b";
    public const int Port = 11435; // Ollama's default is 11434

    public static string BaseUrl => $"http://127.0.0.1:{Port}";

    // Rough sizes for the "download now?" question.
    public const int EngineSizeMb = 1390;
    public const int ModelSizeMb = 1930;

    private static readonly string EngineUrl =
        $"https://github.com/ollama/ollama/releases/download/v{Version}/ollama-windows-amd64.zip";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static Process? _server;

    public static string RootDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ManusReader", "ollama");

    // Versioned folder, so a future version bump installs cleanly beside the old one.
    private static string EngineDir => Path.Combine(RootDir, "engine-" + Version);
    private static string ModelsDir => Path.Combine(RootDir, "models");
    private static string ExePath => Path.Combine(EngineDir, "ollama.exe");

    public static bool IsEngineInstalled => File.Exists(ExePath);

    public record DownloadProgress(string What, long Done, long? Total);

    // ------------------------------------------------------------------ install

    public static async Task InstallEngineAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(RootDir);
        var zip = Path.Combine(RootDir, $"ollama-{Version}.zip");
        var part = zip + ".part";

        try
        {
            using (var resp = await Http.GetAsync(EngineUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(part);
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    progress?.Report(new DownloadProgress("Ollama " + Version, done, total));
                }
            }
            File.Move(part, zip, overwrite: true);

            progress?.Report(new DownloadProgress("Packar upp Ollama", 0, null));
            var tmpDir = EngineDir + ".part";
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, tmpDir), ct);
            if (Directory.Exists(EngineDir)) Directory.Delete(EngineDir, recursive: true);
            Directory.Move(tmpDir, EngineDir);
        }
        finally
        {
            File.Delete(part);
            File.Delete(zip);
        }

        if (!IsEngineInstalled)
            throw new Exception("ollama.exe hittades inte i det nedladdade paketet.");

        // Drop engines from older pinned versions.
        foreach (var old in Directory.GetDirectories(RootDir, "engine-*")
                     .Where(d => !string.Equals(d, EngineDir, StringComparison.OrdinalIgnoreCase)))
        {
            try { Directory.Delete(old, recursive: true); } catch { }
        }
    }

    // ------------------------------------------------------------------ server

    // Returns the version our port answers with, or null if nothing answers.
    private static async Task<string?> RunningVersionAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var json = await Http.GetStringAsync($"{BaseUrl}/api/version", cts.Token);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("version").GetString();
        }
        catch { return null; }
    }

    // Starts our Ollama (if not already running) and waits until it answers.
    public static async Task EnsureRunningAsync(CancellationToken ct = default)
    {
        var running = await RunningVersionAsync();
        if (running == Version) return;
        if (running != null)
            throw new Exception(
                $"Port {Port} används redan av Ollama {running}. Stäng det programmet och försök igen.");

        if (!IsEngineInstalled)
            throw new InvalidOperationException("Ollama är inte nedladdat.");

        var psi = new ProcessStartInfo(ExePath, "serve")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = EngineDir,
        };
        psi.Environment["OLLAMA_HOST"] = $"127.0.0.1:{Port}";
        psi.Environment["OLLAMA_MODELS"] = ModelsDir;
        _server = Process.Start(psi) ?? throw new Exception("Kunde inte starta Ollama.");

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_server.HasExited)
                throw new Exception($"Ollama avslutades direkt (kod {_server.ExitCode}).");
            if (await RunningVersionAsync() == Version) return;
            await Task.Delay(500, ct);
        }
        throw new Exception("Ollama startade inte inom en minut.");
    }

    // Stops the server we started (called when ManusReader closes).
    public static void Stop()
    {
        try { _server?.Kill(entireProcessTree: true); } catch { }
        _server?.Dispose();
        _server = null;
    }

    // ------------------------------------------------------------------ model

    public static async Task<bool> IsModelInstalledAsync()
    {
        var json = await Http.GetStringAsync($"{BaseUrl}/api/tags");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("models").EnumerateArray()
            .Any(m => m.GetProperty("name").GetString() == Model);
    }

    // Downloads the model through our server. Ollama streams one JSON object per
    // line; download lines carry "total"/"completed" for the layer in progress.
    public static async Task PullModelAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var body = new StringContent(
            JsonSerializer.Serialize(new { model = Model, stream = true }), Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/pull") { Content = body };
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
                throw new Exception("Nedladdning av AI-modellen misslyckades: " + err.GetString());

            if (root.TryGetProperty("total", out var total) && total.GetInt64() > 0)
            {
                var completed = root.TryGetProperty("completed", out var c) ? c.GetInt64() : 0;
                progress?.Report(new DownloadProgress("AI-modellen " + Model, completed, total.GetInt64()));
            }
        }

        if (!await IsModelInstalledAsync())
            throw new Exception("AI-modellen hittades inte efter nedladdningen.");
    }
}
