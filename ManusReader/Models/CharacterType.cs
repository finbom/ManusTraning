using System.Collections.Generic;
using System.Linq;

namespace ManusReader.Models
{
    // What kind of character a role is. Decides which voices fit (the voice
    // filter) and gives a starting tempo/pitch that the user can fine-tune.
    public enum CharacterType
    {
        VuxenKvinna = 0,
        VuxenMan = 1,
        AldreKvinna = 2,
        AldreMan = 3,
        BarnFlicka = 4,
        BarnPojke = 5,
    }

    public record CharacterInfo(
        CharacterType Type,
        string Display,
        VoiceGender VoiceGender, // which voices to pick from
        double Rate,             // preset, -50..+50
        double Pitch);           // preset, -50..+50

    public static class Characters
    {
        // Children have no dedicated Swedish voices, so they are made from an adult
        // female voice with raised pitch (a boy a bit lower than a girl). If the
        // engine has a real child voice (Azure "Ana"), that is used without shift.
        public static readonly IReadOnlyList<CharacterInfo> All = new List<CharacterInfo>
        {
            new(CharacterType.VuxenKvinna, "Vuxen kvinna",                VoiceGender.Kvinna,   0,   0),
            new(CharacterType.VuxenMan,    "Vuxen man",                   VoiceGender.Man,      0,   0),
            new(CharacterType.AldreKvinna, "Äldre kvinna",                VoiceGender.Kvinna, -15, -10),
            new(CharacterType.AldreMan,    "Äldre man",                   VoiceGender.Man,    -15, -10),
            new(CharacterType.BarnFlicka,  "Barn, flicka (ljusare röst)", VoiceGender.Kvinna,   5,  30),
            new(CharacterType.BarnPojke,   "Barn, pojke (ljusare röst)",  VoiceGender.Kvinna,   5,  20),
        };

        public static CharacterInfo Get(CharacterType t) => All.First(c => c.Type == t);

        public static bool IsChild(CharacterType t) =>
            t is CharacterType.BarnFlicka or CharacterType.BarnPojke;

        // Old projects only stored Man/Kvinna/Barn.
        public static CharacterType FromLegacyGender(VoiceGender g) => g switch
        {
            VoiceGender.Kvinna => CharacterType.VuxenKvinna,
            VoiceGender.Barn => CharacterType.BarnFlicka,
            _ => CharacterType.VuxenMan,
        };

        // ------------------------------------------------------------ age + gender

        public static bool IsFemale(CharacterType t) =>
            t is CharacterType.VuxenKvinna or CharacterType.AldreKvinna or CharacterType.BarnFlicka;

        // A typical age, used when the user hasn't entered one.
        public static int DefaultAge(CharacterType t) => t switch
        {
            CharacterType.AldreKvinna or CharacterType.AldreMan => 72,
            CharacterType.BarnFlicka or CharacterType.BarnPojke => 9,
            _ => 35,
        };

        public static CharacterType FromAgeAndGender(int age, bool female) => age switch
        {
            < 13 => female ? CharacterType.BarnFlicka : CharacterType.BarnPojke,
            >= 60 => female ? CharacterType.AldreKvinna : CharacterType.AldreMan,
            _ => female ? CharacterType.VuxenKvinna : CharacterType.VuxenMan,
        };

        // Tempo/pitch ranges (-50..+50 scale) that a voice of this age and gender is
        // randomised within. Children are made from adult female voices, so boys get
        // a little less lift than girls; teenage boys use male voices lifted a bit.
        public static (double RateMin, double RateMax, double PitchMin, double PitchMax)
            RangeFor(int age, bool female) => age switch
        {
            < 8 => (0, 8, female ? 32 : 25, female ? 42 : 35),
            < 13 => (0, 8, female ? 25 : 15, female ? 35 : 25),
            < 18 => (2, 10, female ? 6 : 8, female ? 14 : 16),
            < 36 => (0, 8, -2, 8),
            < 60 => (-5, 5, -6, 4),
            < 75 => (-15, -5, -12, -4),
            _ => (-25, -12, -16, -8),
        };

        public static VoiceGender ToLegacyGender(CharacterType t) => t switch
        {
            CharacterType.VuxenMan or CharacterType.AldreMan => VoiceGender.Man,
            CharacterType.BarnFlicka or CharacterType.BarnPojke => VoiceGender.Barn,
            _ => VoiceGender.Kvinna,
        };
    }
}
