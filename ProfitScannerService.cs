using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace CurrencyProfitScanner;

public sealed class ProfitScannerService : IDisposable
{
    private const double SalesHalfLife = 6d;
    private const double UnitsHalfLife = 30d;
    private const double FreshSaleHalfLifeHours = 18d;
    private const double SupplyDaysComfortZone = 4d;

    private readonly CurrencyCandidateSource candidateSource;
    private readonly UniversalisClient universalisClient;
    private readonly PluginConfiguration configuration;
    private readonly IPluginLog log;
    private readonly object stateLock = new();
    private readonly Dictionary<string, IReadOnlyList<ProfitResult>> resultsByCurrency = new(StringComparer.Ordinal);
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
        this.ReloadCandidates();
    }

    public IReadOnlyList<TrackedCurrencyModel> Currencies { get; private set; } = [];

    public IReadOnlyList<SpendableCurrencyItem> Items { get; private set; } = [];

    public TrackedCurrencyModel? SelectedCurrency { get; private set; }

    public IReadOnlyList<ProfitResult> Results { get; private set; } = [];

    public bool IsRefreshing { get; private set; }

    public DateTimeOffset? LastRefreshUtc { get; private set; }

    public string Status { get; private set; } = "Ready.";

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

    public RankingStatus RankingStatus { get; private set; } = new("Not ranked.", null, 0, 0, 0, 0);

    public void ReloadCandidates()
    {
        var catalog = this.candidateSource.LoadCatalog();
        lock (this.stateLock)
        {
            this.Currencies = catalog.Currencies;
            this.RefreshCurrencyAmountsLocked();
            this.Items = catalog.Items;
            this.CandidateSourceStatus = catalog.Status;
            this.resultsByCurrency.Clear();
            this.Results = [];

            if (this.SelectedCurrency is not null)
            {
                this.SelectedCurrency = this.Currencies.FirstOrDefault(currency => SameCurrency(currency, this.SelectedCurrency));
            }

            this.Status = catalog.Status.CandidateLoadStatus;
            this.RankingStatus = new RankingStatus("Candidate data reloaded. Select a currency and refresh market data.", null, 0, 0, 0, 0);
        }
    }

    public void SelectCurrency(TrackedCurrencyModel currency)
    {
        lock (this.stateLock)
        {
            this.RefreshCurrencyAmountsLocked();
            this.SelectedCurrency = currency;
            this.Results = this.GetResultsForCurrencyLocked(currency);
            var sellableCount = this.Items.Count(item => SameCurrency(item, currency) && item.IsMarketable && !item.Disabled);
            this.RankingStatus = this.Results.Count > 0
                ? BuildRankingStatus("Loaded cached rankings for selected currency.", this.Results)
                : sellableCount == 0
                    ? new RankingStatus("Currency selected, but no marketable rewards are known for it yet.", null, 0, 0, 0, 0)
                    : new RankingStatus("Currency selected. Refresh market data to rank sellable items.", null, 0, 0, 0, 0);
            this.Status = this.RankingStatus.Status;
        }
    }

    public IReadOnlyList<SpendableCurrencyItem> GetAllItemsForCurrency(TrackedCurrencyModel currency)
    {
        return this.Items
            .Where(item => SameCurrency(item, currency) && !item.Disabled)
            .OrderBy(item => item.IsMarketable ? 0 : 1)
            .ThenBy(item => item.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<SpendableCurrencyItem> GetItemsForCurrency(TrackedCurrencyModel currency) => this.GetSellableItemsForCurrency(currency);

    public IReadOnlyList<SpendableCurrencyItem> GetSellableItemsForCurrency(TrackedCurrencyModel currency)
    {
        return this.Items
            .Where(item => SameCurrency(item, currency) && item.IsMarketable && !item.Disabled)
            .OrderBy(item => item.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<SpendableCurrencyItem> GetOtherItemsForCurrency(TrackedCurrencyModel currency)
    {
        return this.Items
            .Where(item => SameCurrency(item, currency) && !item.IsMarketable && item.ItemKind == SpendableItemKind.Other && !item.Disabled)
            .OrderBy(item => item.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void RefreshCurrencyAmounts()
    {
        lock (this.stateLock)
        {
            this.RefreshCurrencyAmountsLocked();
        }
    }

    public IReadOnlyList<SpendableCurrencyItem> GetItemsByKindForCurrency(TrackedCurrencyModel currency, SpendableItemKind kind)
    {
        return this.Items
            .Where(item => SameCurrency(item, currency) && item.ItemKind.HasFlag(kind) && !item.Disabled)
            .OrderBy(item => item.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<ProfitResult> GetResultsForCurrency(TrackedCurrencyModel currency)
    {
        lock (this.stateLock)
        {
            return this.GetResultsForCurrencyLocked(currency);
        }
    }

    public double? GetBestGilPerCurrency(TrackedCurrencyModel currency)
    {
        return this.GetResultsForCurrency(currency)
            .Where(result => result.Market.Sales24h > 0)
            .Select(result => result.GilPerCurrency)
            .Where(value => value.HasValue)
            .DefaultIfEmpty()
            .Max();
    }

    public string? GetBestItemName(TrackedCurrencyModel currency)
    {
        return this.GetResultsForCurrency(currency)
            .Where(result => result.Market.Sales24h > 0)
            .OrderByDescending(result => MovementTier(result))
            .ThenByDescending(result => result.Market.Sales24h)
            .ThenByDescending(result => result.Market.UnitsSold24h)
            .ThenByDescending(result => result.GilPerCurrency ?? 0)
            .FirstOrDefault()
            ?.Item.ItemName;
    }

    public async Task RefreshSelectedCurrencyAsync(string worldOrDc)
    {
        TrackedCurrencyModel selected;
        CancellationTokenSource cts;
        lock (this.stateLock)
        {
            if (this.IsRefreshing || this.SelectedCurrency is null)
            {
                return;
            }

            this.refreshCts?.Cancel();
            this.refreshCts?.Dispose();
            cts = new CancellationTokenSource();
            this.refreshCts = cts;
            selected = this.SelectedCurrency;
            this.IsRefreshing = true;
            this.Status = "Refreshing selected currency market data...";
        }

        var token = cts.Token;

        try
        {
            var candidates = this.GetSellableItemsForCurrency(selected);
            if (candidates.Count == 0)
            {
                lock (this.stateLock)
                {
                    this.Results = [];
                    this.resultsByCurrency.Remove(CurrencyKey(selected));
                    this.Status = "Refresh skipped: selected currency has no known marketable rewards to query.";
                    this.RankingStatus = new RankingStatus(this.Status, null, 0, 0, 0, 0);
                }

                return;
            }

            var marketData = await this.universalisClient.GetMarketDataAsync(
                worldOrDc,
                candidates.Select(item => item.ItemId).Distinct().ToList(),
                TimeSpan.FromMinutes(Math.Clamp(this.configuration.CacheTtlMinutes, 1, 120)),
                token).ConfigureAwait(false);

            var results = candidates
                .Select(item => Calculate(item, marketData.GetValueOrDefault(item.ItemId), this.configuration))
                .OrderByDescending(MovementTier)
                .ThenByDescending(result => result.Market.Sales24h)
                .ThenByDescending(result => result.Market.UnitsSold24h)
                .ThenByDescending(result => result.GilPerCurrency ?? 0)
                .ToList();

            var zeroSaleCount = results.Count(result => result.Market.Sales24h == 0);
            var status = results.Count == 0
                ? "Candidates loaded, but no rankings produced."
                : zeroSaleCount == results.Count
                    ? "Candidates loaded, but no 24h sales found."
                    : "Candidates loaded, market data fetched, rankings produced.";
            lock (this.stateLock)
            {
                this.Results = results;
                this.resultsByCurrency[CurrencyKey(selected)] = results;
                this.LastRefreshUtc = DateTimeOffset.UtcNow;
                this.Status = status;
                this.RankingStatus = BuildRankingStatus(this.Status, results);
            }
        }
        catch (OperationCanceledException)
        {
            lock (this.stateLock)
            {
                this.Status = "Refresh cancelled.";
            }
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "Currency Profit Scanner refresh failed");
            lock (this.stateLock)
            {
                this.Status = "Refresh failed. See Dalamud log.";
                this.RankingStatus = this.RankingStatus with { Status = this.Status, LastError = ex.Message };
            }
        }
        finally
        {
            lock (this.stateLock)
            {
                this.IsRefreshing = false;
            }
        }
    }

    public void Dispose()
    {
        this.refreshCts?.Cancel();
        this.refreshCts?.Dispose();
    }

    private static ProfitResult Calculate(SpendableCurrencyItem item, MarketData? data, PluginConfiguration config)
    {
        if (data is null || data.Error is not null)
        {
            return Empty(item, data?.Error ?? "No market data");
        }

        var now = DateTimeOffset.UtcNow;
        var stale = data.LastUploadTime is null || now - data.LastUploadTime > TimeSpan.FromMinutes(config.StaleDataThresholdMinutes);
        var sales24h = data.Sales.Where(sale => now - sale.Timestamp <= TimeSpan.FromHours(24)).ToList();
        var salesCount = sales24h.Count;
        var unitsSold = (uint)sales24h.Sum(sale => (long)sale.Quantity);
        var prices = sales24h.Select(sale => (double)sale.PricePerUnit).Order().ToList();
        var median = Percentile(prices, 0.50);
        var p25 = Percentile(prices, 0.25);
        var lastSaleAge = sales24h.Count == 0 ? null : (double?)(now - sales24h.Max(sale => sale.Timestamp)).TotalHours;
        var floor = MinNullableUInt(data.CurrentFloorPriceNq, data.CurrentFloorPriceHq);
        var floorAsDouble = floor is null ? null : (double?)floor.Value;
        var conservative = MinNullable(median, p25 * 1.15, floorAsDouble * 0.98);
        var expectedTotal = conservative * item.QuantityReceived * config.TaxBufferMultiplier;
        var gilPerCurrency = item.Cost == 0 ? null : expectedTotal / item.Cost;
        var saleVelocity = SaturatingScore(salesCount, SalesHalfLife);
        var unitVelocity = SaturatingScore(unitsSold, UnitsHalfLife);
        var recency = Math.Exp(-(lastSaleAge ?? 36d) / FreshSaleHalfLifeHours);
        var liquidity = salesCount == 0
            ? 0d
            : 0.50 * saleVelocity + 0.30 * unitVelocity + 0.20 * recency;
        var supply = SupplyScore(data.TotalListedQuantity, unitsSold);
        var staleMultiplier = stale ? 0.25 : 1d;
        var finalScore = salesCount == 0 || gilPerCurrency is null
            ? 0
            : gilPerCurrency.Value * liquidity * supply * staleMultiplier;

        var confidence = salesCount == 0
            ? "No 24h movement"
            : stale
                ? "Stale"
                : liquidity < 0.45
                    ? "Thin market"
                    : supply < 0.45
                        ? "Crowded market"
                        : liquidity < 0.70
                            ? "Healthy"
                            : "Strong";

        var snapshot = new MarketSnapshot(
            item.ItemId,
            floor,
            salesCount,
            unitsSold,
            lastSaleAge,
            median,
            p25,
            conservative,
            data.TotalListedQuantity,
            data.LastUploadTime is null ? null : now - data.LastUploadTime.Value,
            now,
            null);

        return new ProfitResult(item, snapshot, gilPerCurrency, expectedTotal, liquidity, finalScore, confidence);
    }

    private static ProfitResult Empty(SpendableCurrencyItem item, string status)
    {
        var snapshot = new MarketSnapshot(item.ItemId, null, 0, 0, null, null, null, null, null, null, null, status);
        return new ProfitResult(item, snapshot, null, null, 0, 0, "Unknown");
    }

    private static RankingStatus BuildRankingStatus(string status, IReadOnlyList<ProfitResult> results)
    {
        return new RankingStatus(
            status,
            null,
            results.Count,
            results.Count(result => result.Market.Sales24h == 0),
            results.Count(result => result.Confidence is "Thin market" or "Crowded market"),
            results.Count(result => result.Confidence == "Stale"));
    }

    private static string CurrencyKey(TrackedCurrencyModel currency) => $"{currency.CurrencyId}:{currency.Name}";

    private IReadOnlyList<ProfitResult> GetResultsForCurrencyLocked(TrackedCurrencyModel currency)
    {
        return this.resultsByCurrency.TryGetValue(CurrencyKey(currency), out var results) ? results : [];
    }

    private unsafe void RefreshCurrencyAmountsLocked()
    {
        this.Currencies = this.Currencies
            .Select(currency => currency.CurrencyId == 0
                ? currency
                : currency with { CurrentAmount = (uint)InventoryManager.Instance()->GetInventoryItemCount(currency.CurrencyId, false, false, false) })
            .ToList();
    }

    private static bool SameCurrency(SpendableCurrencyItem item, TrackedCurrencyModel currency) =>
        item.CurrencyId == currency.CurrencyId && item.CurrencyName.Equals(currency.Name, StringComparison.Ordinal);

    private static bool SameCurrency(TrackedCurrencyModel left, TrackedCurrencyModel right) =>
        left.CurrencyId == right.CurrencyId && left.Name.Equals(right.Name, StringComparison.Ordinal);

    private static int MovementTier(ProfitResult result)
    {
        return result.Market.Sales24h == 0 ? 0 :
            result.Confidence == "Stale" ? 1 :
            result.Confidence is "Thin market" or "Crowded market" ? 2 :
            result.Confidence == "Healthy" ? 3 : 4;
    }

    private static double Clamp(double value) => Math.Clamp(value, 0d, 1d);

    private static double SaturatingScore(double value, double halfLife)
    {
        return 1d - Math.Exp(-Math.Max(value, 0d) / halfLife);
    }

    private static double SupplyScore(uint? activeSupply, uint unitsSold24h)
    {
        if (unitsSold24h == 0)
        {
            return 0d;
        }

        if (activeSupply is null or 0)
        {
            return 0.75d;
        }

        var daysOfSupply = activeSupply.Value / (double)unitsSold24h;
        return 0.25d + 0.75d / (1d + daysOfSupply / SupplyDaysComfortZone);
    }

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
