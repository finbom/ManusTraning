using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ManusReader.Models;
using SpeechSynth = System.Speech.Synthesis.SpeechSynthesizer;
using SpeechGender = System.Speech.Synthesis.VoiceGender;

namespace ManusReader.Services;

public class WindowsTtsService : ITtsService
{
    private SpeechSynth? _synth;
    private TaskCompletionSource<bool>? _tcs;

    public bool IsConfigured => true;

    // ------------------------------------------------------------------ voice discovery

    public static List<(string Name, string Display, VoiceGender Gender)> GetVoices()
    {
        using var temp = new SpeechSynth();
        return temp.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v =>
            {
                var gender = v.VoiceInfo.Gender == SpeechGender.Female ? VoiceGender.Kvinna
                           : v.VoiceInfo.Gender == SpeechGender.Male ? VoiceGender.Man
                           : VoiceGender.Man;
                var display = $"{v.VoiceInfo.Name} ({v.VoiceInfo.Culture.TwoLetterISOLanguageName.ToUpper()})";
                return (v.VoiceInfo.Name, display, gender);
            })
            .ToList();
    }

    public static string DefaultVoiceName()
    {
        var voices = GetVoices();
        return voices.FirstOrDefault(v =>
                v.Name.Contains("Mattias", StringComparison.OrdinalIgnoreCase) ||
                v.Name.Contains("Sofie", StringComparison.OrdinalIgnoreCase)).Name
            ?? voices.FirstOrDefault().Name
            ?? "";
    }

    // ------------------------------------------------------------------ playback

    public Task SpeakAsync(string text, VoiceProfile profile)
    {
        StopSpeaking();

        _synth = new SpeechSynth();

        var voiceName = profile.NaturalVoiceName;
        if (!string.IsNullOrEmpty(voiceName))
        {
            try { _synth.SelectVoice(voiceName); }
            catch { /* fall back to default voice */ }
        }

        // Rate: map -50..+50 range to SAPI -10..+10
        _synth.Rate = (int)Math.Clamp(profile.Rate / 5.0, -10, 10);
        _synth.Volume = 100;

        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _synth.SpeakCompleted += (_, _) => _tcs?.TrySetResult(true);
        _synth.SpeakAsync(text);

        return _tcs.Task;
    }

    public void StopSpeaking()
    {
        _tcs?.TrySetResult(false);
        _tcs = null;
        try { _synth?.SpeakAsyncCancelAll(); } catch { }
        _synth?.Dispose();
        _synth = null;
    }

    public void Dispose()
    {
        StopSpeaking();
        GC.SuppressFinalize(this);
    }
}
