using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using ManusReader.Models;
using NAudio.Lame;
using NAudio.Wave;
using SpeechSynth = System.Speech.Synthesis.SpeechSynthesizer;

namespace ManusReader.Services;

// Renders a rehearsal to a single MP3 file using the Windows (System.Speech)
// voices. Each spoken line is synthesised straight to a PCM buffer (no speaker
// playback) with the role's chosen voice/rate/pitch, then encoded to MP3. Silent
// gaps are written where the user speaks their own lines.
public static class AudioExportService
{
    private const int SampleRate = 22050;   // good enough for speech, smaller files
    private const int BytesPerSecond = SampleRate * 2; // 16-bit mono

    public abstract record Segment;
    public sealed record SpeakSegment(string Text, VoiceProfile Profile) : Segment;
    public sealed record SilenceSegment(int Milliseconds) : Segment;

    public sealed record ExportProgress(int Done, int Total);

    public static void ExportMp3(
        string path,
        IReadOnlyList<Segment> segments,
        int bitRateKbps = 96,
        IProgress<ExportProgress>? progress = null)
    {
        var pcmFormat = new SpeechAudioFormatInfo(
            SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono);
        var waveFormat = new WaveFormat(SampleRate, 16, 1);

        using var synth = new SpeechSynth();
        using var mp3 = new LameMP3FileWriter(path, waveFormat, bitRateKbps);

        var total = segments.Count(s => s is SpeakSegment);
        var done = 0;

        foreach (var seg in segments)
        {
            switch (seg)
            {
                case SpeakSegment speak:
                    var pcm = RenderToPcm(synth, speak, pcmFormat);
                    mp3.Write(pcm, 0, pcm.Length);
                    progress?.Report(new ExportProgress(++done, total));
                    break;

                case SilenceSegment silence:
                    WriteSilence(mp3, silence.Milliseconds);
                    break;
            }
        }

        mp3.Flush();
    }

    private static byte[] RenderToPcm(
        SpeechSynth synth, SpeakSegment speak, SpeechAudioFormatInfo format)
    {
        using var ms = new MemoryStream();
        synth.SetOutputToAudioStream(ms, format);

        if (!string.IsNullOrEmpty(speak.Profile.NaturalVoiceName))
        {
            try { synth.SelectVoice(speak.Profile.NaturalVoiceName); }
            catch { /* fall back to default voice */ }
        }

        synth.Rate = WindowsTtsService.RateFor(speak.Profile);
        synth.Volume = 100;
        synth.SpeakSsml(WindowsTtsService.BuildSsml(speak.Text, speak.Profile));

        synth.SetOutputToNull(); // release the stream so we can read it
        return ms.ToArray();
    }

    private static void WriteSilence(LameMP3FileWriter mp3, int milliseconds)
    {
        if (milliseconds <= 0) return;

        var totalBytes = (long)BytesPerSecond * milliseconds / 1000;
        totalBytes -= totalBytes % 2; // keep 16-bit sample alignment

        var buffer = new byte[Math.Min(totalBytes, BytesPerSecond)]; // up to 1 s of zeros
        var remaining = totalBytes;
        while (remaining > 0)
        {
            var chunk = (int)Math.Min(remaining, buffer.Length);
            mp3.Write(buffer, 0, chunk);
            remaining -= chunk;
        }
    }
}
