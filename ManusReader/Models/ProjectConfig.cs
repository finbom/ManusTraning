using System.Collections.Generic;
using Newtonsoft.Json;

namespace ManusReader.Models
{
    public class ProjectConfig
    {
        [JsonProperty("scriptFilePath")]
        public string ScriptFilePath { get; set; } = "";

        [JsonProperty("voiceProfiles")]
        public List<VoiceProfileData> VoiceProfiles { get; set; } = new();

        [JsonProperty("lineReadStates")]
        public Dictionary<int, bool> LineReadStates { get; set; } = new();
    }

    public class VoiceProfileData
    {
        [JsonProperty("roleName")]
        public string RoleName { get; set; } = "";

        [JsonProperty("voiceName")]
        public string VoiceName { get; set; } = "";

        [JsonProperty("gender")]
        public int Gender { get; set; }

        [JsonProperty("rate")]
        public double Rate { get; set; }

        [JsonProperty("pitch")]
        public double Pitch { get; set; }
    }
}
