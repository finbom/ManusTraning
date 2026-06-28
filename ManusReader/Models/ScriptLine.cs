using System.ComponentModel;

namespace ManusReader.Models
{
    public class ScriptLine : INotifyPropertyChanged
    {
        private bool _shouldRead = true;
        private bool _isCurrent;
        private string _roleName = "";
        private string _text = "";

        public int LineNumber { get; set; }

        public string RoleName
        {
            get => _roleName;
            set { _roleName = value; OnPropertyChanged(nameof(RoleName)); }
        }

        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(nameof(Text)); }
        }

        public bool IsStageDirection { get; set; }
        public bool IsUnknown { get; set; }

        public bool ShouldRead
        {
            get => _shouldRead;
            set { _shouldRead = value; OnPropertyChanged(nameof(ShouldRead)); }
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set { _isCurrent = value; OnPropertyChanged(nameof(IsCurrent)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
