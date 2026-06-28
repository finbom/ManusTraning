using Newtonsoft.Json;

namespace ManusReader.Models;

public enum TtsProvider { Natural, Azure }

public class AppSettings
{
    [JsonProperty("provider")]
    public TtsProvider Provider { get; set; } = TtsProvider.Natural;

    [JsonProperty("azure")]
    public AzureConfig Azure { get; set; } = new();

    // ---- Rehearsal (autoplay) settings ----

    // Pause in seconds inserted after each computer-spoken line.
    [JsonProperty("pauseBetweenSeconds")]
    public double PauseBetweenSeconds { get; set; } = 0.7;

    // When true, the pause for the user's own lines is estimated from the line
    // length; otherwise MyLineSeconds is used.
    [JsonProperty("myLineAuto")]
    public bool MyLineAuto { get; set; } = true;

    // Fixed pause in seconds for the user's own lines (when MyLineAuto is false).
    [JsonProperty("myLineSeconds")]
    public double MyLineSeconds { get; set; } = 3.0;
}
