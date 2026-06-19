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

        this.DrawControls();
        ImGui.Separator();
        ImGui.TextUnformatted(this.scannerService.Status);

        if (this.scannerService.Candidates.Count == 0 && this.scannerService.Results.Count == 0)
        {
            ImGui.TextWrapped("No verified candidate data loaded. The scanner framework is ready, but no item IDs, costs, quantities, or currency IDs have been fabricated.");
        }

        this.DrawDiagnostics();
        this.DrawResultsTable();
        ImGui.End();
    }

    public void Dispose()
    {
    }

    private void DrawControls()
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

        var hideStale = this.configuration.HideStaleData;
        if (ImGui.Checkbox("Hide stale data", ref hideStale))
        {
            this.configuration.HideStaleData = hideStale;
            this.configuration.Save();
        }

        var hideNoMovement = this.configuration.HideNoMovementItems;
        if (ImGui.Checkbox("Hide no-movement items", ref hideNoMovement))
        {
            this.configuration.HideNoMovementItems = hideNoMovement;
            this.configuration.Save();
        }

        var currencyFilter = this.configuration.CurrencyFilter;
        if (ImGui.InputText("Currency filter", ref currencyFilter, 64))
        {
            this.configuration.CurrencyFilter = currencyFilter;
            this.configuration.Save();
        }

        if (ImGui.Button(this.scannerService.IsRefreshing ? "Running..." : "Run pipeline test") && !this.scannerService.IsRefreshing)
        {
            _ = this.scannerService.RefreshAsync(this.configuration.PreferredWorldOrDc);
        }

        ImGui.SameLine();
        if (ImGui.Button("Refresh market data") && !this.scannerService.IsRefreshing)
        {
            _ = this.scannerService.RefreshAsync(this.configuration.PreferredWorldOrDc);
        }

        if (this.scannerService.LastRefreshUtc is { } lastRefresh)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted($"Last refresh: {(DateTimeOffset.UtcNow - lastRefresh).TotalMinutes:N0}m ago");
        }
    }

    private void DrawDiagnostics()
    {
        if (!ImGui.CollapsingHeader("Diagnostics"))
        {
            return;
        }

        ImGui.TextUnformatted($"Universalis: {this.universalisClient.Status}");
        ImGui.TextUnformatted($"Candidate data: {this.scannerService.CandidateDataSourceStatus}");
        ImGui.TextUnformatted("IPC: No IPC required: Lumina uses IDataManager; Universalis uses HTTP.");
        ImGui.TextUnformatted($"IPC contracts: {this.ipcDiagnosticsService.ContractsFound}");
        ImGui.Separator();
        this.DrawPipelineDiagnostics();
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
        foreach (var sheet in this.luminaDiscovery)
        {
            if (!ImGui.TreeNode($"{sheet.SheetClassName} ({(sheet.CanLoadSheet ? "loadable" : "not loadable")})"))
            {
                continue;
            }

            var assessment = sheet.CandidateAssessment;
            ImGui.TextUnformatted($"Result item: {YesNo(assessment.ResultItem)}");
            ImGui.TextUnformatted($"Cost item/currency: {YesNo(assessment.CostCurrency)}");
            ImGui.TextUnformatted($"Cost amount: {YesNo(assessment.CostAmount)}");
            ImGui.TextUnformatted($"Quantity received: {YesNo(assessment.QuantityReceived)}");
            ImGui.TextUnformatted($"Source shop name: {YesNo(assessment.SourceShopName)}");
            ImGui.TextWrapped(assessment.Notes);
            ImGui.Separator();
            foreach (var property in sheet.PublicProperties)
            {
                ImGui.TextUnformatted(property);
            }

            ImGui.TreePop();
        }
    }

    private void DrawPipelineDiagnostics()
    {
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
    }

    private void DrawResultsTable()
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable;
        if (!ImGui.BeginTable("currency-profit-results", 13, flags))
        {
            return;
        }

        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Currency");
        ImGui.TableSetupColumn("Cost");
        ImGui.TableSetupColumn("Qty");
        ImGui.TableSetupColumn("Expected gil/currency");
        ImGui.TableSetupColumn("Final score");
        ImGui.TableSetupColumn("Sales 24h");
        ImGui.TableSetupColumn("Units sold 24h");
        ImGui.TableSetupColumn("Last sale");
        ImGui.TableSetupColumn("Current floor");
        ImGui.TableSetupColumn("Active supply");
        ImGui.TableSetupColumn("Confidence");
        ImGui.TableSetupColumn("Data age / notes");
        ImGui.TableHeadersRow();

        foreach (var result in this.FilteredResults())
        {
            ImGui.TableNextRow();
            this.Cell(result.Candidate.ItemName);
            this.Cell(result.Candidate.CurrencyName);
            this.Cell(result.Candidate.Cost.ToString("N0"));
            this.Cell(result.Candidate.QuantityReceived.ToString("N0"));
            this.Cell(FormatGil(result.GilPerCurrency));
            this.Cell(result.FinalScore.ToString("N2"));
            this.Cell(result.Sales24h.ToString("N0"));
            this.Cell(result.UnitsSold24h.ToString("N0"));
            this.Cell(result.LastSaleAgeHours is null ? "unknown" : $"{result.LastSaleAgeHours:N1}h");
            this.Cell(result.CurrentFloorPrice is null ? "unknown" : result.CurrentFloorPrice.Value.ToString("N0"));
            this.Cell(result.ActiveSupply is null ? "unknown" : result.ActiveSupply.Value.ToString("N0"));
            this.Cell(result.Confidence);
            this.Cell(this.DataAgeAndNotes(result));
        }

        ImGui.EndTable();
    }

    private IEnumerable<ScanResult> FilteredResults()
    {
        return this.scannerService.Results.Where(result =>
            (!this.configuration.HideStaleData || !result.IsStale) &&
            (!this.configuration.HideNoMovementItems || result.Sales24h > 0) &&
            (string.IsNullOrWhiteSpace(this.configuration.CurrencyFilter) ||
             result.Candidate.CurrencyName.Contains(this.configuration.CurrencyFilter, StringComparison.OrdinalIgnoreCase)));
    }

    private string DataAgeAndNotes(ScanResult result)
    {
        var age = result.MarketData?.LastUploadTime is null
            ? "unknown age"
            : $"{(DateTimeOffset.UtcNow - result.MarketData.LastUploadTime.Value).TotalMinutes:N0}m old";
        var notes = string.IsNullOrWhiteSpace(result.Candidate.Notes) ? result.Status : result.Candidate.Notes;
        return $"{age}; {notes}";
    }

    private void Cell(string value)
    {
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(value);
    }

    private static string FormatGil(double? value) => value is null ? "unknown" : value.Value.ToString("N2");

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string FormatTime(DateTimeOffset? value) => value is null ? "never" : $"{value:yyyy-MM-dd HH:mm:ss} UTC";
}
