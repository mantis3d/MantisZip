using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;

namespace MantisZip.UI.Avalonia.ViewModels
{
    public class FormatAssocItemModel : INotifyPropertyChanged
    {
        public string Extension { get; init; } = ""; // ".zip"
        public string Description { get; init; } = ""; // "ZIP 压缩包"
        public Bitmap? Icon { get; set; }
        public bool IsCustom { get; init; }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        private string _currentHandler = "";
        public string CurrentHandler
        {
            get => _currentHandler;
            set { _currentHandler = value; OnPropertyChanged(); }
        }

        public ICommand? DeleteCommand { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
