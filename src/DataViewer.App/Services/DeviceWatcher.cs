using System.Management;
using System.Windows.Threading;

namespace Siemert.DataViewer.App.Services;

/// <summary>
/// Meldet, wenn Geräte am Rechner angesteckt oder abgezogen werden.
/// </summary>
/// <remarks>
/// <para>
/// Ohne diese Überwachung merkt die Anwendung nicht, dass der Logger zwischendurch abgezogen war.
/// Sie hält dann einen Anschluss für verbunden, den es nicht mehr gibt, und ein anschließendes
/// Auslesen läuft ins Leere.
/// </para>
/// <para>
/// Windows meldet hier jedes beliebige Geräteereignis, nicht nur den Logger. Die Meldungen kommen
/// außerdem gehäuft, weil ein einziges USB-Gerät beim Anstecken mehrere Ereignisse auslöst.
/// Deshalb wird entprellt.
/// </para>
/// <para>
/// Wichtig: Die daraufhin ausgelöste Gerätesuche darf niemals einen Anschluss anfassen, der
/// gerade gelesen wird. Genau dieser Fehler hat in Fassung 1 laufende Übertragungen vernichtet —
/// dort genügte ein eingesteckter USB-Stick, um einen Auslesevorgang kurz vor dem Ende
/// kommentarlos zu verwerfen. Die Absicherung sitzt in der Gerätesuche selbst, die belegte
/// Anschlüsse übergeht.
/// </para>
/// <para>
/// <b>Grenze dieser Ueberwachung.</b> Gemeldet wird nur, was am USB-Anschluss des Rechners
/// geschieht. Die USB-Schnittstelle sitzt aber im Adapter, nicht im Logger - damit das Geraet
/// kompakt bleibt. Wird der Logger vom Adapter getrennt, bleibt der Adapter angemeldet, der
/// Anschluss besteht weiter und es entsteht <b>kein</b> Geraeteereignis. Dass das Geraet weg
/// ist, laesst sich deshalb ausschliesslich daran erkennen, dass es auf den Ping nicht mehr
/// antwortet. Diese Pruefung gehoert in die Verbindung, nicht hierher - siehe
/// <see cref="Core.Device.LoggerConnection.HandshakeAsync"/>.
/// </para>
/// </remarks>
public sealed class DeviceWatcher : IDisposable
{
    private const int DebounceMs = 900;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private ManagementEventWatcher? _arrival;
    private ManagementEventWatcher? _removal;
    private bool _disposed;

    public DeviceWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _debounce = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(DebounceMs)
        };

        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Wird ausgelöst, nachdem sich die Gerätelandschaft geändert hat und Ruhe eingekehrt ist.</summary>
    public event EventHandler? DevicesChanged;

    public bool IsRunning => _arrival is not null || _removal is not null;

    public void Start()
    {
        if (_disposed || IsRunning)
        {
            return;
        }

        try
        {
            // EventType 2 = angesteckt, 3 = abgezogen.
            _arrival = Create(2);
            _removal = Create(3);
            _arrival.Start();
            _removal.Start();
            AppLog.Info("Geräteüberwachung gestartet.");
        }
        catch (ManagementException ex)
        {
            // Ohne WMI bleibt die Suche über die Schaltfläche. Kein Grund, die Anwendung zu stören.
            AppLog.Warn("Geräteüberwachung nicht verfügbar: " + ex.Message);
            Stop();
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLog.Warn("Geräteüberwachung nicht erlaubt: " + ex.Message);
            Stop();
        }
    }

    private ManagementEventWatcher Create(int eventType)
    {
        var watcher = new ManagementEventWatcher(
            new WqlEventQuery($"SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = {eventType}"));

        watcher.EventArrived += OnEvent;
        return watcher;
    }

    private void OnEvent(object sender, EventArrivedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // Aus einem WMI-Thread heraus; das Entprellen gehört auf den Oberflächen-Thread.
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            _debounce.Stop();
            _debounce.Start();
        });
    }

    public void Stop()
    {
        _debounce.Stop();
        Dispose(_arrival);
        Dispose(_removal);
        _arrival = null;
        _removal = null;
    }

    private void Dispose(ManagementEventWatcher? watcher)
    {
        if (watcher is null)
        {
            return;
        }

        try
        {
            watcher.EventArrived -= OnEvent;
            watcher.Stop();
        }
        catch (ManagementException)
        {
            // Beim Beenden unerheblich.
        }
        finally
        {
            watcher.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
