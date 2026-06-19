namespace CurrencyProfitScanner;

[Flags]
public enum SpendableItemKind
{
    Other = 0,
    Sellable = 1,
    Collectable = 2,
    Venture = 4,
    Currency = 8,
}

public sealed record TrackedCurrencyModel(
    uint CurrencyId,
    string Name,
    uint IconId,
    uint? CurrentAmount,
    uint? MaxAmount,
    bool Enabled,
    string Source);

public sealed record SpendableCurrencyItem(
    uint ItemId,
    string ItemName,
    uint IconId,
    uint CurrencyId,
    string CurrencyName,
    uint CurrencyIconId,
    uint Cost,
    uint QuantityReceived,
    string? SourceShopName,
    string? SourceVendorName,
    string? SourceZone,
    string? SourceNotes,
    SpendableItemKind ItemKind,
    bool IsMarketable,
    bool Disabled);

public sealed record MarketSnapshot(
    uint ItemId,
    uint? CurrentFloor,
    int Sales24h,
    uint UnitsSold24h,
    double? LastSaleAgeHours,
    double? MedianSalePrice24h,
    double? P25SalePrice24h,
    double? ConservativeSalePrice,
    uint? ActiveSupply,
    TimeSpan? DataAge,
    DateTimeOffset? LastCheckedUtc,
    string? LastError);

public sealed record ProfitResult(
    SpendableCurrencyItem Item,
    MarketSnapshot Market,
    double? GilPerCurrency,
    double? ExpectedTotal,
    double LiquidityScore,
    double FinalScore,
    string Confidence);

public sealed record CandidateCatalogLoadResult(
    IReadOnlyList<TrackedCurrencyModel> Currencies,
    IReadOnlyList<SpendableCurrencyItem> Items,
    CandidateSourceStatus Status);

public sealed record CurrencyVendorCandidate(
    uint ItemId,
    string ItemName,
    string CurrencyName,
    string? CurrencyKey,
    uint Cost,
    uint QuantityReceived,
    string? SourceVendorName,
    string? SourceShopName,
    string? Notes,
    bool IsMarketable);

public sealed record CandidateLoadResult(
    IReadOnlyList<CurrencyVendorCandidate> Candidates,
    CandidateSourceStatus Status);

public sealed record CandidateSourceStatus(
    bool LuminaAvailable,
    bool LuminaItemSheetAvailable,
    string MarketabilityPath,
    string CandidateSourceType,
    string CandidateLoadStatus,
    int CandidateCount,
    int CandidateInvalidCount,
    int CandidateUnmarketableCount,
    int CandidateDuplicateCount,
    string? CandidateLastError);

public sealed record RankingStatus(
    string Status,
    string? LastError,
    int RankedResultCount,
    int ZeroSaleCount,
    int SpeculativeCount,
    int StaleDataCount);

public sealed record SaleEntry(
    uint ItemId,
    uint PricePerUnit,
    uint Quantity,
    DateTimeOffset Timestamp,
    bool Hq,
    string? WorldName,
    uint? WorldId);

public sealed record MarketData(
    uint ItemId,
    uint? CurrentFloorPriceNq,
    uint? CurrentFloorPriceHq,
    int ListingsCount,
    uint? TotalListedQuantity,
    DateTimeOffset? LastUploadTime,
    IReadOnlyList<SaleEntry> Sales,
    string? Error);

public sealed record ScanResult(
    CurrencyVendorCandidate Candidate,
    MarketData? MarketData,
    int Sales24h,
    uint UnitsSold24h,
    double? LastSaleAgeHours,
    double? MedianSalePrice24h,
    double? P25SalePrice24h,
    uint? CurrentFloorPrice,
    uint? ActiveSupply,
    double? ConservativeSalePrice,
    double? NetGil,
    double? GilPerCurrency,
    double LiquidityScore,
    double SupplyPressureScore,
    double FinalScore,
    string Confidence,
    bool IsStale,
    string Status);
