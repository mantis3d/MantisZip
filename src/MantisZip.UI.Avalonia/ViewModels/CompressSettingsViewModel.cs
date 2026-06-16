using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MantisZip.Core.Abstractions;

namespace MantisZip.UI.Avalonia.ViewModels;

/// <summary>
/// 压缩设置对话框的 ViewModel。
/// 用户选择格式、压缩级别、输出路径、密码和注释，通过 CloseAction 回调返回结果。
/// </summary>
public partial class CompressSettingsViewModel : ObservableObject
{
    /// <summary>源文件/目录路径列表（输入，显示用）。</summary>
    public IReadOnlyList<string> SelectedPaths { get; }

    /// <summary>源文件摘要文字，用于界面显示。</summary>
    public string SelectedPathsSummary => SelectedPaths.Count > 0
        ? $"{SelectedPaths.Count} item(s) selected"
        : "No files selected";

    /// <summary>由 View 设置的文件保存选择回调。返回选择的路径，取消返回 null。</summary>
    public Func<Task<string?>>? BrowseOutput { get; set; }

    /// <summary>由 View 设置的关闭回调。参数 true=确认压缩，false=取消。</summary>
    public Func<bool, Task>? CloseAction { get; set; }

    [ObservableProperty]
    private string _defaultFormat = "zip";

    [ObservableProperty]
    private int _compressionLevel = 5;

    [ObservableProperty]
    private string? _outputPath;

    [ObservableProperty]
    private string? _password;

    [ObservableProperty]
    private string? _confirmPassword;

    [ObservableProperty]
    private bool _encrypt;

    [ObservableProperty]
    private string? _comment;

    [ObservableProperty]
    private CommentDistribution _commentDistribution = CommentDistribution.AllSame;

    [ObservableProperty]
    private string _windowTitle = "Compress Settings";

    // -- Comment radio button backing (sync via partial methods)

    [ObservableProperty]
    private bool _commentAllSame = true;

    [ObservableProperty]
    private bool _commentFirstOnly;

    [ObservableProperty]
    private bool _commentPerLine;

    /// <summary>密码与确认密码是否匹配。</summary>
    public bool PasswordsMatch => Password == ConfirmPassword;

    /// <summary>密码强度描述（None / Weak / Medium / Strong）。</summary>
    public string PasswordStrength
    {
        get
        {
            if (string.IsNullOrEmpty(Password))
                return "None";

            if (Password.Length < 4)
                return "Weak";

            bool hasUpper = Password.Any(char.IsUpper);
            bool hasLower = Password.Any(char.IsLower);
            bool hasDigit = Password.Any(char.IsDigit);
            bool hasSpecial = Password.Any(c => !char.IsLetterOrDigit(c));

            int types = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);

            if (Password.Length >= 12 && types >= 3)
                return "Strong";

            if (Password.Length >= 8 && types >= 2)
                return "Medium";

            return "Weak";
        }
    }

    public CompressSettingsViewModel(IReadOnlyList<string> sourcePaths)
    {
        SelectedPaths = sourcePaths;
    }

    partial void OnPasswordChanged(string? value)
    {
        OnPropertyChanged(nameof(PasswordStrength));
        OnPropertyChanged(nameof(PasswordsMatch));
    }

    partial void OnConfirmPasswordChanged(string? value)
    {
        OnPropertyChanged(nameof(PasswordsMatch));
    }

    partial void OnCommentAllSameChanged(bool value)
    {
        if (value)
        {
            CommentFirstOnly = false;
            CommentPerLine = false;
            CommentDistribution = CommentDistribution.AllSame;
        }
    }

    partial void OnCommentFirstOnlyChanged(bool value)
    {
        if (value)
        {
            CommentAllSame = false;
            CommentPerLine = false;
            CommentDistribution = CommentDistribution.FirstOnly;
        }
    }

    partial void OnCommentPerLineChanged(bool value)
    {
        if (value)
        {
            CommentAllSame = false;
            CommentFirstOnly = false;
            CommentDistribution = CommentDistribution.PerLine;
        }
    }

    [RelayCommand]
    private async Task BrowseOutputPath()
    {
        if (BrowseOutput == null) return;
        var path = await BrowseOutput();
        if (!string.IsNullOrEmpty(path))
        {
            OutputPath = path;
        }
    }

    [RelayCommand]
    private async Task StartCompress()
    {
        // Validate passwords match if encrypting
        if (Encrypt && !PasswordsMatch)
        {
            return;
        }

        if (CloseAction != null)
            await CloseAction(true);
    }

    [RelayCommand]
    private async Task Cancel()
    {
        if (CloseAction != null)
            await CloseAction(false);
    }
}
