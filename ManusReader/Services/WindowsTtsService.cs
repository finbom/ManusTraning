using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
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

    // Rate maps the profile's -50..+50 range to the SAPI -10..+10 property.
    public static int RateFor(VoiceProfile profile) =>
        (int)Math.Clamp(profile.Rate / 5.0, -10, 10);

    // Builds SSML with pitch in semitones (-50..+50 → -10..+10 st). xml:lang="sv-SE"
    // tells the engine to apply Swedish pronunciation rules. Shared by playback and
    // audio export so the exported file sounds identical to live playback.
    public static string BuildSsml(string text, VoiceProfile profile)
    {
        var pitchSt = (int)Math.Clamp(profile.Pitch / 5.0, -10, 10);
        var pitchStr = pitchSt >= 0 ? $"+{pitchSt}st" : $"{pitchSt}st";
        var escaped = SecurityElement.Escape(text);

        return $"""
            <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="sv-SE">
              <prosody pitch="{pitchStr}">{escaped}</prosody>
            </speak>
            """;
    }

    public Task SpeakAsync(string text, VoiceProfile profile)
    {
        StopSpeaking();

        _synth = new SpeechSynth();

        if (!string.IsNullOrEmpty(profile.NaturalVoiceName))
        {
            try { _synth.SelectVoice(profile.NaturalVoiceName); }
            catch { /* fall back to default voice */ }
        }

        _synth.Rate = RateFor(profile);
        _synth.Volume = 100;

        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _synth.SpeakCompleted += (_, _) => _tcs?.TrySetResult(true);
        _synth.SpeakSsmlAsync(BuildSsml(text, profile));

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
