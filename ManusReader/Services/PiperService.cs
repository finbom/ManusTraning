using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Formats.Tar;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManusReader.Models;
using NAudio.Wave;
using Newtonsoft.Json.Linq;

namespace ManusReader.Services;

// Piper (https://github.com/rhasspy/piper) – free, local neural TTS. The engine
// (piper.exe, MIT; bundles GPL espeak-ng) and the voice models are downloaded on
// demand from their official sources to %LOCALAPPDATA%\ManusReader\piper, so
// nothing from Piper is redistributed inside ManusReader.exe.
public static class Piper
{
    public const int SampleRate = 22050; // all catalog voices are "medium" = 22.05 kHz

    private const string EngineUrl =
        "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip";
    private const string VoiceBaseUrl =
        "https://huggingface.co/rhasspy/piper-voices/resolve/main/";

    // A voice comes either from rhasspy/piper-voices on Hugging Face (HfPath →
    // .onnx + .onnx.json) or as a .tar.gz release (TarUrl). Some archives lack the
    // .onnx.json; then ConfigFrom names a compatible config to download instead.
    public record CatalogVoice(
        string Id, string Display, VoiceGender Gender, string License, string Credit,
        string? HfPath = null, string? TarUrl = null, string? ConfigFrom = null, int Speakers = 1);

    private const string YeagerReleases = "https://github.com/yeager/piper-voices-sv/releases/download/";

    // Swedish voices. Alma, Axel and Daniel are by Daniel Nylander (yeager).
    public static readonly IReadOnlyList<CatalogVoice> Catalog = new List<CatalogVoice>
    {
        new("sv_SE-alma-medium",   "Alma – Kvinna (SV, Piper)",   VoiceGender.Kvinna, "CC BY 4.0", "Daniel Nylander",
            HfPath: "sv/sv_SE/alma/medium/"),
        new("sv_SE-lisa-medium",   "Lisa – Kvinna (SV, Piper)",   VoiceGender.Kvinna, "ej angiven", "rhasspy/piper-voices",
            HfPath: "sv/sv_SE/lisa/medium/"),
        new("sv_SE-axel-medium",   "Axel – Man (SV, Piper)",      VoiceGender.Man,    "CC BY 4.0", "Daniel Nylander",
            TarUrl: YeagerReleases + "v1.2.0/sv_SE-axel-medium.tar.gz"),
        new("sv_SE-daniel-medium", "Daniel – Man (SV, Piper)",    VoiceGender.Man,    "CC BY 4.0", "Daniel Nylander",
            TarUrl: YeagerReleases + "v1.1.0/sv_SE-daniel-medium.tar.gz",
            ConfigFrom: "sv/sv_SE/nst/medium/sv_SE-nst-medium.onnx.json", Speakers: 16),
        new("sv_SE-nst-medium",    "NST – Man, mörk (SV, Piper)", VoiceGender.Man,    "CC0", "KBLab / Kungliga biblioteket",
            HfPath: "sv/sv_SE/nst/medium/"),
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public static string RootDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ManusReader", "piper");

    private static string EngineDir => Path.Combine(RootDir, "engine");
    private static string VoicesDir => Path.Combine(RootDir, "voices");
    public static string ExePath => Path.Combine(EngineDir, "piper", "piper.exe");

    public static bool IsEngineInstalled => File.Exists(ExePath);

    public static string ModelPath(string id) => Path.Combine(VoicesDir, id + ".onnx");

    public static bool IsVoiceInstalled(string id) =>
        File.Exists(ModelPath(id)) && File.Exists(ModelPath(id) + ".json");

    public static bool IsReady => IsEngineInstalled && InstalledVoices().Count > 0;

    public static List<(string Name, string Display, VoiceGender Gender)> InstalledVoices() =>
        Catalog.Where(v => IsVoiceInstalled(v.Id))
               .Select(v => (v.Id, v.Display, v.Gender))
               .ToList();

    // ------------------------------------------------------------------ download

    public record DownloadProgress(string What, long Done, long? Total);

    public static async Task InstallEngineAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var zip = Path.Combine(RootDir, "piper_windows_amd64.zip");
        await DownloadFileAsync(EngineUrl, zip, "Piper", progress, ct);

        if (Directory.Exists(EngineDir)) Directory.Delete(EngineDir, recursive: true);
        ZipFile.ExtractToDirectory(zip, EngineDir);
        File.Delete(zip);

        if (!IsEngineInstalled)
            throw new Exception("piper.exe hittades inte i det nedladdade paketet.");
    }

    public static async Task InstallVoiceAsync(CatalogVoice voice, IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        var model = ModelPath(voice.Id);

        if (voice.HfPath != null)
        {
            var url = VoiceBaseUrl + voice.HfPath + voice.Id + ".onnx";
            // Config first (small), model last – IsVoiceInstalled needs both.
            await DownloadFileAsync(url + ".json", model + ".json", voice.Display, null, ct);
            await DownloadFileAsync(url, model, voice.Display, progress, ct);
            return;
        }

        var tar = Path.Combine(RootDir, voice.Id + ".tar.gz");
        await DownloadFileAsync(voice.TarUrl!, tar, voice.Display, progress, ct);
        try
        {
            string? onnxTmp = null, jsonTmp = null;
            await using (var gz = new GZipStream(File.OpenRead(tar), CompressionMode.Decompress))
            using (var reader = new TarReader(gz))
            {
                while (reader.GetNextEntry() is { } entry)
                {
                    if (entry.DataStream == null) continue;
                    if (entry.Name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
                        entry.ExtractToFile(onnxTmp = model + ".part", overwrite: true);
                    else if (entry.Name.EndsWith(".onnx.json", StringComparison.OrdinalIgnoreCase))
                        entry.ExtractToFile(jsonTmp = model + ".json.part", overwrite: true);
                }
            }

            if (onnxTmp == null)
                throw new Exception("Röstpaketet innehöll ingen modell (.onnx).");

            if (jsonTmp == null)
            {
                if (voice.ConfigFrom == null)
                    throw new Exception("Röstpaketet saknar konfiguration (.onnx.json).");

                jsonTmp = model + ".json.part";
                await DownloadFileAsync(VoiceBaseUrl + voice.ConfigFrom, jsonTmp, voice.Display, null, ct);
            }

            if (voice.Speakers > 1)
            {
                var cfg = JObject.Parse(File.ReadAllText(jsonTmp));
                cfg["num_speakers"] = voice.Speakers;
                cfg["speaker_id_map"] = new JObject(
                    Enumerable.Range(0, voice.Speakers).Select(i => new JProperty(i.ToString(), i)));
                File.WriteAllText(jsonTmp, cfg.ToString());
            }

            File.Move(jsonTmp, model + ".json", overwrite: true);
            File.Move(onnxTmp, model, overwrite: true);
        }
        finally
        {
            File.Delete(tar);
            File.Delete(model + ".part");
            File.Delete(model + ".json.part");
        }
    }

    public static void RemoveVoice(string id)
    {
        File.Delete(ModelPath(id));
        File.Delete(ModelPath(id) + ".json");
    }

    private static async Task DownloadFileAsync(string url, string dest, string what,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var tmp = dest + ".part";

        using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;

            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                progress?.Report(new DownloadProgress(what, done, total));
            }
        }

        File.Move(tmp, dest, overwrite: true);
    }

    // ------------------------------------------------------------------ synthesis

    // Renders a line to 16-bit mono PCM at SampleRate.
    //
    // Rate (-50..+50) maps to Piper's length_scale. Piper has no pitch control, so
    // pitch is done by rendering longer (length_scale × f) and then resampling by f:
    // the duration comes back to normal while the pitch moves by f. Pitch uses the
    // same -50..+50 → -10..+10 semitone scale as the Windows voices.
    public static byte[] RenderPcm(string text, VoiceProfile profile, CancellationToken ct = default)
    {
        if (!IsEngineInstalled)
            throw new InvalidOperationException("Piper är inte installerat. Öppna TTS-inställningar.");

        var voiceId = IsVoiceInstalled(profile.PiperVoiceName)
            ? profile.PiperVoiceName
            : InstalledVoices().FirstOrDefault().Name
              ?? throw new InvalidOperationException("Ingen Piper-röst är nedladdad.");

        var model = ModelPath(voiceId);
        var semitones = Math.Clamp(profile.Pitch / 5.0, -10, 10);
        var pitchFactor = Math.Pow(2, semitones / 12.0);
        var speed = 1 + Math.Clamp(profile.Rate, -50, 50) / 100.0;
        var lengthScale = BaseLengthScale(model) / speed * pitchFactor;

        var psi = new ProcessStartInfo(ExePath)
        {
            ArgumentList =
            {
                "--model", model, "--output_raw",
                "--length_scale", lengthScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(ExePath)!,
        };

        using var proc = Process.Start(psi) ?? throw new Exception("Kunde inte starta Piper.");
        using var reg = ct.Register(() => { try { proc.Kill(); } catch { } });
        var stderrTask = proc.StandardError.ReadToEndAsync();

        // Piper treats each input line as one utterance – keep the line together.
        var input = Encoding.UTF8.GetBytes(text.ReplaceLineEndings(" ") + "\n");
        proc.StandardInput.BaseStream.Write(input);
        proc.StandardInput.Close();
        using var ms = new MemoryStream();
        proc.StandardOutput.BaseStream.CopyTo(ms);
        proc.WaitForExit();
        ct.ThrowIfCancellationRequested();

        if (proc.ExitCode != 0)
            throw new Exception($"Piper misslyckades (kod {proc.ExitCode}): {LastLine(stderrTask.Result)}");

        var pcm = ms.ToArray();
        return Math.Abs(pitchFactor - 1) < 0.001 ? pcm : Resample(pcm, pitchFactor);
    }

    // Voices can ship a tuned default speed (e.g. alma uses 0.9).
    private static double BaseLengthScale(string model)
    {
        try
        {
            var json = JObject.Parse(File.ReadAllText(model + ".json"));
            return json["inference"]?["length_scale"]?.Value<double>() ?? 1.0;
        }
        catch { return 1.0; }
    }

    // Linear-interpolation resample of 16-bit mono PCM; factor > 1 shortens.
    private static byte[] Resample(byte[] pcm, double factor)
    {
        var src = new short[pcm.Length / 2];
        Buffer.BlockCopy(pcm, 0, src, 0, src.Length * 2);
        if (src.Length < 2) return pcm;

        var dst = new short[(int)(src.Length / factor)];
        for (int i = 0; i < dst.Length; i++)
        {
            var pos = i * factor;
            var j = (int)pos;
            if (j >= src.Length - 1) { dst[i] = src[^1]; continue; }
            var frac = pos - j;
            dst[i] = (short)(src[j] + (src[j + 1] - src[j]) * frac);
        }

        var result = new byte[dst.Length * 2];
        Buffer.BlockCopy(dst, 0, result, 0, result.Length);
        return result;
    }

    private static string LastLine(string s) =>
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
}

// Plays Piper output through the default audio device.
public class PiperTtsService : ITtsService
{
    private CancellationTokenSource? _cts;
    private WaveOutEvent? _player;
    private TaskCompletionSource<bool>? _tcs;

    public bool IsConfigured => Piper.IsReady;

    public async Task SpeakAsync(string text, VoiceProfile profile)
    {
        StopSpeaking();
        var cts = _cts = new CancellationTokenSource();

        byte[] pcm;
        try { pcm = await Task.Run(() => Piper.RenderPcm(text, profile, cts.Token)); }
        catch (OperationCanceledException) { return; }

        if (cts.IsCancellationRequested || pcm.Length == 0) return;

        var tcs = _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = _player = new WaveOutEvent();
        player.Init(new RawSourceWaveStream(new MemoryStream(pcm), new WaveFormat(Piper.SampleRate, 16, 1)));
        player.PlaybackStopped += (_, _) => tcs.TrySetResult(true);
        player.Play();

        await tcs.Task;
        if (ReferenceEquals(_player, player)) _player = null;
        player.Dispose();
    }

    public void StopSpeaking()
    {
        _cts?.Cancel();
        _cts = null;
        _player?.Stop(); // raises PlaybackStopped → SpeakAsync completes and disposes
        _player = null;
        _tcs?.TrySetResult(false);
        _tcs = null;
    }

    public void Dispose()
    {
        StopSpeaking();
        GC.SuppressFinalize(this);
    }
}
