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
        var currencies = new List<TrackedCurrencyModel>();
        var items = new List<SpendableCurrencyItem>();
        var invalid = 0;
        var unmarketable = 0;
        var duplicates = 0;
        string? lastError = null;

        var itemSheet = this.dataManager.GetExcelSheet<Item>();
        if (itemSheet is null)
        {
            return new CandidateCatalogLoadResult(LoadFallbackCurrencies(), [], new CandidateSourceStatus(
                LuminaAvailable: this.dataManager is not null,
                LuminaItemSheetAvailable: false,
                MarketabilityPath: "Item.ItemSearchCategory.RowId > 0 and Item.IsUntradable == false",
                CandidateSourceType: "Built-in currency catalog fallback",
                CandidateLoadStatus: "Lumina Item sheet unavailable. Showing built-in currency categories only.",
                CandidateCount: 0,
                CandidateInvalidCount: 0,
                CandidateUnmarketableCount: 0,
                CandidateDuplicateCount: 0,
                CandidateLastError: "Lumina Item sheet unavailable."));
        }

        try
        {
            var specialShopResult = this.LoadSpecialShopCandidates(itemSheet);
            currencies.AddRange(specialShopResult.Currencies);
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
            currencies.AddRange(seedResult.Currencies);
            items.AddRange(seedResult.Candidates);
            invalid += seedResult.InvalidCount;
            unmarketable += seedResult.UnmarketableCount;
        }
        catch (Exception ex)
        {
            lastError = $"Seed load failed: {ex.Message}";
        }

        var dedupedItems = new Dictionary<string, SpendableCurrencyItem>(StringComparer.Ordinal);
        foreach (var candidate in items)
        {
            var key = $"{candidate.ItemId}:{candidate.CurrencyId}:{candidate.CurrencyName}:{candidate.Cost}:{candidate.QuantityReceived}:{candidate.SourceShopName}:{candidate.SourceVendorName}";
            if (!dedupedItems.TryAdd(key, candidate))
            {
                duplicates++;
            }
        }

        currencies.AddRange(dedupedItems.Values.Select(item => new TrackedCurrencyModel(
            item.CurrencyId,
            item.CurrencyName,
            item.CurrencyIconId,
            null,
            null,
            true,
            item.SourceNotes ?? item.SourceShopName ?? "Spendable item")));

        var dedupedCurrencies = new Dictionary<string, TrackedCurrencyModel>(StringComparer.Ordinal);
        foreach (var currency in currencies)
        {
            if (currency.CurrencyId == 0 && string.IsNullOrWhiteSpace(currency.Name))
            {
                invalid++;
                continue;
            }

            var key = $"{currency.CurrencyId}:{currency.Name}";
            if (!dedupedCurrencies.TryGetValue(key, out var existing))
            {
                dedupedCurrencies[key] = currency;
                continue;
            }

            if (existing.IconId == 0 && currency.IconId != 0)
            {
                dedupedCurrencies[key] = existing with { IconId = currency.IconId };
            }
        }

        var usedFallbackCurrencies = dedupedCurrencies.Count == 0;
        var currencyList = usedFallbackCurrencies
            ? LoadFallbackCurrencies().ToList()
            : dedupedCurrencies.Values.OrderBy(currency => currency.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var statusText = usedFallbackCurrencies
            ? "Showing built-in currency categories. No Lumina/seed spendable rows were loaded yet."
            : $"Loaded {currencyList.Count} currenc{(currencyList.Count == 1 ? "y" : "ies")} and {dedupedItems.Count} spendable item(s).";

        var status = new CandidateSourceStatus(
            LuminaAvailable: true,
            LuminaItemSheetAvailable: true,
            MarketabilityPath: "Item.ItemSearchCategory.RowId > 0 and Item.IsUntradable == false",
            CandidateSourceType: usedFallbackCurrencies
                ? "Built-in currency catalog fallback + Lumina/JSON item enrichment"
                : "Lumina SpecialShop + standalone JSON currencies + validated JSON seed",
            CandidateLoadStatus: statusText,
            CandidateCount: dedupedItems.Count,
            CandidateInvalidCount: invalid,
            CandidateUnmarketableCount: unmarketable,
            CandidateDuplicateCount: duplicates,
            CandidateLastError: lastError);

        return new CandidateCatalogLoadResult(currencyList, dedupedItems.Values.ToList(), status);
    }

    public string ResolveItemName(uint itemId)
    {
        var item = this.dataManager.GetExcelSheet<Item>()?.GetRow(itemId);
        return item?.Name.ExtractText() ?? $"Item {itemId}";
    }

    private CandidateBatch LoadSpecialShopCandidates(Lumina.Excel.ExcelSheet<Item> itemSheet)
    {
        var currencies = new List<TrackedCurrencyModel>();
        var candidates = new List<SpendableCurrencyItem>();
        var invalid = 0;
        var unmarketable = 0;
        var shopSheet = this.dataManager.GetExcelSheet<SpecialShop>();
        if (shopSheet is null)
        {
            return new CandidateBatch(currencies, candidates, invalid, unmarketable);
        }

        foreach (var shop in shopSheet)
        {
            var shopName = shop.Name.ExtractText();
            foreach (var entry in shop.Item)
            {
                var pairCount = Math.Min(entry.ReceiveItems.Count, entry.ItemCosts.Count);
                for (var i = 0; i < pairCount; i++)
                {
                    var received = entry.ReceiveItems[i];
                    var cost = entry.ItemCosts[i];
                    var itemId = received.Item.RowId;
                    var costItemId = cost.ItemCost.RowId;

                    // SpecialShop contains many empty padded slots. Skip these silently; they are not invalid data.
                    if (itemId == 0 && costItemId == 0 && cost.CurrencyCost == 0 && received.ReceiveCount == 0)
                    {
                        continue;
                    }

                    if (itemId == 0 || costItemId == 0 || cost.CurrencyCost == 0 || received.ReceiveCount == 0)
                    {
                        continue;
                    }

                    var item = itemSheet.GetRow(itemId);
                    var costItem = itemSheet.GetRow(costItemId);
                    if (item.RowId != itemId || costItem.RowId != costItemId)
                    {
                        invalid++;
                        continue;
                    }

                    var currencyName = costItem.Name.ExtractText();
                    if (string.IsNullOrWhiteSpace(currencyName))
                    {
                        currencyName = $"Currency {costItemId}";
                    }

                    var sourceShopName = string.IsNullOrWhiteSpace(shopName) ? $"SpecialShop {shop.RowId}" : shopName;
                    currencies.Add(new TrackedCurrencyModel(
                        costItemId,
                        currencyName,
                        costItem.Icon,
                        null,
                        null,
                        true,
                        sourceShopName));

                    var marketable = IsMarketable(item);
                    if (!marketable)
                    {
                        unmarketable++;
                    }

                    candidates.Add(new SpendableCurrencyItem(
                        itemId,
                        item.Name.ExtractText(),
                        item.Icon,
                        costItemId,
                        currencyName,
                        costItem.Icon,
                        cost.CurrencyCost,
                        received.ReceiveCount,
                        null,
                        sourceShopName,
                        null,
                        marketable
                            ? $"Lumina SpecialShop row {shop.RowId}; receive/cost index {i}."
                            : $"Lumina SpecialShop row {shop.RowId}; non-marketable reward at receive/cost index {i}.",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        marketable ? SpendableItemKind.Sellable : SpendableItemKind.Other,
                        marketable,
                        false));
                }
            }
        }

        return new CandidateBatch(currencies, candidates, invalid, unmarketable);
    }

    private CandidateBatch LoadSeedCandidates(Lumina.Excel.ExcelSheet<Item> itemSheet)
    {
        var currencies = new List<TrackedCurrencyModel>();
        var candidates = new List<SpendableCurrencyItem>();
        var invalid = 0;
        var unmarketable = 0;
        var seedPath = Path.Combine(this.configDirectory, "currency-candidates.json");
        if (!File.Exists(seedPath))
        {
            return new CandidateBatch(currencies, candidates, invalid, unmarketable);
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
            return new CandidateBatch(currencies, candidates, invalid, unmarketable);
        }

        var seedCurrencies = catalog?.Currencies ?? [];
        foreach (var seedCurrency in seedCurrencies)
        {
            var model = this.BuildCurrencyModel(itemSheet, seedCurrency.CurrencyId, seedCurrency.CurrencyName, seedCurrency.IconId, seedCurrency.MaxAmount, "JSON seed currency");
            if (model is null)
            {
                invalid++;
                continue;
            }

            currencies.Add(model);
        }

        var seeds = legacySeeds ?? catalog?.Items;
        if (seeds is null)
        {
            return new CandidateBatch(currencies, candidates, invalid, unmarketable);
        }

        foreach (var seed in seeds)
        {
            var seedCurrency = seedCurrencies.FirstOrDefault(currency => currency.CurrencyId != 0 && currency.CurrencyId == seed.CurrencyId);
            var currencyName = string.IsNullOrWhiteSpace(seed.CurrencyName) ? seedCurrency?.CurrencyName ?? string.Empty : seed.CurrencyName;
            var currencyIcon = seedCurrency?.IconId ?? 0u;
            var currencyMax = seedCurrency?.MaxAmount;

            var currencyModel = this.BuildCurrencyModel(itemSheet, seed.CurrencyId, currencyName, currencyIcon, currencyMax, "JSON seed item");
            if (currencyModel is null || seed.ItemId == 0 || seed.Cost == 0 || seed.QuantityReceived == 0)
            {
                invalid++;
                continue;
            }

            currencies.Add(currencyModel);

            var item = itemSheet.GetRow(seed.ItemId);
            if (item.RowId != seed.ItemId)
            {
                invalid++;
                continue;
            }

            var marketable = IsMarketable(item);
            if (!marketable)
            {
                unmarketable++;
            }

            candidates.Add(new SpendableCurrencyItem(
                seed.ItemId,
                item.Name.ExtractText(),
                item.Icon,
                currencyModel.CurrencyId,
                currencyModel.Name,
                currencyModel.IconId,
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
                marketable ? SpendableItemKind.Sellable : SpendableItemKind.Other,
                marketable,
                false));
        }

        return new CandidateBatch(currencies, candidates, invalid, unmarketable);
    }

    private TrackedCurrencyModel? BuildCurrencyModel(
        Lumina.Excel.ExcelSheet<Item> itemSheet,
        uint currencyId,
        string? currencyName,
        uint iconId,
        uint? maxAmount,
        string source)
    {
        var name = currencyName ?? string.Empty;
        var icon = iconId;
        if (currencyId != 0)
        {
            var currencyItem = itemSheet.GetRow(currencyId);
            if (currencyItem.RowId == currencyId)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = currencyItem.Name.ExtractText();
                }

                if (icon == 0)
                {
                    icon = currencyItem.Icon;
                }
            }
        }

        if (currencyId == 0 && string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Currency {currencyId}";
        }

        return new TrackedCurrencyModel(currencyId, name, icon, null, maxAmount, true, source);
    }

    private static IReadOnlyList<TrackedCurrencyModel> LoadFallbackCurrencies() =>
    [
        new TrackedCurrencyModel(0, "Allagan Tomestones", 0, null, null, true, "Built-in category"),
        new TrackedCurrencyModel(0, "Crafter Scrips", 0, null, null, true, "Built-in category"),
        new TrackedCurrencyModel(0, "Gatherer Scrips", 0, null, null, true, "Built-in category"),
        new TrackedCurrencyModel(0, "Bicolor Gemstones", 0, null, null, true, "Built-in category"),
        new TrackedCurrencyModel(0, "Grand Company Seals", 0, null, null, true, "Built-in category"),
        new TrackedCurrencyModel(0, "Allied / Centurio / Sacks of Nuts", 0, null, null, true, "Built-in category"),
        new TrackedCurrencyModel(0, "Wolf Marks", 0, null, null, true, "Built-in category"),
        new TrackedCurrencyModel(0, "MGP", 0, null, null, true, "Built-in category"),
        new TrackedCurrencyModel(0, "Tribe Currencies", 0, null, null, true, "Built-in category"),
        new TrackedCurrencyModel(0, "Cosmic Exploration Currencies", 0, null, null, true, "Built-in category"),
    ];

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
        IReadOnlyList<TrackedCurrencyModel> Currencies,
        IReadOnlyList<SpendableCurrencyItem> Candidates,
        int InvalidCount,
        int UnmarketableCount);
}
