using System.IO;
using System.Linq;
using System.Windows;
using AndroidTvPcIsoBuilder.Presentation.Wpf.Services;
using AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            WindowChromeHelper.ApplyDarkTitleBar(this);
            _viewModel = viewModel;
            DataContext = _viewModel;
            Loaded += async (_, _) => await _viewModel.InitializeAsync();
        }

        private void AppsDropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void AppsDropZone_Drop(object sender, DragEventArgs e)
        {
            if (_viewModel.CurrentEditor is not { } editor)
                return;

            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
                return;

            foreach (var path in paths.Where(p => p.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)))
            {
                await editor.AddAppFromPathAsync(path, Path.GetFileNameWithoutExtension(path));
            }
        }
    }
}
