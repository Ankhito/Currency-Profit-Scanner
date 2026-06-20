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
            currencies.AddRange(this.LoadKnownCurrencies(itemSheet));

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

        var dedupedItems = new Dictionary<string, SpendableCurrencyItem>(StringComparer.Ordinal);
        foreach (var candidate in items)
        {
            var key = $"{candidate.ItemId}:{candidate.CurrencyId}:{candidate.CurrencyName}:{candidate.Cost}:{candidate.QuantityReceived}:{candidate.SourceShopName}:{candidate.SourceVendorName}";
            if (!dedupedItems.TryAdd(key, candidate))
            {
                duplicates++;
            }
        }

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
                ? "CurrencySpender tracked currency catalog fallback"
                : "CurrencySpender tracked currency catalog + Lumina/JSON item enrichment",
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
                    var rawCostItemId = cost.ItemCost.RowId;
                    var costItemId = this.ConvertCurrencyId(shop.RowId, rawCostItemId, shop.UseCurrencyType);

                    // SpecialShop contains many empty padded slots. Skip these silently; they are not invalid data.
                    if (itemId == 0 && rawCostItemId == 0 && cost.CurrencyCost == 0 && received.ReceiveCount == 0)
                    {
                        continue;
                    }

                    if (itemId == 0 || rawCostItemId == 0 || costItemId == 0 || cost.CurrencyCost == 0 || received.ReceiveCount == 0)
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

                    var kind = marketable ? SpendableItemKind.Sellable : InferKind(item.Name.ExtractText());
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
                            ? $"Lumina SpecialShop row {shop.RowId}; receive/cost index {i}; raw currency {rawCostItemId}."
                            : $"Lumina SpecialShop row {shop.RowId}; non-marketable reward at receive/cost index {i}; raw currency {rawCostItemId}.",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        kind,
                        marketable,
                        false));
                }
            }
        }

        return new CandidateBatch(currencies, candidates, invalid, unmarketable);
    }

    private uint ConvertCurrencyId(uint specialShopId, uint itemId, ushort useCurrencyType)
    {
        if (specialShopId == 1770637)
        {
            return CurrencyTypeMap.GetValueOrDefault(itemId, itemId);
        }

        if (specialShopId == 1770446 || (specialShopId == 1770699 && itemId < 10))
        {
            return CurrencyTypeMap.GetValueOrDefault(itemId, this.GetTomestoneCurrencyId(itemId) ?? itemId);
        }

        if ((useCurrencyType == 2 || useCurrencyType == 4) && itemId < 10)
        {
            return this.GetTomestoneCurrencyId(itemId) ?? itemId;
        }

        if (useCurrencyType == 16 && itemId < 10)
        {
            return CurrencyTypeMap.GetValueOrDefault(itemId, itemId);
        }

        return itemId;
    }

    private uint? GetTomestoneCurrencyId(uint itemId)
    {
        if (itemId == 1)
        {
            return 28;
        }

        try
        {
            var tomestoneSheet = this.dataManager.GetExcelSheet<TomestonesItem>();
            return itemId switch
            {
                2 => tomestoneSheet?.FirstOrDefault(item => item.Tomestones.RowId is 2).Item.RowId,
                3 => tomestoneSheet?.FirstOrDefault(item => item.Tomestones.RowId is 3).Item.RowId,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
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

            var kind = marketable ? SpendableItemKind.Sellable : InferKind(item.Name.ExtractText());
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
                kind,
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

    private IReadOnlyList<TrackedCurrencyModel> LoadKnownCurrencies(Lumina.Excel.ExcelSheet<Item> itemSheet)
    {
        var currencies = new List<TrackedCurrencyModel>();
        foreach (var currency in KnownCurrencies)
        {
            var model = this.BuildCurrencyModel(itemSheet, currency.CurrencyId, currency.Name, 0, currency.MaxAmount, currency.Source);
            if (model is not null)
            {
                currencies.Add(model);
            }
        }

        try
        {
            var tomestoneSheet = this.dataManager.GetExcelSheet<TomestonesItem>();
            var nonLimited = tomestoneSheet?.FirstOrDefault(item => item.Tomestones.RowId is 2);
            var limited = tomestoneSheet?.FirstOrDefault(item => item.Tomestones.RowId is 3);
            if (nonLimited is { RowId: not 0 })
            {
                var model = this.BuildCurrencyModel(itemSheet, nonLimited.Value.Item.RowId, null, 0, 2000, "Known currency catalog: current non-limited tomestone");
                if (model is not null)
                {
                    currencies.Add(model);
                }
            }

            if (limited is { RowId: not 0 })
            {
                var model = this.BuildCurrencyModel(itemSheet, limited.Value.Item.RowId, null, 0, 2000, "Known currency catalog: current weekly-capped tomestone");
                if (model is not null)
                {
                    currencies.Add(model);
                }
            }
        }
        catch
        {
            // Optional expansion only; static known currencies still cover the common list.
        }

        return currencies;
    }

    private static IReadOnlyList<TrackedCurrencyModel> LoadFallbackCurrencies() =>
    [
        .. KnownCurrencies.Select(currency => new TrackedCurrencyModel(
            currency.CurrencyId,
            currency.Name,
            0,
            null,
            currency.MaxAmount,
            true,
            currency.Source)),
    ];

    private static readonly KnownCurrency[] KnownCurrencies =
    [
        new(20, "Storm Seal", 90000, "Known currency catalog: Grand Company"),
        new(21, "Serpent Seal", 90000, "Known currency catalog: Grand Company"),
        new(22, "Flame Seal", 90000, "Known currency catalog: Grand Company"),
        new(29, "MGP", 9999999, "Known currency catalog: Gold Saucer"),
        new(28, "Allagan Tomestone of Poetics", 2000, "Known currency catalog: tomestone"),
        new(25, "Wolf Mark", 20000, "Known currency catalog: PvP"),
        new(36656, "Trophy Crystal", 20000, "Known currency catalog: PvP"),
        new(27, "Allied Seal", 4000, "Known currency catalog: hunt"),
        new(10307, "Centurio Seal", 4000, "Known currency catalog: hunt"),
        new(13625, "Centurio Clan Mark Log", null, "Known currency catalog: hunt sub-currency"),
        new(20308, "Veteran's Clan Mark Log", null, "Known currency catalog: hunt sub-currency"),
        new(21103, "Mythic Clan Mark Log", null, "Known currency catalog: hunt sub-currency"),
        new(26533, "Sack of Nuts", 4000, "Known currency catalog: hunt"),
        new(26807, "Bicolor Gemstone", 1500, "Known currency catalog: FATE"),
        new(35833, "Bicolor Gemstone Voucher", null, "Known currency catalog: FATE sub-currency"),
        new(43961, "Turali Bicolor Gemstone Voucher", null, "Known currency catalog: FATE sub-currency"),
        new(33913, "Purple Crafters' Scrip", 4000, "Known currency catalog: crafter scrip"),
        new(41784, "Orange Crafters' Scrip", 4000, "Known currency catalog: crafter scrip"),
        new(33914, "Purple Gatherers' Scrip", 4000, "Known currency catalog: gatherer scrip"),
        new(41785, "Orange Gatherers' Scrip", 4000, "Known currency catalog: gatherer scrip"),
        new(12839, "Crafter's Delineation", null, "Known currency catalog: scrip sub-currency"),
        new(41807, "Scrip Exchange Voucher", null, "Known currency catalog: scrip sub-currency"),
        new(28063, "Skybuilders' Scrip", 10000, "Known currency catalog: restoration"),
        new(37549, "Seafarer's Cowrie", 9999999, "Known currency catalog: Island Sanctuary"),
        new(37550, "Islander Cowrie", 9999999, "Known currency catalog: Island Sanctuary"),
        new(45690, "Cosmocredit", 30000, "Known currency catalog: Cosmic Exploration"),
        new(45691, "Lunar Credit", 10000, "Known currency catalog: Cosmic Exploration"),
        new(48146, "Phaenna Credit", 10000, "Known currency catalog: Cosmic Exploration"),
        new(48147, "Oizys Credit", 10000, "Known currency catalog: Cosmic Exploration"),
        new(48148, "Auxesia Credit", 10000, "Known currency catalog: Cosmic Exploration"),
    ];

    private static readonly Dictionary<uint, uint> CurrencyTypeMap = new()
    {
        [1] = 10309,
        [2] = 33913,
        [3] = 10311,
        [4] = 33914,
        [5] = 10307,
        [6] = 41784,
        [7] = 41785,
        [8] = 21072,
        [9] = 21073,
        [10] = 21074,
        [11] = 21075,
        [12] = 21076,
        [13] = 21077,
        [14] = 21078,
        [15] = 21079,
        [16] = 21080,
        [17] = 21081,
        [18] = 21172,
        [19] = 21173,
        [20] = 21935,
        [21] = 22525,
        [22] = 26533,
        [23] = 26807,
        [24] = 28063,
        [25] = 28186,
        [26] = 28187,
        [27] = 28188,
        [28] = 30341,
    };

    private static bool IsMarketable(Item item) => !item.IsUntradable && item.ItemSearchCategory.RowId > 0;

    private static SpendableItemKind InferKind(string itemName)
    {
        if (itemName.Contains("Venture", StringComparison.OrdinalIgnoreCase))
        {
            return SpendableItemKind.Venture;
        }

        if (itemName.Contains("Orchestrion", StringComparison.OrdinalIgnoreCase) ||
            itemName.Contains("Triple Triad", StringComparison.OrdinalIgnoreCase) ||
            itemName.Contains("Card", StringComparison.OrdinalIgnoreCase) ||
            itemName.Contains("Minion", StringComparison.OrdinalIgnoreCase) ||
            itemName.Contains("Mount", StringComparison.OrdinalIgnoreCase) ||
            itemName.Contains("Roll", StringComparison.OrdinalIgnoreCase) ||
            itemName.Contains("Emote", StringComparison.OrdinalIgnoreCase) ||
            itemName.Contains("Hairstyle", StringComparison.OrdinalIgnoreCase) ||
            itemName.Contains("Framer", StringComparison.OrdinalIgnoreCase))
        {
            return SpendableItemKind.Collectable;
        }

        return SpendableItemKind.Other;
    }

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

    private sealed record KnownCurrency(uint CurrencyId, string Name, uint? MaxAmount, string Source);
}
