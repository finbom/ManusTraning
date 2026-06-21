using System;
using System.Threading.Tasks;
using ManusReader.Models;

namespace ManusReader.Services;

public interface ITtsService : IDisposable
{
    bool IsConfigured { get; }
    Task SpeakAsync(string text, VoiceProfile profile);
    void StopSpeaking();
}
