using Newtonsoft.Json;

namespace ManusReader.Models;

public enum TtsProvider { Natural, Azure }

public class AppSettings
{
    [JsonProperty("provider")]
    public TtsProvider Provider { get; set; } = TtsProvider.Natural;

    [JsonProperty("azure")]
    public AzureConfig Azure { get; set; } = new();
}
