using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using MantisZip.Core.Abstractions;
using MantisZip.UI.Avalonia.Views;

namespace MantisZip.UI.Avalonia;

public partial class App : Application
{
    private const string LightThemeUri = "avares://MantisZip.UI.Avalonia/Themes/ThemeLight.axaml";
    private const string DarkThemeUri = "avares://MantisZip.UI.Avalonia/Themes/ThemeDark.axaml";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // ── Apply system theme ──
        ApplySystemTheme();
        if (PlatformSettings is IPlatformSettings ps)
        {
            ps.ColorValuesChanged += (_, _) =>
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplySystemTheme());
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();

            if (args.Length == 0)
            {
                // No args: normal UI startup
                desktop.MainWindow = new MainWindow();
            }
            else
            {
                var command = args[0].ToLowerInvariant();
                var path = args.Length > 1 ? args[1] : null;

                switch (command)
                {
                    case "--open":
                        // Open archive in MainWindow
                        desktop.MainWindow = new MainWindow();
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            var mainWindow = (MainWindow)desktop.MainWindow;
                            mainWindow.LoadArchiveOnStartup(path);
                        }
                        break;

                    case "--extract":
                        // Extract to same directory as archive
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            ExtractArchive(path, Path.GetDirectoryName(path) ?? ".");
                        desktop.Shutdown();
                        break;

                    case "--extract-here":
                        // Extract to current directory
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            ExtractArchive(path, Directory.GetCurrentDirectory());
                        desktop.Shutdown();
                        break;

                    case "--extract-to-name":
                        // Extract to subfolder named after archive (no extension)
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            var dirName = Path.GetFileNameWithoutExtension(path);
                            // Handle .tar.gz double extension
                            if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                                dirName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
                            var targetDir = Path.Combine(Path.GetDirectoryName(path) ?? ".", dirName);
                            Directory.CreateDirectory(targetDir);
                            ExtractArchive(path, targetDir);
                        }
                        desktop.Shutdown();
                        break;

                    default:
                        // Unknown args: just show UI
                        desktop.MainWindow = new MainWindow();
                        break;
                }
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplySystemTheme()
    {
        if (PlatformSettings is not IPlatformSettings ps) return;
        try
        {
            var isDark = ps.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;

            // ── Swap resource dictionary ──
            var uri = new Uri(isDark ? DarkThemeUri : LightThemeUri);
            Resources.MergedDictionaries.Clear();
            if (AvaloniaXamlLoader.Load(uri) is IResourceProvider themeProvider)
                Resources.MergedDictionaries.Add(themeProvider);

            // ── Set theme variant for FluentTheme ──
            RequestedThemeVariant = isDark
                ? global::Avalonia.Styling.ThemeVariant.Dark
                : global::Avalonia.Styling.ThemeVariant.Light;
        }
        catch
        {
            // Fallback: keep current theme
        }
    }

    private static void ExtractArchive(string archivePath, string targetDir)
    {
        try
        {
            var engine = ArchiveEngineFactory.GetEngineByExtension(archivePath);
            if (engine == null)
            {
                Console.Error.WriteLine($"Unsupported archive format: {archivePath}");
                return;
            }

            engine.ExtractAsync(archivePath, targetDir).GetAwaiter().GetResult();
            Console.WriteLine($"Extracted: {archivePath} -> {targetDir}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Extraction failed: {ex.Message}");
        }
    }
}
