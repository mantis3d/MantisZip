using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MantisZip.UI.Avalonia.Services;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Dialogs;

/// <summary>
/// 压缩设置窗口。通过 ShowDialog 返回 bool 结果，
/// 调用方在结果为 true 后读取 ViewModel 的属性构造 CompressRequest。
/// </summary>
public partial class CompressSettingsWindow : Window
{
    private bool _loaded;
    /// <summary>
    /// ViewModel，公开属性供调用方关闭后读取。
    /// </summary>
    public CompressSettingsViewModel ViewModel { get; }

    /// <summary>
    /// 设计时需要的无参构造函数。不要直接使用，调用 <see cref="CompressSettingsWindow(IReadOnlyList{string})"/>。
    /// </summary>
    public CompressSettingsWindow()
    {
        InitializeComponent();
        ViewModel = new CompressSettingsViewModel(Array.Empty<string>());
        DataContext = ViewModel;
        SubscribeViewModel();
    }

    public CompressSettingsWindow(IReadOnlyList<string> sourcePaths)
    {
        InitializeComponent();

        ViewModel = new CompressSettingsViewModel(sourcePaths);

        // 设置文件保存浏览回调
        ViewModel.BrowseOutput = async () =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save archive",
                DefaultExtension = ".zip",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("ZIP archives") { Patterns = new[] { "*.zip" } },
                    new FilePickerFileType("7z archives") { Patterns = new[] { "*.7z" } },
                    new FilePickerFileType("TAR.GZ archives") { Patterns = new[] { "*.tar.gz" } },
                }
            });
            return file?.Path?.LocalPath;
        };

        // 设置文件/文件夹选择回调
        ViewModel.PickFiles = async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select files to compress",
                AllowMultiple = true
            });
            return files?.Select(f => f.Path?.LocalPath).Where(p => p != null).ToList()!;
        };

        ViewModel.PickFolder = async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select folder to compress",
                AllowMultiple = false
            });
            return folders.Count >= 1 ? folders[0].Path?.LocalPath : null;
        };

        // 设置关闭回调
        ViewModel.CloseAction = async (result) =>
        {
            Close(result);
            await Task.CompletedTask;
        };

        DataContext = ViewModel;
        SubscribeViewModel();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        FormatOptionsPanel.LoadDefaults();
    }

    /// <summary>
    /// Subscribe to ViewModel PropertyChanged events to update UI elements
    /// that can't be easily bound in XAML.
    /// </summary>
    private void SubscribeViewModel()
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.Password):
            case nameof(ViewModel.ConfirmPassword):
                UpdatePasswordMatchIndicator();
                break;
            case nameof(ViewModel.IsPasswordLibraryMode):
                UpdateSaveCheckLabel();
                break;
        }
    }

    /// <summary>
    /// Update the password match indicator text and visibility.
    /// </summary>
    private void UpdatePasswordMatchIndicator()
    {
        if (PasswordMatchIndicator == null) return;

        if (!string.IsNullOrEmpty(ViewModel.ConfirmPassword))
        {
            if (ViewModel.PasswordsMatch)
            {
                PasswordMatchIndicator.Text = LocalizationManager.T("Compress_Pwd_Match");
                PasswordMatchIndicator.Foreground = new SolidColorBrush(Color.Parse("#4CAF50")); // Green
                PasswordMatchIndicator.IsVisible = true;
            }
            else
            {
                PasswordMatchIndicator.Text = LocalizationManager.T("Compress_Pwd_NoMatch");
                PasswordMatchIndicator.Foreground = new SolidColorBrush(Color.Parse("#F44336")); // Red
                PasswordMatchIndicator.IsVisible = true;
            }
        }
        else
        {
            PasswordMatchIndicator.IsVisible = false;
        }
    }

    /// <summary>
    /// Update the save-to-library checkbox label based on current mode.
    /// </summary>
    private void UpdateSaveCheckLabel()
    {
        // The SaveToLibrary checkbox text is bound to localized strings in XAML,
        // but the label changes based on mode (library vs new password).
        // We handle the dynamic label update here since Avalonia's binding
        // can't easily change between two different string keys.
    }

    /// <summary>
    /// 切换密码显示/隐藏。
    /// </summary>
    private void TogglePasswordReveal(object? sender, RoutedEventArgs e)
    {
        if (PasswordTextBox.PasswordChar == '●')
        {
            PasswordTextBox.PasswordChar = default;
            RevealButton.Content = LocalizationManager.T("Compress_ShowPassword") == "Show" ? "Hide" : "隐藏";
        }
        else
        {
            PasswordTextBox.PasswordChar = '●';
            RevealButton.Content = LocalizationManager.T("Compress_ShowPassword");
        }
    }
}
