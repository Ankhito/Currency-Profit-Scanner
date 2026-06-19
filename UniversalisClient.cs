using System.Net;
using System.Text.Json;
using Dalamud.Plugin.Services;

namespace CurrencyProfitScanner;

public sealed class UniversalisClient : IDisposable
{
    private const string BaseUrl = "https://universalis.app/api/v2";
    private const int MaxItemsPerRequest = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient httpClient;
    private readonly IPluginLog log;
    private readonly Dictionary<string, CacheEntry> cache = new();

    public UniversalisClient(IPluginLog log)
    {
        this.log = log;
        this.httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
    }

    public string Status { get; private set; } = "Not queried.";

    public string BaseEndpoint => BaseUrl;

    public string? LastError { get; private set; }

    public DateTimeOffset? LastFetchUtc { get; private set; }

    public int LastItemsRequested { get; private set; }

    public int LastItemsReturned { get; private set; }

    public bool LastFetchUsedCache { get; private set; }

    public async Task<IReadOnlyDictionary<uint, MarketData>> GetMarketDataAsync(
        string worldOrDc,
        IReadOnlyCollection<uint> itemIds,
        TimeSpan cacheTtl,
        CancellationToken cancellationToken)
    {
        var cleanWorldOrDc = Uri.EscapeDataString(worldOrDc.Trim());
        this.LastItemsRequested = itemIds.Distinct().Count();
        this.LastItemsReturned = 0;
        this.LastFetchUsedCache = false;
        this.LastError = null;
        if (string.IsNullOrWhiteSpace(cleanWorldOrDc) || itemIds.Count == 0)
        {
            this.Status = string.IsNullOrWhiteSpace(cleanWorldOrDc)
                ? "World/DC/region is missing."
                : "No item IDs requested.";
            this.LastError = this.Status;
            return new Dictionary<uint, MarketData>();
        }

        var results = new Dictionary<uint, MarketData>();
        foreach (var chunk in itemIds.Distinct().Chunk(MaxItemsPerRequest))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = $"{cleanWorldOrDc}:{string.Join(",", chunk.Order())}";
            if (this.cache.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.CreatedAt < cacheTtl)
            {
                this.LastFetchUsedCache = true;
                foreach (var pair in cached.Data)
                {
                    results[pair.Key] = pair.Value;
                }

                continue;
            }

            var fetched = await this.FetchChunkAsync(cleanWorldOrDc, chunk, cancellationToken).ConfigureAwait(false);
            this.cache[key] = new CacheEntry(DateTimeOffset.UtcNow, fetched);
            this.LastFetchUtc = DateTimeOffset.UtcNow;
            this.Status = fetched.Values.Any(v => v.Error is null)
                ? $"Last query OK at {DateTimeOffset.UtcNow:HH:mm:ss} UTC."
                : fetched.Values.FirstOrDefault()?.Error ?? "No Universalis data returned.";
            this.LastError = fetched.Values.FirstOrDefault(v => v.Error is not null)?.Error;
            foreach (var pair in fetched)
            {
                results[pair.Key] = pair.Value;
            }
        }

        this.LastItemsReturned = results.Values.Count(v => v.Error is null);
        return results;
    }

    public void Dispose() => this.httpClient.Dispose();

    private async Task<IReadOnlyDictionary<uint, MarketData>> FetchChunkAsync(
        string worldOrDc,
        IReadOnlyCollection<uint> itemIds,
        CancellationToken cancellationToken)
    {
        var ids = string.Join(",", itemIds);
        var url = $"{BaseUrl}/{worldOrDc}/{ids}?listings=100&entriesToReturn=100&fields=items,lastUploadTime";

        try
        {
            using var response = await this.httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return itemIds.ToDictionary(id => id, id => ErrorData(id, "Universalis rate limit"));
            }

            if (!response.IsSuccessStatusCode)
            {
                return itemIds.ToDictionary(id => id, id => ErrorData(id, $"Universalis HTTP {(int)response.StatusCode}"));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<UniversalisMultiItemResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (payload?.Items is null)
            {
                return itemIds.ToDictionary(id => id, id => ErrorData(id, "Malformed Universalis response"));
            }

            return itemIds.ToDictionary(id => id, id => Normalize(id, payload.Items.TryGetValue(id.ToString(), out var item) ? item : null));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Failed to fetch Universalis data");
            return itemIds.ToDictionary(id => id, id => ErrorData(id, "Universalis request failed"));
        }
    }

    private static MarketData Normalize(uint itemId, UniversalisItemResponse? item)
    {
        if (item is null)
        {
            return ErrorData(itemId, "No Universalis item data");
        }

        var listings = item.Listings ?? [];
        var nqFloor = listings.Where(l => !l.Hq).Select(l => (uint?)l.PricePerUnit).Min();
        var hqFloor = listings.Where(l => l.Hq).Select(l => (uint?)l.PricePerUnit).Min();
        var totalQuantity = listings.Count == 0 ? null : (uint?)listings.Sum(l => (long)l.Quantity);
        var sales = (item.RecentHistory ?? [])
            .Select(s => new SaleEntry(
                itemId,
                s.PricePerUnit,
                s.Quantity,
                DateTimeOffset.FromUnixTimeSeconds(s.Timestamp),
                s.Hq,
                s.WorldName,
                s.WorldId))
            .ToList();

        return new MarketData(
            itemId,
            nqFloor,
            hqFloor,
            listings.Count,
            totalQuantity,
            item.LastUploadTime is > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(item.LastUploadTime.Value) : null,
            sales,
            null);
    }

    private static MarketData ErrorData(uint itemId, string error) => new(itemId, null, null, 0, null, null, [], error);

    private sealed record CacheEntry(DateTimeOffset CreatedAt, IReadOnlyDictionary<uint, MarketData> Data);

    private sealed class UniversalisMultiItemResponse
    {
        public Dictionary<string, UniversalisItemResponse>? Items { get; set; }
    }

    private sealed class UniversalisItemResponse
    {
        public List<UniversalisListing>? Listings { get; set; }

        public List<UniversalisSale>? RecentHistory { get; set; }

        public long? LastUploadTime { get; set; }
    }

    private sealed class UniversalisListing
    {
        public uint PricePerUnit { get; set; }

        public uint Quantity { get; set; }

        public bool Hq { get; set; }
    }

    private sealed class UniversalisSale
    {
        public uint PricePerUnit { get; set; }

        public uint Quantity { get; set; }

        public long Timestamp { get; set; }

        public bool Hq { get; set; }

        public string? WorldName { get; set; }

        public uint? WorldId { get; set; }
    }
}
