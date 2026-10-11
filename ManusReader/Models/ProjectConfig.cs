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

        [JsonProperty("naturalVoiceName")]
        public string NaturalVoiceName { get; set; } = "";

        [JsonProperty("piperVoiceName")]
        public string PiperVoiceName { get; set; } = "";

        // Legacy Man/Kvinna/Barn – still written so older versions can open the file.
        [JsonProperty("gender")]
        public int Gender { get; set; }

        [JsonProperty("character")]
        public int? Character { get; set; }

        [JsonProperty("age")]
        public int? Age { get; set; }

        [JsonProperty("rate")]
        public double Rate { get; set; }

        [JsonProperty("pitch")]
        public double Pitch { get; set; }
    }
}
