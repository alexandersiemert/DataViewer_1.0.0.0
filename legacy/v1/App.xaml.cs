using System;
using System.Windows;
using System.Windows.Threading;

namespace DataViewer_1._0._0._0
{
    /// <summary>
    /// Interaktionslogik für "App.xaml"
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Logger.Info("Anwendung gestartet. Log-Verzeichnis: " + Logger.LogDirectory);

            // UI-Thread-Ausnahmen abfangen, damit die App nicht kommentarlos verschwindet.
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            // Ausnahmen aus Hintergrund-Threads (z.B. SerialPort) protokollieren.
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error("Unbehandelte UI-Ausnahme.", e.Exception);
            MessageBox.Show(
                "Ein unerwarteter Fehler ist aufgetreten. Details wurden protokolliert unter:\n" + Logger.LogDirectory,
                "SIEMERT DataViewer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            // Anwendung nicht abstürzen lassen; der Fehler ist protokolliert.
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Logger.Error("Unbehandelte Ausnahme (AppDomain). Terminating=" + e.IsTerminating,
                e.ExceptionObject as Exception);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info("Anwendung beendet (ExitCode=" + e.ApplicationExitCode + ").");
            base.OnExit(e);
        }
    }
}
