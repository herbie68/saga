using EbookManager.Application.Books;
using EbookManager.Application.Importing;
using EbookManager.Domain.Abstractions;
using EbookManager.Infrastructure.Files;
using EbookManager.Infrastructure.Metadata;
using EbookManager.Infrastructure.Persistence;
using EbookManager.Infrastructure.Persistence.Repositories;
using EbookManager.Infrastructure.Settings;
using EbookManager.Libraries;
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

            var startupService = serviceProvider.GetRequiredService<AppStartupService>();
            await startupService.InitializeAsync(CancellationToken.None);

            MainWindow = serviceProvider.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                exception.Message,
                "Ebook Manager startup failed",
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
        services.AddSingleton<DirectoryScanner>();
        services.AddSingleton<Sha256FileHasher>();
        services.AddSingleton<IImportExceptionClassifier, SqliteImportExceptionClassifier>();
        services.AddSingleton<IMetadataAdapter, FallbackMetadataAdapter>();
        services.AddSingleton<IMetadataAdapter, EpubMetadataAdapter>();
        services.AddSingleton<IMetadataAdapter, CbzMetadataAdapter>();
        services.AddSingleton<IMetadataAdapterResolver, MetadataAdapterResolver>();
        services.AddSingleton<BookSearchService>();
        services.AddTransient<ILibraryFileStore>(sp =>
        {
            var currentLibrary = sp.GetRequiredService<CurrentLibrary>().Current;
            if (currentLibrary is null)
            {
                throw new InvalidOperationException("No active library is loaded.");
            }

            return new ManagedLibraryFileStore(currentLibrary.DirectoryPath);
        });
        services.AddTransient<IBookRepository>(sp =>
        {
            var currentLibrary = sp.GetRequiredService<CurrentLibrary>().Current;
            if (currentLibrary is null)
            {
                throw new InvalidOperationException("No active library is loaded.");
            }

            return new EfBookRepository(
                sp.GetRequiredService<LibraryDbContextFactory>(),
                currentLibrary.DirectoryPath);
        });
        services.AddTransient<IImportRepository>(sp =>
        {
            var currentLibrary = sp.GetRequiredService<CurrentLibrary>().Current;
            if (currentLibrary is null)
            {
                throw new InvalidOperationException("No active library is loaded.");
            }

            return new EfImportRepository(
                sp.GetRequiredService<LibraryDbContextFactory>(),
                currentLibrary.DirectoryPath);
        });
        services.AddTransient<BookService>();
        services.AddTransient<ImportService>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private static void RegisterSyncfusionLicense()
    {
        var licenseKey = Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY");
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return;
        }

        SyncfusionLicenseProvider.RegisterLicense(licenseKey);
    }
}
