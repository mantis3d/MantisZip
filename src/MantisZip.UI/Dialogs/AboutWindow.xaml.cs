using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace MantisZip.UI;

/// <summary>
/// 关于窗口，展示应用信息、作者、依赖库和致谢。
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = "v" + AppConstants.Version;
        LoadContributors();
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            App.LogDebug("AboutWindow: failed to open {0}: {1}", e.Uri, ex.Message);
        }
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void LoadContributors()
    {
        LoadContributorList("contributors-technical.csv", ContributorsTechnicalList, ContributorsTechnicalEmpty);
        LoadContributorList("contributors-financial.csv", ContributorsFinancialList, ContributorsFinancialEmpty);
    }

    private void LoadContributorList(string fileName, ItemsControl listControl, TextBlock emptyControl)
    {
        try
        {
            var csvPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (!File.Exists(csvPath))
            {
                ShowEmptyState(listControl, emptyControl);
                return;
            }

            var contributors = new List<Contributor>();
            var lines = File.ReadAllLines(csvPath, Encoding.UTF8);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 2)
                    continue;

                var name = parts[0].Trim();
                if (string.IsNullOrEmpty(name))
                    continue;

                if (int.TryParse(parts[1].Trim(), out var score))
                {
                    contributors.Add(new Contributor { Name = name, Score = score });
                }
            }

            if (contributors.Count == 0)
            {
                ShowEmptyState(listControl, emptyControl);
                return;
            }

            contributors = contributors
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToList();

            listControl.ItemsSource = contributors;
            listControl.Visibility = Visibility.Visible;
            emptyControl.Visibility = Visibility.Collapsed;
        }
        catch (IOException ex)
        {
            App.LogDebug("Contributors: failed to read {0}: {1}", fileName, ex.Message);
            ShowEmptyState(listControl, emptyControl);
        }
        catch (UnauthorizedAccessException ex)
        {
            App.LogDebug("Contributors: access denied {0}: {1}", fileName, ex.Message);
            ShowEmptyState(listControl, emptyControl);
        }
    }

    private static void ShowEmptyState(ItemsControl listControl, TextBlock emptyControl)
    {
        listControl.Visibility = Visibility.Collapsed;
        emptyControl.Visibility = Visibility.Visible;
    }

    private class Contributor
    {
        public string Name { get; init; } = "";
        public int Score { get; init; }
    }
}
