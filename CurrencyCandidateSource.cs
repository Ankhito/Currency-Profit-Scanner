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
        var catalog = this.LoadCatalog();
        var candidates = catalog.Items.Select(item => new CurrencyVendorCandidate(
            item.ItemId,
            item.ItemName,
            item.CurrencyName,
            item.CurrencyId == 0 ? item.CurrencyName : item.CurrencyId.ToString(),
            item.Cost,
            item.QuantityReceived,
            item.SourceVendorName,
            item.SourceShopName,
            item.SourceNotes,
            item.IsMarketable)).ToList();

        return new CandidateLoadResult(candidates, catalog.Status);
    }

    public CandidateCatalogLoadResult LoadCatalog()
    {
        var items = new List<SpendableCurrencyItem>();
        var invalid = 0;
        var unmarketable = 0;
        var duplicates = 0;
        string? lastError = null;

        var itemSheet = this.dataManager.GetExcelSheet<Item>();
        if (itemSheet is null)
        {
            return new CandidateCatalogLoadResult([], [], new CandidateSourceStatus(
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
            items.AddRange(specialShopResult.Candidates);
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
            items.AddRange(seedResult.Candidates);
            invalid += seedResult.InvalidCount;
            unmarketable += seedResult.UnmarketableCount;
        }
        catch (Exception ex)
        {
            lastError = $"Seed load failed: {ex.Message}";
        }

        var deduped = new Dictionary<string, SpendableCurrencyItem>(StringComparer.Ordinal);
        foreach (var candidate in items)
        {
            var key = $"{candidate.ItemId}:{candidate.CurrencyId}:{candidate.CurrencyName}:{candidate.Cost}:{candidate.QuantityReceived}:{candidate.SourceShopName}:{candidate.SourceVendorName}";
            if (!deduped.TryAdd(key, candidate))
            {
                duplicates++;
            }
        }

        var statusText = deduped.Count == 0
            ? "No verified candidates loaded."
            : $"Loaded {deduped.Count} verified candidate(s).";

        var currencies = deduped.Values
            .GroupBy(item => new { item.CurrencyId, item.CurrencyName, item.CurrencyIconId })
            .Select(group => new TrackedCurrencyModel(
                group.Key.CurrencyId,
                group.Key.CurrencyName,
                group.Key.CurrencyIconId,
                null,
                null,
                true,
                "Validated JSON seed"))
            .OrderBy(currency => currency.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var status = new CandidateSourceStatus(
            LuminaAvailable: true,
            LuminaItemSheetAvailable: true,
            MarketabilityPath: "Item.ItemSearchCategory.RowId > 0 and Item.IsUntradable == false",
            CandidateSourceType: "Lumina SpecialShop single cost/reward + validated JSON seed",
            CandidateLoadStatus: statusText,
            CandidateCount: deduped.Count,
            CandidateInvalidCount: invalid,
            CandidateUnmarketableCount: unmarketable,
            CandidateDuplicateCount: duplicates,
            CandidateLastError: lastError);

        return new CandidateCatalogLoadResult(currencies, deduped.Values.ToList(), status);
    }

    public string ResolveItemName(uint itemId)
    {
        var item = this.dataManager.GetExcelSheet<Item>()?.GetRow(itemId);
        return item?.Name.ExtractText() ?? $"Item {itemId}";
    }

    private CandidateBatch LoadSpecialShopCandidates(Lumina.Excel.ExcelSheet<Item> itemSheet)
    {
        var candidates = new List<SpendableCurrencyItem>();
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

                candidates.Add(new SpendableCurrencyItem(
                    itemId,
                    item.Name.ExtractText(),
                    item.Icon,
                    costItemId,
                    costItem.Name.ExtractText(),
                    costItem.Icon,
                    cost.CurrencyCost,
                    received.ReceiveCount,
                    null,
                    string.IsNullOrWhiteSpace(shopName) ? $"SpecialShop {shop.RowId}" : shopName,
                    null,
                    $"Lumina SpecialShop row {shop.RowId}; single receive and single cost only.",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    SpendableItemKind.Sellable,
                    true,
                    false));
            }
        }

        return new CandidateBatch(candidates, invalid, unmarketable);
    }

    private CandidateBatch LoadSeedCandidates(Lumina.Excel.ExcelSheet<Item> itemSheet)
    {
        var candidates = new List<SpendableCurrencyItem>();
        var invalid = 0;
        var unmarketable = 0;
        var seedPath = Path.Combine(this.configDirectory, "currency-candidates.json");
        if (!File.Exists(seedPath))
        {
            return new CandidateBatch(candidates, invalid, unmarketable);
        }

        SeedCatalog? catalog = null;
        List<SeedCandidate>? legacySeeds = null;
        try
        {
            var json = File.ReadAllText(seedPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                legacySeeds = JsonSerializer.Deserialize<List<SeedCandidate>>(json, options);
            }
            else
            {
                catalog = JsonSerializer.Deserialize<SeedCatalog>(json, options);
            }
        }
        catch
        {
            invalid++;
            return new CandidateBatch(candidates, invalid, unmarketable);
        }

        var currencyMap = (catalog?.Currencies ?? [])
            .Where(currency => currency.CurrencyId != 0 || !string.IsNullOrWhiteSpace(currency.CurrencyName))
            .GroupBy(currency => currency.CurrencyId)
            .ToDictionary(group => group.Key, group => group.First());

        var seeds = legacySeeds ?? catalog?.Items;
        if (seeds is null)
        {
            return new CandidateBatch(candidates, invalid, unmarketable);
        }

        foreach (var seed in seeds)
        {
            var currencyName = seed.CurrencyName;
            var currencyIcon = 0u;
            if (seed.CurrencyId != 0 && currencyMap.TryGetValue(seed.CurrencyId, out var seedCurrency))
            {
                currencyName = string.IsNullOrWhiteSpace(currencyName) ? seedCurrency.CurrencyName : currencyName;
                currencyIcon = seedCurrency.IconId;
            }

            if (seed.ItemId == 0 || seed.Cost == 0 || seed.QuantityReceived == 0 || string.IsNullOrWhiteSpace(currencyName))
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

            if (currencyIcon == 0 && seed.CurrencyId != 0)
            {
                var currencyItem = itemSheet.GetRow(seed.CurrencyId);
                if (currencyItem.RowId == seed.CurrencyId)
                {
                    currencyIcon = currencyItem.Icon;
                }
            }

            candidates.Add(new SpendableCurrencyItem(
                seed.ItemId,
                item.Name.ExtractText(),
                item.Icon,
                seed.CurrencyId,
                currencyName,
                currencyIcon,
                seed.Cost,
                seed.QuantityReceived,
                seed.SourceVendorName,
                seed.SourceShopName,
                seed.SourceZone,
                seed.SourceNotes ?? seed.VerificationSource,
                seed.TerritoryId,
                seed.MapId,
                seed.X,
                seed.Y,
                seed.Z,
                seed.AetheryteId,
                seed.LifestreamCommand,
                SpendableItemKind.Sellable,
                true,
                false));
        }

        return new CandidateBatch(candidates, invalid, unmarketable);
    }

    private static bool IsMarketable(Item item) => !item.IsUntradable && item.ItemSearchCategory.RowId > 0;

    private sealed class SeedCandidate
    {
        public uint ItemId { get; set; }

        public uint CurrencyId { get; set; }

        public string CurrencyName { get; set; } = string.Empty;

        public string? CurrencyKey { get; set; }

        public uint Cost { get; set; }

        public uint QuantityReceived { get; set; } = 1;

        public string? SourceShopName { get; set; }

        public string? SourceVendorName { get; set; }

        public string? SourceZone { get; set; }

        public string? SourceNotes { get; set; }

        public uint? TerritoryId { get; set; }

        public uint? MapId { get; set; }

        public float? X { get; set; }

        public float? Y { get; set; }

        public float? Z { get; set; }

        public uint? AetheryteId { get; set; }

        public string? LifestreamCommand { get; set; }

        public string? VerificationSource { get; set; }
    }

    private sealed class SeedCurrency
    {
        public uint CurrencyId { get; set; }

        public string CurrencyName { get; set; } = string.Empty;

        public uint IconId { get; set; }

        public uint? MaxAmount { get; set; }

        public string? SourceNotes { get; set; }
    }

    private sealed class SeedCatalog
    {
        public List<SeedCurrency> Currencies { get; set; } = [];

        public List<SeedCandidate> Items { get; set; } = [];
    }

    private sealed record CandidateBatch(
        IReadOnlyList<SpendableCurrencyItem> Candidates,
        int InvalidCount,
        int UnmarketableCount);
}
