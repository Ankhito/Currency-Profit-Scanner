using System.Numerics;
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
        CurrencyUi.PushTheme();
        if (!ImGui.Begin("Currency Profit Scanner", ref open))
        {
            this.IsOpen = open;
            ImGui.End();
            CurrencyUi.PopTheme();
            return;
        }

        this.IsOpen = open;
        this.DrawHeader();
        if (ImGui.BeginTabBar("##currency-profit-tabs"))
        {
            if (ImGui.BeginTabItem("Home###tab_home"))
            {
                this.DrawHome();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Currencies###tab_currencies"))
            {
                this.DrawToolbar();
                ImGui.Spacing();
                this.DrawCurrencies();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Diagnostics###tab_diagnostics"))
            {
                this.DrawAdvancedDiagnostics(forceOpen: true);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
        ImGui.End();
        CurrencyUi.PopTheme();
    }

    private void DrawHome()
    {
        var width = ImGui.GetContentRegionAvail().X;
        CenterText("Currency Profit Scanner", 1.8f, CurrencyUi.Blue);
        CenterText("Find the spend that will actually sell.", 1f, CurrencyUi.Dimmed);
        ImGui.Spacing();
        CurrencyUi.Section("Guide");
        ImGui.TextWrapped("Pick a currency, refresh Universalis market data for your world or data center, then sort rewards by a sellability-adjusted gil score.");
        ImGui.Spacing();
        DrawGuideCard(width, "Currencies", "Browse spendable currencies, current amounts, and known shop rewards.");
        DrawGuideCard(width, "Market Board Profit", "Compare floor price, 24h sales, units moved, score, and risk label.");
        DrawGuideCard(width, "Unlocks / Collectables", "Keep non-market rewards visible without letting them pollute profit rankings.");
    }

    private static void DrawGuideCard(float width, string title, string desc)
    {
        var h = ImGui.GetTextLineHeightWithSpacing() * 2.4f;
        var p0 = ImGui.GetCursorScreenPos();
        var p1 = new Vector2(p0.X + width, p0.Y + h);
        ImGui.InvisibleButton($"##card_{title}", new Vector2(width, h));
        var hover = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(p0, p1, ImGui.GetColorU32(hover ? new Vector4(0.175f, 0.175f, 0.175f, 1f) : CurrencyUi.Panel), 8f);
        draw.AddRect(p0, p1, ImGui.GetColorU32(hover ? CurrencyUi.Accent with { W = 0.6f } : new Vector4(0.30f, 0.30f, 0.30f, 0.45f)), 8f);
        draw.AddText(new Vector2(p0.X + 14f, p0.Y + 7f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), title);
        draw.AddText(new Vector2(p0.X + 14f, p0.Y + 7f + ImGui.GetTextLineHeight() + 2f), ImGui.GetColorU32(CurrencyUi.Dimmed), desc);
        ImGui.Spacing();
    }

    private void DrawHeader()
    {
        ImGui.SetWindowFontScale(1.55f);
        ImGui.TextColored(CurrencyUi.Blue, "Currency Profit Scanner");
        ImGui.SetWindowFontScale(1f);
        ImGui.TextColored(CurrencyUi.Dimmed, "Spend currencies where Universalis says they actually move.");
        ImGui.Spacing();
        CurrencyUi.Pill(this.scannerService.Currencies.Count == 0 ? "Catalog pending" : $"{this.scannerService.Currencies.Count:N0} currencies", this.scannerService.Currencies.Count == 0 ? CurrencyUi.Red : CurrencyUi.Green);
        ImGui.SameLine();
        CurrencyUi.Pill(this.scannerService.IsRefreshing ? "Refreshing" : "Ready", this.scannerService.IsRefreshing ? CurrencyUi.Gold : CurrencyUi.Blue);
        ImGui.SameLine();
        CurrencyUi.Pill(this.universalisClient.LastFetchUsedCache ? "Cache hit" : "Universalis", CurrencyUi.Dimmed);
        ImGui.Spacing();
    }

    private void DrawToolbar()
    {
        CurrencyUi.Section("Market");
        var world = this.configuration.PreferredWorldOrDc;
        ImGui.SetNextItemWidth(220);
        if (ImGui.InputText("World/DC/Region", ref world, 64))
        {
            this.configuration.PreferredWorldOrDc = world;
            this.configuration.Save();
        }

        if (string.IsNullOrWhiteSpace(this.EffectiveWorldOrDc))
        {
            ImGui.SameLine();
            ImGui.TextColored(CurrencyUi.Gold, "Enter a world, data center, or region before refreshing.");
        }

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

    private string EffectiveWorldOrDc => this.configuration.PreferredWorldOrDc.Trim();

    private static void CenterText(string text, float scale, Vector4 color)
    {
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.SetWindowFontScale(scale);
        var textWidth = ImGui.CalcTextSize(text).X;
        var offset = (width - textWidth) * 0.5f;
        if (offset > 0f)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        }

        ImGui.TextColored(color, text);
        ImGui.SetWindowFontScale(1f);
    }

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
            const string template = """
            {
              "currencies": [
                {
                  "currencyId": 0,
                  "currencyName": "",
                  "iconId": 0,
                  "maxAmount": null,
                  "sourceNotes": "Manual verified"
                }
              ],
              "items": [
                {
                  "currencyId": 0,
                  "currencyName": "",
                  "itemId": 0,
                  "cost": 0,
                  "quantityReceived": 1,
                  "sourceShopName": "",
                  "sourceVendorName": "",
                  "sourceZone": "",
                  "sourceNotes": "",
                  "territoryId": null,
                  "mapId": null,
                  "x": null,
                  "y": null,
                  "z": null,
                  "aetheryteId": null,
                  "lifestreamCommand": null,
                  "verificationSource": ""
                }
              ]
            }
            """;
            File.WriteAllText(path, template);
        }
    }
}
