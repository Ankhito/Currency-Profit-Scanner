using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System.Text.Json;

namespace CurrencyProfitScanner;

public sealed class CurrencyCandidateSource
{
    private readonly IDataManager dataManager;
    private readonly string configDirectory;

    public CurrencyCandidateSource(IDataManager dataManager, string configDirectory)
    {
        this.dataManager = dataManager;
        this.configDirectory = configDirectory;
    }

    public CandidateLoadResult LoadCandidates()
    {
        var candidates = new List<CurrencyVendorCandidate>();
        var invalid = 0;
        var unmarketable = 0;
        var duplicates = 0;
        string? lastError = null;

        var itemSheet = this.dataManager.GetExcelSheet<Item>();
        if (itemSheet is null)
        {
            return new CandidateLoadResult([], new CandidateSourceStatus(
                LuminaAvailable: this.dataManager is not null,
                LuminaItemSheetAvailable: false,
                MarketabilityPath: "Item.ItemSearchCategory.RowId > 0 and Item.IsUntradable == false",
                CandidateSourceType: "Lumina SpecialShop + optional JSON seed",
                CandidateLoadStatus: "Item sheet unavailable.",
                CandidateCount: 0,
                CandidateInvalidCount: 0,
                CandidateUnmarketableCount: 0,
                CandidateDuplicateCount: 0,
                CandidateLastError: "Lumina Item sheet unavailable."));
        }

        try
        {
            var specialShopResult = this.LoadSpecialShopCandidates(itemSheet);
            candidates.AddRange(specialShopResult.Candidates);
            invalid += specialShopResult.InvalidCount;
            unmarketable += specialShopResult.UnmarketableCount;
        }
        catch (Exception ex)
        {
            lastError = $"SpecialShop load failed: {ex.Message}";
        }

        try
        {
            var seedResult = this.LoadSeedCandidates(itemSheet);
            candidates.AddRange(seedResult.Candidates);
            invalid += seedResult.InvalidCount;
            unmarketable += seedResult.UnmarketableCount;
        }
        catch (Exception ex)
        {
            lastError = $"Seed load failed: {ex.Message}";
        }

        var deduped = new Dictionary<string, CurrencyVendorCandidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var key = $"{candidate.ItemId}:{candidate.CurrencyName}:{candidate.Cost}:{candidate.QuantityReceived}:{candidate.SourceShopName}";
            if (!deduped.TryAdd(key, candidate))
            {
                duplicates++;
            }
        }

        var statusText = deduped.Count == 0
            ? "No verified candidates loaded."
            : $"Loaded {deduped.Count} verified candidate(s).";

        return new CandidateLoadResult(deduped.Values.ToList(), new CandidateSourceStatus(
            LuminaAvailable: true,
            LuminaItemSheetAvailable: true,
            MarketabilityPath: "Item.ItemSearchCategory.RowId > 0 and Item.IsUntradable == false",
            CandidateSourceType: "Lumina SpecialShop single cost/reward + optional JSON seed",
            CandidateLoadStatus: statusText,
            CandidateCount: deduped.Count,
            CandidateInvalidCount: invalid,
            CandidateUnmarketableCount: unmarketable,
            CandidateDuplicateCount: duplicates,
            CandidateLastError: lastError));
    }

    public string ResolveItemName(uint itemId)
    {
        var item = this.dataManager.GetExcelSheet<Item>()?.GetRow(itemId);
        return item?.Name.ExtractText() ?? $"Item {itemId}";
    }

    private CandidateBatch LoadSpecialShopCandidates(Lumina.Excel.ExcelSheet<Item> itemSheet)
    {
        var candidates = new List<CurrencyVendorCandidate>();
        var invalid = 0;
        var unmarketable = 0;
        var shopSheet = this.dataManager.GetExcelSheet<SpecialShop>();
        if (shopSheet is null)
        {
            return new CandidateBatch(candidates, invalid, unmarketable);
        }

        foreach (var shop in shopSheet)
        {
            var shopName = shop.Name.ExtractText();
            foreach (var entry in shop.Item)
            {
                if (entry.ReceiveItems.Count != 1 || entry.ItemCosts.Count != 1)
                {
                    invalid++;
                    continue;
                }

                var received = entry.ReceiveItems[0];
                var cost = entry.ItemCosts[0];
                var itemId = received.Item.RowId;
                var costItemId = cost.ItemCost.RowId;
                if (itemId == 0 || cost.CurrencyCost == 0 || received.ReceiveCount == 0 || costItemId == 0)
                {
                    invalid++;
                    continue;
                }

                var item = itemSheet.GetRow(itemId);
                var costItem = itemSheet.GetRow(costItemId);
                if (item.RowId != itemId || costItem.RowId != costItemId)
                {
                    invalid++;
                    continue;
                }

                if (!IsMarketable(item))
                {
                    unmarketable++;
                    continue;
                }

                candidates.Add(new CurrencyVendorCandidate(
                    itemId,
                    item.Name.ExtractText(),
                    costItem.Name.ExtractText(),
                    costItemId.ToString(),
                    cost.CurrencyCost,
                    received.ReceiveCount,
                    null,
                    string.IsNullOrWhiteSpace(shopName) ? $"SpecialShop {shop.RowId}" : shopName,
                    $"Lumina SpecialShop row {shop.RowId}; single receive and single cost only.",
                    true));
            }
        }

        return new CandidateBatch(candidates, invalid, unmarketable);
    }

    private CandidateBatch LoadSeedCandidates(Lumina.Excel.ExcelSheet<Item> itemSheet)
    {
        var candidates = new List<CurrencyVendorCandidate>();
        var invalid = 0;
        var unmarketable = 0;
        var seedPath = Path.Combine(this.configDirectory, "currency-candidates.json");
        if (!File.Exists(seedPath))
        {
            return new CandidateBatch(candidates, invalid, unmarketable);
        }

        List<SeedCandidate>? seeds;
        try
        {
            var json = File.ReadAllText(seedPath);
            seeds = JsonSerializer.Deserialize<List<SeedCandidate>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            invalid++;
            return new CandidateBatch(candidates, invalid, unmarketable);
        }

        if (seeds is null)
        {
            return new CandidateBatch(candidates, invalid, unmarketable);
        }

        foreach (var seed in seeds)
        {
            if (seed.ItemId == 0 || seed.Cost == 0 || seed.QuantityReceived == 0 || string.IsNullOrWhiteSpace(seed.CurrencyName))
            {
                invalid++;
                continue;
            }

            var item = itemSheet.GetRow(seed.ItemId);
            if (item.RowId != seed.ItemId)
            {
                invalid++;
                continue;
            }

            if (!IsMarketable(item))
            {
                unmarketable++;
                continue;
            }

            candidates.Add(new CurrencyVendorCandidate(
                seed.ItemId,
                item.Name.ExtractText(),
                seed.CurrencyName,
                seed.CurrencyKey,
                seed.Cost,
                seed.QuantityReceived,
                seed.SourceVendorName,
                seed.SourceShopName,
                seed.VerificationSource,
                true));
        }

        return new CandidateBatch(candidates, invalid, unmarketable);
    }

    private static bool IsMarketable(Item item) => !item.IsUntradable && item.ItemSearchCategory.RowId > 0;

    private sealed class SeedCandidate
    {
        public uint ItemId { get; set; }

        public string CurrencyName { get; set; } = string.Empty;

        public string? CurrencyKey { get; set; }

        public uint Cost { get; set; }

        public uint QuantityReceived { get; set; } = 1;

        public string? SourceShopName { get; set; }

        public string? SourceVendorName { get; set; }

        public string? VerificationSource { get; set; }
    }

    private sealed record CandidateBatch(
        IReadOnlyList<CurrencyVendorCandidate> Candidates,
        int InvalidCount,
        int UnmarketableCount);
}
