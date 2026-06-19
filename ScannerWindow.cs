using Dalamud.Bindings.ImGui;

namespace CurrencyProfitScanner;

public sealed class ScannerWindow : IDisposable
{
    private readonly PluginConfiguration configuration;
    private readonly ProfitScannerService scannerService;
    private readonly UniversalisClient universalisClient;
    private readonly IpcDiagnosticsService ipcDiagnosticsService;
    private readonly LuminaDiscoveryService luminaDiscoveryService;
    private IReadOnlyList<LuminaSheetDiscovery>? luminaDiscovery;
    private bool detailOpen;

    public ScannerWindow(
        PluginConfiguration configuration,
        ProfitScannerService scannerService,
        UniversalisClient universalisClient,
        IpcDiagnosticsService ipcDiagnosticsService,
        LuminaDiscoveryService luminaDiscoveryService)
    {
        this.configuration = configuration;
        this.scannerService = scannerService;
        this.universalisClient = universalisClient;
        this.ipcDiagnosticsService = ipcDiagnosticsService;
        this.luminaDiscoveryService = luminaDiscoveryService;
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

        var isOpen = this.IsOpen;
        if (!ImGui.Begin("Currency Profit Scanner", ref isOpen))
        {
            this.IsOpen = isOpen;
            ImGui.End();
            return;
        }

        this.IsOpen = isOpen;
        this.DrawTopControls();
        ImGui.Separator();

        if (this.scannerService.Currencies.Count == 0)
        {
            ImGui.TextWrapped($"No verified currency candidates loaded. Add manually verified rows to currency-candidates.json in the plugin config directory, then click Reload Candidates.");
            this.DrawDiagnostics();
            ImGui.End();
            return;
        }

        this.DrawCurrencyTable();
        this.DrawDiagnostics();
        ImGui.End();
    }

    private void DrawTopControls()
    {
        var worldOrDc = this.configuration.PreferredWorldOrDc;
        if (ImGui.InputText("World/DC/Region", ref worldOrDc, 64))
        {
            this.configuration.PreferredWorldOrDc = worldOrDc;
            this.configuration.Save();
        }

        var minSales = this.configuration.MinimumSales24h;
        if (ImGui.InputInt("Minimum sales 24h", ref minSales))
        {
            this.configuration.MinimumSales24h = Math.Max(0, minSales);
            this.configuration.Save();
        }

        if (ImGui.Button("Reload Candidates"))
        {
            this.scannerService.ReloadCandidates();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(this.scannerService.Status);
    }

    private void DrawCurrencyTable()
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable;
        if (!ImGui.BeginTable("currency-list", 8, flags))
        {
            return;
        }

        ImGui.TableSetupColumn("Icon");
        ImGui.TableSetupColumn("Currency");
        ImGui.TableSetupColumn("Amount");
        ImGui.TableSetupColumn("Full");
        ImGui.TableSetupColumn("Candidates");
        ImGui.TableSetupColumn("Best gil/currency");
        ImGui.TableSetupColumn("Best item");
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.NoSort);
        ImGui.TableHeadersRow();

        foreach (var currency in this.scannerService.Currencies)
        {
            var candidateCount = this.scannerService.GetItemsForCurrency(currency).Count;
            ImGui.TableNextRow();
            this.Cell(currency.IconId == 0 ? "-" : $"#{currency.IconId}");
            this.Cell(currency.Name);
            this.Cell(currency.CurrentAmount is null ? "Unknown" : $"{currency.CurrentAmount:N0}/{currency.MaxAmount?.ToString("N0") ?? "?"}");
            this.Cell(currency.CurrentAmount is not null && currency.MaxAmount is > 0
                ? $"{currency.CurrentAmount.Value * 100d / currency.MaxAmount.Value:N0}%"
                : "Unknown");
            this.Cell(candidateCount.ToString("N0"));
            this.Cell(FormatGil(this.scannerService.GetBestGilPerCurrency(currency)));
            this.Cell(this.scannerService.GetBestItemName(currency) ?? "Unknown");
            ImGui.TableNextColumn();
            if (ImGui.Button($"Analyze##{currency.CurrencyId}-{currency.Name}"))
            {
                this.scannerService.SelectCurrency(currency);
                this.detailOpen = true;
            }
        }

        ImGui.EndTable();
    }

    private void DrawDetailWindow()
    {
        if (!this.detailOpen || this.scannerService.SelectedCurrency is not { } currency)
        {
            return;
        }

        var open = this.detailOpen;
        if (!ImGui.Begin($"Spend {currency.Name}###currency-detail", ref open))
        {
            this.detailOpen = open;
            ImGui.End();
            return;
        }

        this.detailOpen = open;
        this.DrawDetailHeader(currency);
        ImGui.Separator();
        this.DrawSellableSection(currency);
        this.DrawCollectablesSection();
        this.DrawDiagnostics();
        ImGui.End();
    }

    private void DrawDetailHeader(TrackedCurrencyModel currency)
    {
        ImGui.TextUnformatted(currency.IconId == 0 ? "[no icon]" : $"Icon #{currency.IconId}");
        ImGui.SameLine();
        ImGui.TextUnformatted(currency.Name);
        ImGui.TextUnformatted($"Amount: {(currency.CurrentAmount is null ? "Unknown" : currency.CurrentAmount.Value.ToString("N0"))} / {(currency.MaxAmount is null ? "Unknown" : currency.MaxAmount.Value.ToString("N0"))}");
        ImGui.TextUnformatted($"Candidates: {this.scannerService.GetItemsForCurrency(currency).Count:N0}");
        ImGui.TextUnformatted($"Market status: {this.universalisClient.Status}");

        if (ImGui.Button(this.scannerService.IsRefreshing ? "Refreshing..." : "Refresh Market Data") && !this.scannerService.IsRefreshing)
        {
            _ = this.scannerService.RefreshSelectedCurrencyAsync(this.configuration.PreferredWorldOrDc);
        }

        if (this.scannerService.LastRefreshUtc is { } lastRefresh)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted($"Last refresh: {(DateTimeOffset.UtcNow - lastRefresh).TotalMinutes:N0}m ago");
        }
    }

    private void DrawSellableSection(TrackedCurrencyModel currency)
    {
        if (!ImGui.CollapsingHeader("Sellable on Market Board", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        var candidates = this.scannerService.GetItemsForCurrency(currency);
        if (candidates.Count == 0)
        {
            ImGui.TextWrapped("This currency has no verified marketable candidates. Add verified rows to currency-candidates.json.");
            return;
        }

        var rows = this.scannerService.Results.Count == 0
            ? candidates.Select(item => new ProfitResult(
                item,
                new MarketSnapshot(item.ItemId, null, 0, 0, null, null, null, null, null, null, null, "Not fetched"),
                null,
                null,
                0,
                0,
                "Unknown")).ToList()
            : this.scannerService.Results;

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollX;
        if (!ImGui.BeginTable("currency-sellables", 14, flags))
        {
            return;
        }

        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Sales 24h");
        ImGui.TableSetupColumn("Units 24h");
        ImGui.TableSetupColumn("Last sale");
        ImGui.TableSetupColumn("Cost");
        ImGui.TableSetupColumn("Qty");
        ImGui.TableSetupColumn("Can buy");
        ImGui.TableSetupColumn("Floor");
        ImGui.TableSetupColumn("Conservative");
        ImGui.TableSetupColumn("Gil/currency");
        ImGui.TableSetupColumn("Expected total");
        ImGui.TableSetupColumn("Confidence");
        ImGui.TableSetupColumn("Source");
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort);
        ImGui.TableHeadersRow();

        foreach (var result in rows)
        {
            var item = result.Item;
            ImGui.TableNextRow();
            this.Cell(item.IconId == 0 ? item.ItemName : $"#{item.IconId} {item.ItemName}");
            this.Cell(result.Market.Sales24h.ToString("N0"));
            this.Cell(result.Market.UnitsSold24h.ToString("N0"));
            this.Cell(result.Market.LastSaleAgeHours is null ? "Unknown" : $"{result.Market.LastSaleAgeHours:N1}h");
            this.Cell(item.Cost.ToString("N0"));
            this.Cell(item.QuantityReceived.ToString("N0"));
            this.Cell(currency.CurrentAmount is null ? "Unknown" : (currency.CurrentAmount.Value / Math.Max(item.Cost, 1)).ToString("N0"));
            this.Cell(result.Market.CurrentFloor is null ? "Unknown" : result.Market.CurrentFloor.Value.ToString("N0"));
            this.Cell(FormatGil(result.Market.ConservativeSalePrice));
            this.Cell(FormatGil(result.GilPerCurrency));
            this.Cell(FormatGil(result.ExpectedTotal));
            this.Cell(result.Confidence);
            this.Cell(SourceText(item));
            ImGui.TableNextColumn();
            if (ImGui.Button($"Copy name##name-{item.ItemId}-{item.Cost}"))
            {
                ImGui.SetClipboardText(item.ItemName);
            }

            ImGui.SameLine();
            if (ImGui.Button($"Copy ID##id-{item.ItemId}-{item.Cost}"))
            {
                ImGui.SetClipboardText(item.ItemId.ToString());
            }
        }

        ImGui.EndTable();
    }

    private static string SourceText(SpendableCurrencyItem item)
    {
        return string.Join(" / ", new[] { item.SourceShopName, item.SourceVendorName, item.SourceZone, item.SourceNotes }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private void DrawCollectablesSection()
    {
        if (ImGui.CollapsingHeader("Collectables / Unlocks"))
        {
            ImGui.TextWrapped("Collectable and unlock tracking is not implemented in this read-only market-profit pass.");
        }
    }

    private void DrawDiagnostics()
    {
        if (!ImGui.CollapsingHeader("Diagnostics"))
        {
            return;
        }

        var candidate = this.scannerService.CandidateSourceStatus;
        ImGui.TextUnformatted("Lumina");
        ImGui.TextUnformatted($"IDataManager available: {YesNo(candidate.LuminaAvailable)}");
        ImGui.TextUnformatted($"Item sheet available: {YesNo(candidate.LuminaItemSheetAvailable)}");
        ImGui.TextUnformatted($"Marketability path: {candidate.MarketabilityPath}");
        ImGui.TextUnformatted($"Candidate source: {candidate.CandidateSourceType}");
        ImGui.TextUnformatted($"Candidates loaded: {candidate.CandidateCount:N0}");
        ImGui.TextUnformatted($"Skipped invalid: {candidate.CandidateInvalidCount:N0}");
        ImGui.TextUnformatted($"Skipped unmarketable: {candidate.CandidateUnmarketableCount:N0}");
        ImGui.TextUnformatted($"Skipped duplicate: {candidate.CandidateDuplicateCount:N0}");
        ImGui.TextUnformatted($"Candidate last error: {candidate.CandidateLastError ?? "none"}");

        ImGui.Separator();
        ImGui.TextUnformatted("Universalis");
        ImGui.TextUnformatted($"Endpoint: {this.universalisClient.BaseEndpoint}");
        ImGui.TextUnformatted($"Last fetch: {FormatTime(this.universalisClient.LastFetchUtc)}");
        ImGui.TextUnformatted($"Items requested: {this.universalisClient.LastItemsRequested:N0}");
        ImGui.TextUnformatted($"Items returned: {this.universalisClient.LastItemsReturned:N0}");
        ImGui.TextUnformatted($"Last error: {this.universalisClient.LastError ?? "none"}");
        ImGui.TextUnformatted($"Cache: {(this.universalisClient.LastFetchUsedCache ? "hit" : "miss/not used")}");

        ImGui.Separator();
        ImGui.TextUnformatted("Ranking");
        var ranking = this.scannerService.RankingStatus;
        ImGui.TextUnformatted($"Status: {ranking.Status}");
        ImGui.TextUnformatted($"Ranked results: {ranking.RankedResultCount:N0}");
        ImGui.TextUnformatted($"Zero-sale results: {ranking.ZeroSaleCount:N0}");
        ImGui.TextUnformatted($"Speculative results: {ranking.SpeculativeCount:N0}");
        ImGui.TextUnformatted($"Stale-data results: {ranking.StaleDataCount:N0}");
        ImGui.TextUnformatted($"Ranking last error: {ranking.LastError ?? "none"}");

        ImGui.Separator();
        ImGui.TextUnformatted("IPC: No IPC required: Lumina uses IDataManager; Universalis uses HTTP.");
        ImGui.TextUnformatted($"IPC contracts: {this.ipcDiagnosticsService.ContractsFound}");

        ImGui.Separator();
        if (ImGui.Button("Run Lumina discovery"))
        {
            this.luminaDiscovery = this.luminaDiscoveryService.Discover();
        }

        if (this.luminaDiscovery is null)
        {
            ImGui.TextUnformatted("Lumina discovery: not run.");
            return;
        }

        ImGui.TextUnformatted($"Lumina discovery: {this.luminaDiscovery.Count} compile-visible sheet class(es).");
    }

    private void Cell(string value)
    {
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(value);
    }

    private static string FormatGil(double? value) => value is null ? "Unknown" : value.Value.ToString("N2");

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string FormatTime(DateTimeOffset? value) => value is null ? "never" : $"{value:yyyy-MM-dd HH:mm:ss} UTC";
}
