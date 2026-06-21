using Newtonsoft.Json;

namespace ManusReader.Models
{
    public class AzureConfig
    {
        [JsonProperty("subscriptionKey")]
        public string SubscriptionKey { get; set; } = "YOUR_KEY";

        [JsonProperty("region")]
        public string Region { get; set; } = "swedencentral";

        [JsonProperty("language")]
        public string Language { get; set; } = "sv-SE";
    }
}
