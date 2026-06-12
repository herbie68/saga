using EbookManager.Application.Books;
using EbookManager.Application.Importing;
using EbookManager.App.Services;
using EbookManager.Domain.Abstractions;
using EbookManager.Infrastructure.Files;
using EbookManager.Infrastructure.Metadata;
using EbookManager.Infrastructure.Persistence;
using EbookManager.Infrastructure.Persistence.Repositories;
using EbookManager.Infrastructure.Settings;
using EbookManager.Libraries;
using EbookManager.Presentation.Abstractions;
using EbookManager.Presentation.Importing;
using EbookManager.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Syncfusion.Licensing;

namespace EbookManager.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? serviceProvider;

    public App()
    {
    }

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            RegisterSyncfusionLicense();
            serviceProvider = BuildServiceProvider();

            await serviceProvider.GetRequiredService<LocalizationService>()
                .ApplySavedCultureAsync(CancellationToken.None);
            await serviceProvider.GetRequiredService<ThemeService>()
                .ApplySavedThemeAsync(CancellationToken.None);

            var startupService = serviceProvider.GetRequiredService<AppStartupService>();
            await startupService.InitializeAsync(CancellationToken.None);

            MainWindow = serviceProvider.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                exception.Message,
                serviceProvider?.GetService<LocalizationService>()?.GetString("StartupFailedTitle")
                    ?? "Ebook Manager startup failed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        serviceProvider = null;
        base.OnExit(e);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<CurrentLibrary>();
        services.AddSingleton<LibraryDbContextFactory>();
        services.AddSingleton<ILibraryDatabaseInitializer, LibraryDatabaseInitializer>();
        services.AddSingleton<LibraryService>();
        services.AddSingleton<AppStartupService>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<DeleteConfirmationService>();
        services.AddSingleton<IUserInteractionService, UserInteractionService>();
        services.AddSingleton<DirectoryScanner>();
        services.AddSingleton<IFileHasher, Sha256FileHasher>();
        services.AddSingleton<IMetadataSidecarStore, JsonMetadataSidecarStore>();
        services.AddSingleton<IImportExceptionClassifier, SqliteImportExceptionClassifier>();
        services.AddSingleton<IMetadataAdapter, FallbackMetadataAdapter>();
        services.AddSingleton<IMetadataAdapter, EpubMetadataAdapter>();
        services.AddSingleton<IMetadataAdapter, CbzMetadataAdapter>();
        services.AddSingleton<IMetadataAdapterResolver, MetadataAdapterResolver>();
        services.AddSingleton<CalibreOpfMetadataSidecarStore>();
        services.AddSingleton<IMetadataSourceResolver, MetadataSourceResolver>();
        services.AddSingleton<BookSearchService>();
        services.AddSingleton<ILibraryFileStore, CurrentLibraryFileStore>();
        services.AddSingleton<IBookRepository, CurrentLibraryBookRepository>();
        services.AddSingleton<IImportRepository, CurrentLibraryImportRepository>();
        services.AddTransient<BookService>();
        services.AddSingleton<ImportService>();
        services.AddSingleton<IImportRunner>(provider => provider.GetRequiredService<ImportService>());
        services.AddSingleton<ImportJobViewModel>();
        services.AddSingleton<IImportAgent, ImportAgent>();
        services.AddTransient<BookDetailsViewModel>();
        services.AddTransient(provider => new LibraryViewModel(
            provider.GetRequiredService<IBookRepository>(),
            provider.GetRequiredService<BookSearchService>(),
            provider.GetRequiredService<BookDetailsViewModel>(),
            provider.GetRequiredService<IUserInteractionService>(),
            provider.GetService<ImportService>(),
            provider.GetService<IImportAgent>(),
            provider.GetService<IImportRepository>(),
            provider.GetService<LibraryService>(),
            provider.GetService<CurrentLibrary>(),
            provider.GetService<BookService>(),
            provider.GetService<ILibraryDatabaseInitializer>(),
            provider.GetService<DirectoryScanner>(),
            provider.GetRequiredService<IAppSettingsStore>()));
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
    }

    private static void RegisterSyncfusionLicense()
    {
        var licenseKey = SyncfusionLicenseKeyResolver.ResolveFromEnvironmentAndLocalFiles();
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return;
        }

        SyncfusionLicenseProvider.RegisterLicense(licenseKey);
    }
}
