using Siemert.DataViewer.App.ViewModels;
using Siemert.DataViewer.Core.Device;
using Siemert.DataViewer.Core.Model;

namespace DataViewer.Core.Tests;

/// <summary>
/// Tests zum Nachfuehren der Geraeteliste.
/// </summary>
/// <remarks>
/// Anlass war ein Befund aus dem Betrieb: Der Logger fiel ohne Zutun aus der Liste und kam
/// Sekunden spaeter zurueck. Ursache war, dass ein einzelner ausgebliebener Ping als Beweis
/// galt. Er ist keiner - das Geraet verbucht jedes Oeffnen und Schliessen des Anschlusses als
/// An- und Abmeldung und ist danach kurz nicht ansprechbar.
/// </remarks>
public sealed class DeviceListTests
{
    private static SerialPortCandidate Port(string name) =>
        new(name, "Prueflogger", "USB\\VID_0403&PID_6001");

    /// <summary>ViewModel mit gesteuerter Geraetesuche und ohne Zugriff auf echte Anschluesse.</summary>
    private static MainViewModel Build(Queue<string[]> scans)
    {
        var vm = new MainViewModel
        {
            ProbeAsync = (_, _, _) => Task.FromResult<IReadOnlyList<SerialPortCandidate>>(
                scans.Count > 0 ? [.. scans.Dequeue().Select(Port)] : []),

            // Ohne diese Vertretung wuerde ein echter Anschluss angesprochen. Die Kopfdaten
            // muessen vorliegen, sonst kommt das Geraet gar nicht erst in die Liste.
            ReadHeaderAsync = port => Task.FromResult<DeviceInfo?>(Header(port))
        };

        return vm;
    }

    private static DeviceInfo Header(string port) => new()
    {
        Model = "SI-TL1",
        ModelCode = "TL1",
        SerialNumber = 300 + port.Length,
        Checksum = "B910",
        RawHeader = string.Empty,
        Source = port
    };

    private static string[] Names(MainViewModel vm) => [.. vm.Devices.Select(d => d.Port.PortName)];

    // Hinweis: Die Meldungen in vm.Notices werden ueber den Dispatcher der WPF-Anwendung
    // eingetragen. Ohne laufende Anwendung gibt es keinen, die Meldungen landen also nicht in
    // der Sammlung und sind hier nicht pruefbar. Geprueft wird der Zustand der Geraeteliste -
    // darum geht es.

    [Fact]
    public async Task Ein_einzelner_ausgebliebener_Ping_entfernt_das_Geraet_nicht()
    {
        var vm = Build(new Queue<string[]>([["COM250"], [], ["COM250"]]));

        await vm.RefreshDevicesAsync(false);
        Assert.Equal(["COM250"], Names(vm));

        // Der Aussetzer. Genau hier verschwand das Geraet bisher aus der Liste.
        await vm.RefreshDevicesAsync(false);
        Assert.Equal(["COM250"], Names(vm));

        await vm.RefreshDevicesAsync(false);
        Assert.Equal(["COM250"], Names(vm));
    }

    [Fact]
    public async Task Nach_zwei_Aussetzern_gilt_das_Geraet_als_entfernt()
    {
        var vm = Build(new Queue<string[]>([["COM250"], [], []]));

        await vm.RefreshDevicesAsync(false);
        await vm.RefreshDevicesAsync(false);
        Assert.NotEmpty(vm.Devices);

        await vm.RefreshDevicesAsync(false);

        // Zweimal in Folge ausgeblieben - jetzt ist es begruendet.
        Assert.Empty(vm.Devices);
    }

    [Fact]
    public async Task Ein_Aussetzer_setzt_den_Zaehler_nicht_dauerhaft_hoch()
    {
        // Aussetzer, Antwort, Aussetzer: Das darf nicht als zwei in Folge zaehlen.
        var vm = Build(new Queue<string[]>([["COM250"], [], ["COM250"], [], ["COM250"]]));

        for (int i = 0; i < 5; i++)
        {
            await vm.RefreshDevicesAsync(false);
            Assert.Equal(["COM250"], Names(vm));
        }
    }

    [Fact]
    public async Task Die_Auswahl_bleibt_ueber_Suchlaeufe_hinweg_bestehen()
    {
        // Vorher wurde die Liste geleert und neu aufgebaut; dabei sprang die Auswahl.
        var vm = Build(new Queue<string[]>([["COM250", "COM251"], ["COM250", "COM251"]]));

        await vm.RefreshDevicesAsync(false);
        vm.SelectedDevice = vm.Devices.First(d => d.Port.PortName == "COM251");

        DeviceItem chosen = vm.SelectedDevice!;
        await vm.RefreshDevicesAsync(false);

        Assert.Same(chosen, vm.SelectedDevice);
        Assert.Equal("COM251", vm.SelectedDevice!.Port.PortName);
    }

    [Fact]
    public async Task Kopfdaten_werden_nur_einmal_je_Geraet_gelesen()
    {
        // Der zweite Grund fuer das Flackern: Die Kopfdaten wurden bei jedem Suchlauf neu
        // geholt und dafuer der Anschluss ein zweites Mal geoeffnet und geschlossen. Sie
        // aendern sich nie.
        var reads = new List<string>();

        var vm = new MainViewModel
        {
            ProbeAsync = (_, _, _) => Task.FromResult<IReadOnlyList<SerialPortCandidate>>([Port("COM250")]),
            ReadHeaderAsync = port =>
            {
                reads.Add(port);
                return Task.FromResult<DeviceInfo?>(new DeviceInfo
                {
                    Model = "SI-TL1",
                    ModelCode = "TL1",
                    SerialNumber = 349,
                    Checksum = "B910",
                    RawHeader = string.Empty
                });
            }
        };

        await vm.RefreshDevicesAsync(false);
        await vm.RefreshDevicesAsync(false);
        await vm.RefreshDevicesAsync(false);

        Assert.Equal(["COM250"], reads);
    }

    [Fact]
    public async Task Ohne_Kopfdaten_erscheint_das_Geraet_nicht_in_der_Liste()
    {
        // Ein Eintrag, der nur "COM250" heisst und sich Sekunden spaeter in "SI-TL1 Nr. 349"
        // verwandelt, sieht aus wie ein Fehler. Solange die Kopfdaten fehlen, ist ausserdem
        // nicht gesichert, dass dort wirklich ein Logger haengt.
        var vm = new MainViewModel
        {
            ProbeAsync = (_, _, _) => Task.FromResult<IReadOnlyList<SerialPortCandidate>>([Port("COM250")]),
            ReadHeaderAsync = _ => Task.FromResult<DeviceInfo?>(null)
        };

        await vm.RefreshDevicesAsync(false);
        Assert.Empty(vm.Devices);

        // Sobald die Kopfdaten kommen, erscheint es.
        vm.ReadHeaderAsync = port => Task.FromResult<DeviceInfo?>(Header(port));
        await vm.RefreshDevicesAsync(false);

        DeviceItem item = Assert.Single(vm.Devices);
        Assert.NotNull(item.Info);
        Assert.Contains("SI-TL1", item.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ein_unlesbares_Geraet_blockiert_die_Liste_nicht()
    {
        // Wirft das Lesen der Kopfdaten, darf das die uebrigen Geraete nicht verhindern.
        var vm = new MainViewModel
        {
            ProbeAsync = (_, _, _) => Task.FromResult<IReadOnlyList<SerialPortCandidate>>(
                [Port("COM250"), Port("COM251")]),
            ReadHeaderAsync = port => port == "COM250"
                ? throw new InvalidOperationException("Anschluss belegt")
                : Task.FromResult<DeviceInfo?>(Header(port))
        };

        await vm.RefreshDevicesAsync(false);

        Assert.Equal(["COM251"], Names(vm));
    }

    [Fact]
    public async Task Ein_neu_hinzugekommenes_Geraet_wird_gemeldet()
    {
        var vm = Build(new Queue<string[]>([["COM250"], ["COM250", "COM251"]]));

        await vm.RefreshDevicesAsync(false);
        Assert.Equal(["COM250"], Names(vm));

        await vm.RefreshDevicesAsync(false);

        // Das neue Geraet kommt hinzu, das bekannte behaelt seinen Platz.
        Assert.Equal(["COM250", "COM251"], Names(vm));
    }
}
