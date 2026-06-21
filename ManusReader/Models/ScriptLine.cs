using System.ComponentModel;

namespace ManusReader.Models
{
    public class ScriptLine : INotifyPropertyChanged
    {
        private bool _shouldRead = true;
        private bool _isCurrent;

        public int LineNumber { get; set; }
        public string RoleName { get; set; } = "";
        public string Text { get; set; } = "";
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
