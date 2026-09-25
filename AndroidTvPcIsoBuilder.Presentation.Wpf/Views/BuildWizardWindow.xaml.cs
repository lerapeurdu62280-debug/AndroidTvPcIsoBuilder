using System.Windows;
using AndroidTvPcIsoBuilder.Presentation.Wpf.Services;
using AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf.Views
{
    public partial class BuildWizardWindow : Window
    {
        public BuildWizardWindow(BuildWizardViewModel viewModel)
        {
            InitializeComponent();
            WindowChromeHelper.ApplyDarkTitleBar(this);
            DataContext = viewModel;
        }
    }
}
