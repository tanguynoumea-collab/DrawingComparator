using System.Windows;
using DrawingComparator.App.Services;
using DrawingComparator.App.ViewModels;
using DrawingComparator.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DrawingComparator.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<IPdfDocumentService, PdfDocumentService>();
        services.AddSingleton<IComparisonCompositor, ComparisonCompositor>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IUserDialogs, UserDialogs>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        MainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
