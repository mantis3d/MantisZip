using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MantisZip.UI.Avalonia.ViewModels;

namespace MantisZip.UI.Avalonia.Views;

public partial class PreviewPanel : UserControl
{
    public PreviewPanel()
    {
        InitializeComponent();

        this.DataContextChanged += (_, _) =>
        {
            if (this.DataContext is PreviewViewModel vm)
            {
                vm.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(PreviewViewModel.IsHtmlVisible) ||
                        args.PropertyName == nameof(PreviewViewModel.HtmlContent))
                    {
                        UpdateWebViewContent(vm);
                    }
                    if (args.PropertyName == nameof(PreviewViewModel.InfoPanelOrientation))
                    {
                        ApplyInfoPanelOrientation(vm.InfoPanelOrientation);
                    }
                };
                ApplyInfoPanelOrientation(vm.InfoPanelOrientation);
            }
        };
    }

    public void ApplyInfoPanelOrientation(string orientation)
    {
        if (PreviewInfoBorder == null) return;
        var isVertical = orientation == "Vertical";

        if (isVertical)
        {
            // Info panel below content
            Grid.SetRow(PreviewInfoBorder, 2);
            Grid.SetColumn(PreviewInfoBorder, 0);
            Grid.SetRowSpan(PreviewInfoBorder, 1);
            PreviewRootGrid.RowDefinitions[2].Height = GridLength.Auto;
            PreviewRootGrid.ColumnDefinitions[1].Width = new GridLength(0);
            PreviewInfoBorder.Width = double.NaN;
            PreviewInfoBorder.MaxWidth = double.PositiveInfinity;
            PreviewInfoBorder.BorderThickness = new Thickness(0, 1, 0, 0);
            PreviewInfoBorder.Margin = new Thickness(0, 8, 0, 0);
            PreviewInfoBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            // Info panel to the right of content (default)
            Grid.SetRow(PreviewInfoBorder, 0);
            Grid.SetColumn(PreviewInfoBorder, 1);
            Grid.SetRowSpan(PreviewInfoBorder, 2);
            PreviewRootGrid.RowDefinitions[2].Height = new GridLength(0);
            PreviewRootGrid.ColumnDefinitions[1].Width = new GridLength(220, GridUnitType.Pixel);
            PreviewInfoBorder.Width = 220;
            PreviewInfoBorder.MaxWidth = 220;
            PreviewInfoBorder.BorderThickness = new Thickness(1, 0, 0, 0);
            PreviewInfoBorder.Margin = new Thickness(0);
            PreviewInfoBorder.HorizontalAlignment = HorizontalAlignment.Left;
        }
    }

    private void UpdateWebViewContent(PreviewViewModel vm)
    {
        if (vm.IsHtmlVisible && !string.IsNullOrEmpty(vm.HtmlContent))
        {
            // data URI: embed HTML inline so no temp files needed
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(vm.HtmlContent));
            HtmlWebView.Source = new Uri($"data:text/html;base64,{base64}");
        }
    }
}
