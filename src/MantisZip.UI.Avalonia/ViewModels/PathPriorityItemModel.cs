using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MantisZip.UI.Avalonia.ViewModels
{
    /// <summary>默认路径优先级列表项。Kind 对应 AppSettings.DefaultPathOrder 的值域（context/explorer/recent/custom）。</summary>
    public class PathPriorityItemModel : INotifyPropertyChanged
    {
        public string Kind { get; init; } = ""; // "context" / "explorer" / "recent" / "custom"

        private string _displayName = "";
        public string DisplayName
        {
            get => _displayName;
            set { _displayName = value; OnPropertyChanged(); }
        }

        private bool _canMoveUp;
        public bool CanMoveUp
        {
            get => _canMoveUp;
            set { _canMoveUp = value; OnPropertyChanged(); }
        }

        private bool _canMoveDown;
        public bool CanMoveDown
        {
            get => _canMoveDown;
            set { _canMoveDown = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}