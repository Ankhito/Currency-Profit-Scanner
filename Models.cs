namespace CurrencyProfitScanner;

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
