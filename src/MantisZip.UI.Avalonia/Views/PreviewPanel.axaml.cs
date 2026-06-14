using System.Text;
using Avalonia.Controls;
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
                };
            }
        };
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
