using System.Net.Http;
using System.Windows;
using AndroidTvPcIsoBuilder.Application.Interfaces;
using AndroidTvPcIsoBuilder.Application.Services;
using AndroidTvPcIsoBuilder.Application.Validation;
using AndroidTvPcIsoBuilder.Infrastructure.Download;
using AndroidTvPcIsoBuilder.Infrastructure.FileSystem;
using AndroidTvPcIsoBuilder.Infrastructure.Iso;
using AndroidTvPcIsoBuilder.Infrastructure.Persistence;
using AndroidTvPcIsoBuilder.Presentation.Wpf.Services;
using AndroidTvPcIsoBuilder.Presentation.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AndroidTvPcIsoBuilder.Presentation.Wpf
{
    public partial class App : System.Windows.Application
    {
        private ServiceProvider? _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }

        private static void ConfigureServices(ServiceCollection services)
        {
            services.AddSingleton<IFileSystem, LocalFileSystem>();
            services.AddSingleton<IProjectRepository, JsonProjectRepository>();
            services.AddSingleton<IIsoBuilder, IsoBuilder>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<INotificationService, NotificationService>();

            services.AddSingleton(new HttpClient
            {
                Timeout = TimeSpan.FromHours(2)
            });
            services.AddSingleton<IFileDownloader, HttpFileDownloader>();
            services.AddSingleton<IIsoDownloadService, HttpIsoDownloadService>();
            services.AddSingleton<IsoDownloadOrchestrationService>();
            services.AddSingleton<IAppCatalogService, FDroidAppCatalogService>();
            services.AddSingleton<AppCatalogOrchestrationService>();

            services.AddSingleton<ProjectValidator>();
            services.AddSingleton<ProjectService>();
            services.AddSingleton<AppPackageService>();
            services.AddSingleton<BuildOrchestrationService>();

            services.AddTransient<DownloadIsoViewModel>();
            services.AddTransient<DownloadIsoWindow>();
            services.AddTransient<AppCatalogViewModel>();
            services.AddTransient<AppCatalogWindow>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            services.AddTransient<Func<Guid, ProjectEditorViewModel>>(provider => projectId =>
                new ProjectEditorViewModel(
                    projectId,
                    provider.GetRequiredService<ProjectService>(),
                    provider.GetRequiredService<AppPackageService>(),
                    provider.GetRequiredService<BuildOrchestrationService>(),
                    provider.GetRequiredService<IDialogService>(),
                    provider.GetRequiredService<INotificationService>()));
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }
    }
}
