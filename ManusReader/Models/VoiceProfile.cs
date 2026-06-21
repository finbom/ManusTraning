using System.ComponentModel;

namespace ManusReader.Models
{
    public enum VoiceGender { Man = 0, Kvinna = 1, Barn = 2 }

    public class VoiceProfile : INotifyPropertyChanged
    {
        private string _roleName = "";
        private string _voiceName = "sv-SE-MattiasNeural";
        private string _naturalVoiceName = "";
        private VoiceGender _gender = VoiceGender.Man;
        private double _rate;
        private double _pitch;

        public string RoleName
        {
            get => _roleName;
            set { _roleName = value; OnPropertyChanged(nameof(RoleName)); }
        }

        public string VoiceName
        {
            get => _voiceName;
            set { _voiceName = value; OnPropertyChanged(nameof(VoiceName)); }
        }

        public string NaturalVoiceName
        {
            get => _naturalVoiceName;
            set { _naturalVoiceName = value; OnPropertyChanged(nameof(NaturalVoiceName)); }
        }

        public VoiceGender Gender
        {
            get => _gender;
            set { _gender = value; OnPropertyChanged(nameof(Gender)); }
        }

        public double Rate
        {
            get => _rate;
            set { _rate = value; OnPropertyChanged(nameof(Rate)); }
        }

        public double Pitch
        {
            get => _pitch;
            set { _pitch = value; OnPropertyChanged(nameof(Pitch)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
