using System.Windows;
using System.Windows.Threading;
using Siemert.DataViewer.App.Services;

namespace Siemert.DataViewer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Selbsttest ohne Oberfläche - siehe SelfTest.
        if (e.Args.Length > 0 && string.Equals(e.Args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
        {
            int code = SelfTest.Run(e.Args);
            Shutdown(code);
            return;
        }

        // Baut jedes Fenster einmal auf, ohne es zu zeigen. Faengt Fehler in den XAML-Dateien,
        // die sonst erst beim Anwender auftraeten - der Uebersetzer prueft XAML nur oberflaechlich.
        if (e.Args.Length > 0 && string.Equals(e.Args[0], "--fenstertest", StringComparison.OrdinalIgnoreCase))
        {
            Shutdown(SelfTest.CheckWindows());
            return;
        }

        base.OnStartup(e);

        AppLog.Prune();
        AppLog.Info($"{AppInfo.ProductName} {AppInfo.Version} gestartet.");

        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += OnBackgroundException;
        TaskScheduler.UnobservedTaskException += OnTaskException;
    }

    /// <summary>
    /// Unerwartete Fehler in der Oberfläche.
    /// </summary>
    /// <remarks>
    /// Die Vorgängerversion hat hier pauschal <c>e.Handled = true</c> gesetzt und weitergemacht.
    /// Bei einem Auswerteprogramm ist das die gefährlichere Variante: Der Anwender sieht dann
    /// womöglich eine halb aufgebaute Auswertung und hält sie für vollständig. Wir melden den
    /// Fehler deshalb deutlich und benennen, welche Anzeige nicht mehr belastbar ist.
    /// </remarks>
    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unbehandelter Fehler in der Oberfläche.", e.Exception);

        MessageBox.Show(
            "In der Anwendung ist ein unerwarteter Fehler aufgetreten.\n\n" +
            e.Exception.Message +
            "\n\nDie aktuell angezeigte Auswertung ist möglicherweise unvollständig und sollte " +
            "nicht weiterverwendet werden. Bitte lesen Sie die Aufzeichnung erneut ein.\n\n" +
            "Einzelheiten stehen im Protokoll:\n" + AppInfo.LogDirectory,
            AppInfo.ProductName,
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private static void OnBackgroundException(object sender, UnhandledExceptionEventArgs e) =>
        AppLog.Error("Unbehandelter Fehler im Hintergrund. Beendet=" + e.IsTerminating,
            e.ExceptionObject as Exception);

    private static void OnTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("Unbeobachteter Fehler in einer Hintergrundaufgabe.", e.Exception);
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info("Beendet (Code " + e.ApplicationExitCode + ").");
        base.OnExit(e);
    }
}
