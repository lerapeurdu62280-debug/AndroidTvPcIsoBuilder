using System.Windows;
using AndroidTvPcIsoBuilder.Presentation.Wpf.Services;
using AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf
{
    public partial class AppCatalogWindow : Window
    {
        public AppCatalogViewModel ViewModel { get; }

        public AppCatalogWindow(AppCatalogViewModel viewModel)
        {
            InitializeComponent();
            WindowChromeHelper.ApplyDarkTitleBar(this);
            ViewModel = viewModel;
            DataContext = ViewModel;

            ViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppCatalogViewModel.CompletedSuccessfully) && ViewModel.CompletedSuccessfully)
                {
                    DialogResult = true;
                }
            };
        }
    }
}
