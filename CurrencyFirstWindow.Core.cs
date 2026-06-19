using Dalamud.Bindings.ImGui;

namespace CurrencyProfitScanner;

public sealed partial class CurrencyFirstWindow : IDisposable
{
    private readonly PluginConfiguration configuration;
    private readonly ProfitScannerService scannerService;
    private readonly UniversalisClient universalisClient;
    private readonly IpcDiagnosticsService ipcDiagnosticsService;
    private readonly NavigationIpcService navigationIpcService;
    private bool detailOpen;

    public CurrencyFirstWindow(
        PluginConfiguration configuration,
        ProfitScannerService scannerService,
        UniversalisClient universalisClient,
        IpcDiagnosticsService ipcDiagnosticsService,
        NavigationIpcService navigationIpcService)
    {
        this.configuration = configuration;
        this.scannerService = scannerService;
        this.universalisClient = universalisClient;
        this.ipcDiagnosticsService = ipcDiagnosticsService;
        this.navigationIpcService = navigationIpcService;
    }

    public bool IsOpen { get; set; }

    public void Draw()
    {
        this.DrawMainWindow();
        this.DrawDetailWindow();
    }

    public void Dispose()
    {
    }

    private void DrawMainWindow()
    {
        if (!this.IsOpen)
        {
            return;
        }

        var open = this.IsOpen;
        if (!ImGui.Begin("Currency Profit Scanner", ref open))
        {
            this.IsOpen = open;
            ImGui.End();
            return;
        }

        this.IsOpen = open;
        this.DrawToolbar();
        ImGui.Separator();
        this.DrawCurrencies();
        this.DrawAdvancedDiagnostics();
        ImGui.End();
    }

    private void DrawToolbar()
    {
        var world = this.configuration.PreferredWorldOrDc;
        ImGui.SetNextItemWidth(220);
        if (ImGui.InputText("World/DC/Region", ref world, 64))
        {
            this.configuration.PreferredWorldOrDc = world;
            this.configuration.Save();
        }

        ImGui.SameLine();
        var minSales = this.configuration.MinimumSales24h;
        ImGui.SetNextItemWidth(90);
        if (ImGui.InputInt("Min sales", ref minSales))
        {
            this.configuration.MinimumSales24h = Math.Max(0, minSales);
            this.configuration.Save();
        }

        ImGui.SameLine();
        if (ImGui.Button("Reload"))
        {
            this.scannerService.ReloadCandidates();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(this.scannerService.Status);
    }

    private void Cell(string value)
    {
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(value);
    }

    private static string FormatGil(double? value) => value is null ? "Unknown" : value.Value.ToString("N2");

    private static string SourceText(SpendableCurrencyItem item)
    {
        return string.Join(" / ", new[] { item.SourceShopName, item.SourceVendorName, item.SourceZone }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private void CreateSeedTemplate()
    {
        var path = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "currency-candidates.json");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "{\n  \"currencies\": [],\n  \"items\": []\n}\n");
        }
    }
}
