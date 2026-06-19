using Dalamud.Plugin.Services;

namespace CurrencyProfitScanner;

public sealed class ProfitScannerService : IDisposable
{
    private readonly CurrencyCandidateSource candidateSource;
    private readonly UniversalisClient universalisClient;
    private readonly PluginConfiguration configuration;
    private readonly IPluginLog log;
    private CancellationTokenSource? refreshCts;

    public ProfitScannerService(
        CurrencyCandidateSource candidateSource,
        UniversalisClient universalisClient,
        PluginConfiguration configuration,
        IPluginLog log)
    {
        this.candidateSource = candidateSource;
        this.universalisClient = universalisClient;
        this.configuration = configuration;
        this.log = log;
    }

    public IReadOnlyList<CurrencyVendorCandidate> Candidates { get; private set; } = [];

    public IReadOnlyList<ScanResult> Results { get; private set; } = [];

    public bool IsRefreshing { get; private set; }

    public DateTimeOffset? LastRefreshUtc { get; private set; }

    public string Status { get; private set; } = "Not refreshed.";

    public string CandidateDataSourceStatus { get; private set; } = "Not loaded.";

    public CandidateSourceStatus CandidateSourceStatus { get; private set; } = new(
        LuminaAvailable: false,
        LuminaItemSheetAvailable: false,
        MarketabilityPath: "Item.ItemSearchCategory.RowId > 0 and Item.IsUntradable == false",
        CandidateSourceType: "Not loaded",
        CandidateLoadStatus: "Not loaded.",
        CandidateCount: 0,
        CandidateInvalidCount: 0,
        CandidateUnmarketableCount: 0,
        CandidateDuplicateCount: 0,
        CandidateLastError: null);

    public RankingStatus RankingStatus { get; private set; } = new(
        Status: "Not ranked.",
        LastError: null,
        RankedResultCount: 0,
        ZeroSaleCount: 0,
        SpeculativeCount: 0,
        StaleDataCount: 0);

    public async Task RefreshAsync(string worldOrDc)
    {
        if (this.IsRefreshing)
        {
            return;
        }

        this.refreshCts?.Cancel();
        this.refreshCts?.Dispose();
        this.refreshCts = new CancellationTokenSource();
        var token = this.refreshCts.Token;

        this.IsRefreshing = true;
        this.Status = "Refreshing Universalis data...";

        try
        {
            var candidateLoad = this.candidateSource.LoadCandidates();
            this.Candidates = candidateLoad.Candidates;
            this.CandidateSourceStatus = candidateLoad.Status;
            this.CandidateDataSourceStatus = candidateLoad.Status.CandidateLoadStatus;

            if (this.Candidates.Count == 0)
            {
                this.Results = [];
                this.Status = "No verified candidate data loaded.";
                this.RankingStatus = new RankingStatus("No verified candidates loaded.", null, 0, 0, 0, 0);
                return;
            }

            var marketData = await this.universalisClient.GetMarketDataAsync(
                worldOrDc,
                this.Candidates.Select(c => c.ItemId).ToList(),
                TimeSpan.FromMinutes(Math.Clamp(this.configuration.CacheTtlMinutes, 1, 120)),
                token).ConfigureAwait(false);

            this.Results = this.Candidates
                .Select(candidate => Calculate(candidate, marketData.GetValueOrDefault(candidate.ItemId), this.configuration))
                .OrderByDescending(r => r.Sales24h > 0)
                .ThenByDescending(r => r.Sales24h)
                .ThenByDescending(r => r.GilPerCurrency ?? 0)
                .ToList();

            this.LastRefreshUtc = DateTimeOffset.UtcNow;
            var zeroSaleCount = this.Results.Count(r => r.Sales24h == 0);
            var speculativeCount = this.Results.Count(r => r.Confidence == "Speculative");
            var staleCount = this.Results.Count(r => r.IsStale);
            this.RankingStatus = new RankingStatus(
                this.Results.Count == 0
                    ? "Candidates loaded, but no rankings produced."
                    : zeroSaleCount == this.Results.Count
                        ? "Candidates loaded, but no 24h sales found."
                        : "Candidates loaded, market data fetched, rankings produced.",
                null,
                this.Results.Count,
                zeroSaleCount,
                speculativeCount,
                staleCount);
            this.Status = this.RankingStatus.Status;
        }
        catch (OperationCanceledException)
        {
            this.Status = "Refresh cancelled.";
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Currency Profit Scanner refresh failed");
            this.Status = "Refresh failed. See Dalamud log.";
            this.RankingStatus = this.RankingStatus with { Status = this.Status, LastError = ex.Message };
        }
        finally
        {
            this.IsRefreshing = false;
        }
    }

    public void Dispose()
    {
        this.refreshCts?.Cancel();
        this.refreshCts?.Dispose();
    }

    private static ScanResult Calculate(CurrencyVendorCandidate candidate, MarketData? data, PluginConfiguration config)
    {
        if (data is null)
        {
            return Empty(candidate, "No market data");
        }

        if (data.Error is not null)
        {
            return Empty(candidate, data.Error);
        }

        var now = DateTimeOffset.UtcNow;
        var stale = data.LastUploadTime is null || now - data.LastUploadTime > TimeSpan.FromMinutes(config.StaleDataThresholdMinutes);
        var sales24h = data.Sales.Where(s => now - s.Timestamp <= TimeSpan.FromHours(24)).ToList();
        var salesCount = sales24h.Count;
        var unitsSold = (uint)sales24h.Sum(s => (long)s.Quantity);
        var prices = sales24h.Select(s => (double)s.PricePerUnit).Order().ToList();
        var median = Percentile(prices, 0.50);
        var p25 = Percentile(prices, 0.25);
        var lastSaleAge = sales24h.Count == 0 ? null : (double?)(now - sales24h.Max(s => s.Timestamp)).TotalHours;
        var floor = MinNullableUInt(data.CurrentFloorPriceNq, data.CurrentFloorPriceHq);
        var floorAsDouble = floor is null ? null : (double?)floor.Value;
        var conservative = MinNullable(median, p25 * 1.15, floorAsDouble * 0.98);
        var netGil = conservative * candidate.QuantityReceived * config.TaxBufferMultiplier;
        var gilPerCurrency = candidate.Cost == 0 ? null : netGil / candidate.Cost;
        var liquidity = 0.60 * Clamp(salesCount / 10d) +
                        0.25 * Clamp(unitsSold / 50d) +
                        0.15 * Math.Exp(-(lastSaleAge ?? 24) / 12d);
        var supply = Clamp(unitsSold / (double)Math.Max(data.TotalListedQuantity ?? 1, 1));
        var staleMultiplier = stale ? 0.25 : 1d;
        var finalScore = salesCount == 0 || gilPerCurrency is null
            ? 0
            : gilPerCurrency.Value * (0.20 + 0.80 * liquidity) * (0.50 + 0.50 * supply) * staleMultiplier;

        var confidence = salesCount == 0
            ? "No 24h movement"
            : salesCount < config.MinimumSales24h
                ? "Speculative"
                : stale
                    ? "Stale"
                    : "Recent sales";

        return new ScanResult(
            candidate,
            data,
            salesCount,
            unitsSold,
            lastSaleAge,
            median,
            p25,
            floor,
            data.TotalListedQuantity,
            conservative,
            netGil,
            gilPerCurrency,
            liquidity,
            supply,
            finalScore,
            confidence,
            stale,
            salesCount == 0 ? "No sales in last 24h" : "OK");
    }

    private static ScanResult Empty(CurrencyVendorCandidate candidate, string status) =>
        new(candidate, null, 0, 0, null, null, null, null, null, null, null, null, 0, 0, 0, "Unknown", true, status);

    private static double Clamp(double value) => Math.Clamp(value, 0d, 1d);

    private static double? MinNullable(params double?[] values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : present.Min();
    }

    private static uint? MinNullableUInt(params uint?[] values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : present.Min();
    }

    private static double? Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        var index = (sortedValues.Count - 1) * percentile;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * (index - lower);
    }
}
