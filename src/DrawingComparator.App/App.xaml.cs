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

        // Filet de sécurité : toute exception non gérée est journalisée puis montrée,
        // l'app ne disparaît jamais sans explication.
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            MessageBox.Show(
                $"Une erreur inattendue s'est produite : {args.Exception.Message}\n\nDétails : {CrashLogPath}",
                "DrawingComparator", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.ExceptionObject as Exception);

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

        // DrawingComparator.exe base.pdf [revision.pdf] : chargement direct depuis la ligne de commande.
        var pdfArgs = e.Args.Where(a => a.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToArray();
        var vm = _services.GetRequiredService<MainViewModel>();
        _ = RunStartupSequenceAsync(vm, pdfArgs, e.Args);
    }

    private async Task RunStartupSequenceAsync(MainViewModel vm, string[] pdfPaths, string[] allArgs)
    {
        if (pdfPaths.Length > 0)
            await vm.LoadIntoLayerAsync(vm.BaseLayer, pdfPaths[0]);
        if (pdfPaths.Length > 1)
            await vm.LoadIntoLayerAsync(vm.RevisionLayer, pdfPaths[1]);

        // Outillage design-review : --align entre en mode calage, --screenshot <png> capture
        // la fenêtre par RenderTargetBitmap (fiable sans bureau interactif) puis quitte.
        int screenshotIndex = Array.IndexOf(allArgs, "--screenshot");
        if (allArgs.Contains("--align") && vm.StartAlignmentCommand.CanExecute(null))
            vm.StartAlignmentCommand.Execute(null);

        if (screenshotIndex >= 0 && screenshotIndex + 1 < allArgs.Length)
        {
            await Task.Delay(2500); // laisser la composition asynchrone aboutir
            CaptureWindow(MainWindow!, allArgs[screenshotIndex + 1]);
            Shutdown();
        }
    }

    private static void CaptureWindow(Window window, string outputPath)
    {
        var target = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)window.ActualWidth, (int)window.ActualHeight, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        target.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
        using var stream = System.IO.File.Create(outputPath);
        encoder.Save(stream);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private static string CrashLogPath
        => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "drawingcomparator-crash.log");

    private static void LogCrash(Exception? ex)
    {
        try
        {
            System.IO.File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // le filet de sécurité ne doit jamais lever à son tour
        }
    }
}
