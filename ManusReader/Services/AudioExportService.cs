using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using ManusReader.Models;
using Microsoft.CognitiveServices.Speech;
using NAudio.Lame;
using NAudio.Wave;
using SapiSynth = System.Speech.Synthesis.SpeechSynthesizer;
using AzureSynth = Microsoft.CognitiveServices.Speech.SpeechSynthesizer;

namespace ManusReader.Services;

// Renders a rehearsal to a single MP3 file. Each spoken line is synthesised to a
// raw PCM buffer (no speaker playback) by an engine-specific IPcmRenderer, silent
// gaps are inserted where the user speaks, and everything is encoded to MP3 with
// LAME. Works with the Windows, Piper and Azure voices.
public static class AudioExportService
{
    public abstract record Segment;
    public sealed record SpeakSegment(string Text, VoiceProfile Profile) : Segment;
    public sealed record SilenceSegment(int Milliseconds) : Segment;

    public sealed record ExportProgress(int Done, int Total);

    // Produces 16-bit mono PCM for a line, using the chosen voice/rate/pitch.
    public interface IPcmRenderer : IDisposable
    {
        int SampleRate { get; }
        byte[] RenderPcm(string text, VoiceProfile profile);
    }

    public static void ExportMp3(
        string path,
        IReadOnlyList<Segment> segments,
        IPcmRenderer renderer,
        int bitRateKbps = 96,
        IProgress<ExportProgress>? progress = null)
    {
        var waveFormat = new WaveFormat(renderer.SampleRate, 16, 1);
        var bytesPerSecond = renderer.SampleRate * 2; // 16-bit mono

        using var mp3 = new LameMP3FileWriter(path, waveFormat, bitRateKbps);

        var total = segments.Count(s => s is SpeakSegment);
        var done = 0;

        foreach (var seg in segments)
        {
            switch (seg)
            {
                case SpeakSegment speak:
                    var pcm = renderer.RenderPcm(speak.Text, speak.Profile);
                    if (pcm.Length > 0) mp3.Write(pcm, 0, pcm.Length);
                    progress?.Report(new ExportProgress(++done, total));
                    break;

                case SilenceSegment silence:
                    WriteSilence(mp3, silence.Milliseconds, bytesPerSecond);
                    break;
            }
        }

        mp3.Flush();
    }

    private static void WriteSilence(LameMP3FileWriter mp3, int milliseconds, int bytesPerSecond)
    {
        if (milliseconds <= 0) return;

        var totalBytes = (long)bytesPerSecond * milliseconds / 1000;
        totalBytes -= totalBytes % 2; // keep 16-bit sample alignment

        var buffer = new byte[Math.Min(totalBytes, bytesPerSecond)]; // up to 1 s of zeros
        var remaining = totalBytes;
        while (remaining > 0)
        {
            var chunk = (int)Math.Min(remaining, buffer.Length);
            mp3.Write(buffer, 0, chunk);
            remaining -= chunk;
        }
    }

    // ---------------------------------------------------------------- Windows renderer

    public sealed class WindowsPcmRenderer : IPcmRenderer
    {
        private readonly SapiSynth _synth = new();
        private readonly SpeechAudioFormatInfo _format =
            new(22050, AudioBitsPerSample.Sixteen, AudioChannel.Mono);

        public int SampleRate => 22050;

        public byte[] RenderPcm(string text, VoiceProfile profile)
        {
            using var ms = new MemoryStream();
            _synth.SetOutputToAudioStream(ms, _format);

            if (!string.IsNullOrEmpty(profile.NaturalVoiceName))
            {
                try { _synth.SelectVoice(profile.NaturalVoiceName); }
                catch { /* fall back to default voice */ }
            }

            _synth.Rate = WindowsTtsService.RateFor(profile);
            _synth.Volume = 100;
            _synth.SpeakSsml(WindowsTtsService.BuildSsml(text, profile));

            _synth.SetOutputToNull(); // release the stream so we can read it
            return ms.ToArray();
        }

        public void Dispose() => _synth.Dispose();
    }

    // ---------------------------------------------------------------- Piper renderer

    public sealed class PiperPcmRenderer : IPcmRenderer
    {
        public int SampleRate => Piper.SampleRate;

        public byte[] RenderPcm(string text, VoiceProfile profile) => Piper.RenderPcm(text, profile);

        public void Dispose() { }
    }

    // ---------------------------------------------------------------- Azure renderer

    public sealed class AzurePcmRenderer : IPcmRenderer
    {
        private readonly AzureSynth _synth;
        private readonly string _lang;

        public int SampleRate => 24000;

        public AzurePcmRenderer(AzureConfig config)
        {
            _lang = config.Language;
            var speechConfig = SpeechConfig.FromSubscription(config.SubscriptionKey, config.Region);
            // Raw PCM out (no container) so segments concatenate cleanly before MP3 encode.
            speechConfig.SetSpeechSynthesisOutputFormat(
                SpeechSynthesisOutputFormat.Raw24Khz16BitMonoPcm);
            // null audio output → no speaker playback; bytes come back in result.AudioData.
            _synth = new AzureSynth(speechConfig, null);
        }

        public byte[] RenderPcm(string text, VoiceProfile profile)
        {
            var ssml = TtsService.BuildSsml(text, profile, _lang);
            var result = _synth.SpeakSsmlAsync(ssml).GetAwaiter().GetResult();

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
                return result.AudioData;

            if (result.Reason == ResultReason.Canceled)
            {
                var c = SpeechSynthesisCancellationDetails.FromResult(result);
                throw new Exception($"Azure TTS misslyckades: {c.Reason} – {c.ErrorDetails}");
            }

            return Array.Empty<byte>();
        }

        public void Dispose() => _synth.Dispose();
    }
}
