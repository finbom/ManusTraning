using System;
using System.Security;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using ManusReader.Models;

namespace ManusReader.Services
{
    public class TtsService : ITtsService
    {
        private SpeechSynthesizer? _synthesizer;
        private AzureConfig? _config;

        public bool IsConfigured =>
            _config != null &&
            !string.IsNullOrWhiteSpace(_config.SubscriptionKey) &&
            _config.SubscriptionKey != "YOUR_KEY";

        public void Configure(AzureConfig config)
        {
            _config = config;
            _synthesizer?.Dispose();
            var speechConfig = SpeechConfig.FromSubscription(config.SubscriptionKey, config.Region);
            _synthesizer = new SpeechSynthesizer(speechConfig);
        }

        public async Task SpeakAsync(string text, VoiceProfile profile)
        {
            if (_synthesizer == null)
                throw new InvalidOperationException("TTS är inte konfigurerat.");

            var rateStr = profile.Rate >= 0 ? $"+{profile.Rate:F0}%" : $"{profile.Rate:F0}%";
            var pitchStr = profile.Pitch >= 0 ? $"+{profile.Pitch:F0}%" : $"{profile.Pitch:F0}%";
            var escaped = SecurityElement.Escape(text);
            var lang = _config!.Language;

            var ssml = $"""
                <speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="{lang}">
                  <voice name="{profile.VoiceName}">
                    <prosody rate="{rateStr}" pitch="{pitchStr}">{escaped}</prosody>
                  </voice>
                </speak>
                """;

            await _synthesizer.SpeakSsmlAsync(ssml);
        }

        public void StopSpeaking()
        {
            _synthesizer?.StopSpeakingAsync();
        }

        public void Dispose()
        {
            _synthesizer?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
