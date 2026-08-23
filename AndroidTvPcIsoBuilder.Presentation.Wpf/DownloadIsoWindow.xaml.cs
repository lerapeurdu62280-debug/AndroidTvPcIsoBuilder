using System.Windows;
using AndroidTvPcIsoBuilder.Presentation.Wpf.Services;
using AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf
{
    public partial class DownloadIsoWindow : Window
    {
        public DownloadIsoViewModel ViewModel { get; }

        public DownloadIsoWindow(DownloadIsoViewModel viewModel)
        {
            InitializeComponent();
            WindowChromeHelper.ApplyDarkTitleBar(this);
            ViewModel = viewModel;
            DataContext = ViewModel;

            ViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DownloadIsoViewModel.CompletedSuccessfully) && ViewModel.CompletedSuccessfully)
                {
                    DialogResult = true;
                }
            };
        }
    }
}
