using System.Windows;

using DrawingComparator.App.Services;
using DrawingComparator.App.ViewModels;
using DrawingComparator.Core;

using Microsoft.Extensions.DependencyInjection;

namespace DrawingComparator.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private int _dispatcherErrorCount;
    private DateTime _dispatcherErrorWindowStart = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Filet de sécurité : toute exception non gérée est journalisée puis montrée,
        // l'app ne disparaît jamais sans explication.
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);

            // Les exceptions d'état irrécupérable ne se rattrapent pas : mieux vaut
            // un crash franc qu'un export silencieusement faux.
            if (args.Exception is OutOfMemoryException)
                return;

            // Garde anti-rafale : au-delà de 3 erreurs en 10 s, une MessageBox par
            // événement souris rendrait l'app inutilisable — on s'arrête proprement.
            var now = DateTime.UtcNow;
            if ((now - _dispatcherErrorWindowStart).TotalSeconds > 10)
            {
                _dispatcherErrorWindowStart = now;
                _dispatcherErrorCount = 0;
            }
            if (++_dispatcherErrorCount > 3)
            {
                MessageBox.Show(
                    $"Erreurs répétées — l'application va se fermer.\nDétails : {CrashLogPath}",
                    "DrawingComparator", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            MessageBox.Show(
                $"Une erreur inattendue s'est produite : {args.Exception.Message}\n\nDétails : {CrashLogPath}",
                "DrawingComparator", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            LogCrash(args.Exception);

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

        var vm = _services.GetRequiredService<MainViewModel>();
        _ = RunStartupSequenceAsync(vm, e.Args);
    }

    /// <summary>
    /// Séquence de démarrage :
    /// - `DrawingComparator.exe base.pdf [revision.pdf]` charge les plans directement ;
    /// - `--align` et `--screenshot &lt;png&gt;` sont l'outillage de design-review, volontairement
    ///   présent dans l'exe Release (arbitrage dev-council n°1 : une revue capture le binaire
    ///   réellement distribué, pas un build Debug).
    /// Toute erreur est journalisée ; en mode capture, l'app quitte toujours (code 1 si échec).
    /// </summary>
    private async Task RunStartupSequenceAsync(MainViewModel vm, string[] args)
    {
        int screenshotIndex = Array.IndexOf(args, "--screenshot");
        bool screenshotMode = screenshotIndex >= 0 && screenshotIndex + 1 < args.Length;
        try
        {
            var pdfPaths = args.Where(a => a.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (pdfPaths.Length > 0)
                await vm.LoadIntoLayerAsync(vm.BaseLayer, pdfPaths[0]);
            if (pdfPaths.Length > 1)
                await vm.LoadIntoLayerAsync(vm.RevisionLayer, pdfPaths[1]);

            if (args.Contains("--align") && vm.StartAlignmentCommand.CanExecute(null))
                vm.StartAlignmentCommand.Execute(null);

            if (screenshotMode)
            {
                // Synchronisation sur la composition réelle (pas de délai magique) :
                // on attend la fin de la boucle de recomposition en vol, avec timeout.
                var recompose = vm.CurrentRecompose ?? Task.CompletedTask;
                await Task.WhenAny(recompose, Task.Delay(TimeSpan.FromSeconds(8)));
                await Task.Delay(300); // laisser le binding pousser le WriteableBitmap à l'écran

                CaptureWindow(MainWindow!, args[screenshotIndex + 1]);
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            if (screenshotMode)
                Shutdown(1); // jamais de hang silencieux du pipeline de capture
        }
    }

    private static void CaptureWindow(Window window, string outputPath)
    {
        if (window.ActualWidth < 1 || window.ActualHeight < 1)
            throw new InvalidOperationException("Fenêtre sans surface à capturer (minimisée ?).");
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