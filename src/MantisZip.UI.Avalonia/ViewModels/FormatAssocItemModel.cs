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

        /// <summary>
        /// 是否出现在「打开压缩包」对话框的筛选里。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="IsEnabled"/> 是两个独立开关：那一列决定资源管理器里双击 / 图标归谁，
        /// 这一列只影响应用内打开对话框的默认筛选（拖拽 / 命令行 / 双击不受影响）。
        /// </remarks>
        private bool _isOpenDialogVisible = true;
        public bool IsOpenDialogVisible
        {
            get => _isOpenDialogVisible;
            set { _isOpenDialogVisible = value; OnPropertyChanged(); }
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
