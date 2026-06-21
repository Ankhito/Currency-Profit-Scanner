using Dalamud.Bindings.ImGui;

namespace CurrencyProfitScanner;

public sealed partial class CurrencyFirstWindow
{
    private void DrawDetailWindow()
    {
        if (!this.detailOpen || this.scannerService.SelectedCurrency is not { } currency)
        {
            return;
        }

        var open = this.detailOpen;
        if (this.detailFocusRequested)
        {
            ImGui.SetNextWindowCollapsed(false);
            ImGui.SetNextWindowFocus();
        }

        CurrencyUi.PushTheme();
        if (!ImGui.Begin($"Spend {currency.Name}###currency-detail", ref open))
        {
            this.detailOpen = open;
            this.detailFocusRequested = false;
            ImGui.End();
            CurrencyUi.PopTheme();
            return;
        }

        this.detailOpen = open;
        this.detailFocusRequested = false;
        ImGui.TextUnformatted(currency.Name);
        ImGui.TextUnformatted($"Shop items: {this.scannerService.GetAllItemsForCurrency(currency).Count:N0}");
        ImGui.TextUnformatted($"Sellable: {this.scannerService.GetSellableItemsForCurrency(currency).Count:N0}");
        ImGui.TextUnformatted($"Universalis target: {this.EffectiveWorldOrDc}");
        ImGui.TextUnformatted($"Market source: {this.universalisClient.Status}");
        ImGui.TextWrapped(this.scannerService.Status);
        var canRefresh = this.CanRefreshMarket(currency);
        if (!canRefresh)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button(this.scannerService.IsRefreshing ? "Refreshing..." : "Refresh"))
        {
            this.RefreshSelectedCurrency();
        }

        if (!canRefresh)
        {
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(this.RefreshDisabledReason(currency));
            }
        }

        ImGui.Separator();
        this.DrawProfitTable(currency);

        this.DrawAdvancedDiagnostics();
        ImGui.End();
        CurrencyUi.PopTheme();
    }

    private void DrawProfitTable(TrackedCurrencyModel currency)
    {
        var candidates = this.scannerService.GetSellableItemsForCurrency(currency);
        if (!ImGui.CollapsingHeader($"Market Board Profit##profit-{currency.CurrencyId}-{currency.Name}", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (candidates.Count == 0)
        {
            ImGui.TextDisabled("No marketable rewards are known for this currency yet.");
            return;
        }

        var rows = this.scannerService.GetResultsForCurrency(currency);
        if (rows.Count == 0)
        {
            ImGui.TextDisabled("Refresh market data to rank these rewards with Universalis sales and supply data.");
            rows = candidates.Select(item => new ProfitResult(
                item,
                new MarketSnapshot(item.ItemId, null, 0, 0, null, null, null, null, null, null, null, "Not fetched"),
                null,
                null,
                0,
                0,
                "Not fetched")).ToList();
        }

        rows = this.DedupeProfitRows(rows);
        var tableHeight = Math.Max(240f, ImGui.GetContentRegionAvail().Y * 0.72f);
        const ImGuiTableFlags flags = ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollX |
            ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.Sortable |
            ImGuiTableFlags.SizingFixedFit;
        if (!ImGui.BeginTable($"profit-table-{currency.CurrencyId}-{currency.Name}", 11, flags, new(0, tableHeight)))
        {
            return;
        }

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthFixed, 230f);
        ImGui.TableSetupColumn("Cost", ImGuiTableColumnFlags.WidthFixed, 58f);
        ImGui.TableSetupColumn("Sales", ImGuiTableColumnFlags.WidthFixed, 58f);
        ImGui.TableSetupColumn("Units", ImGuiTableColumnFlags.WidthFixed, 58f);
        ImGui.TableSetupColumn("Floor", ImGuiTableColumnFlags.WidthFixed, 82f);
        ImGui.TableSetupColumn("Gil/cur", ImGuiTableColumnFlags.WidthFixed, 82f);
        ImGui.TableSetupColumn("Score", ImGuiTableColumnFlags.WidthFixed, 68f);
        ImGui.TableSetupColumn("Market", ImGuiTableColumnFlags.WidthFixed, 96f);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 170f);
        ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthFixed, 110f);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.WidthFixed, 76f);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();

        rows = this.SortProfitRows(rows);

        foreach (var result in rows)
        {
            var item = result.Item;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            this.DrawIcon(item.IconId, 22f);
            ImGui.SameLine();
            ImGui.TextUnformatted(item.ItemName);
            this.Cell(item.QuantityReceived == 1 ? item.Cost.ToString("N0") : $"{item.Cost:N0} / {item.QuantityReceived:N0}");
            this.Cell(result.Market.Sales24h.ToString("N0"));
            this.Cell(result.Market.UnitsSold24h.ToString("N0"));
            this.Cell(result.Market.CurrentFloor?.ToString("N0") ?? "Unknown");
            this.Cell(FormatGil(result.GilPerCurrency));
            this.Cell(result.FinalScore.ToString("N2"));
            this.Cell(result.Confidence);
            this.Cell(VendorText(item));
            this.Cell(item.SourceZone ?? "Unknown");
            ImGui.TableNextColumn();
            this.DrawItemActions(item);
        }

        ImGui.EndTable();
    }

    private void DrawItemActions(SpendableCurrencyItem item)
    {
        if (ImGui.Button($"Name##name-{item.ItemId}-{item.CurrencyId}-{item.Cost}"))
        {
            ImGui.SetClipboardText(item.ItemName);
        }

        ImGui.SameLine();
        if (ImGui.Button($"ID##id-{item.ItemId}-{item.CurrencyId}-{item.Cost}"))
        {
            ImGui.SetClipboardText(item.ItemId.ToString());
        }
    }

    private IReadOnlyList<ProfitResult> DedupeProfitRows(IReadOnlyList<ProfitResult> rows)
    {
        return rows
            .GroupBy(result => $"{result.Item.ItemId}:{result.Item.Cost}:{result.Item.QuantityReceived}:{result.Item.CurrencyId}", StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var shops = group
                    .Select(result => VendorText(result.Item))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (shops.Count <= 1)
                {
                    return first;
                }

                return first with
                {
                    Item = first.Item with
                    {
                        SourceVendorName = null,
                        SourceShopName = $"{shops.Count:N0} shops",
                    },
                };
            })
            .ToList();
    }

    private IReadOnlyList<ProfitResult> SortProfitRows(IReadOnlyList<ProfitResult> rows)
    {
        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.IsNull || sortSpecs.SpecsCount == 0)
        {
            return rows;
        }

        var spec = sortSpecs.Specs;
        var ascending = spec.SortDirection == ImGuiSortDirection.Ascending;
        IOrderedEnumerable<ProfitResult> ordered = spec.ColumnIndex switch
        {
            0 => ascending
                ? rows.OrderBy(result => result.Item.ItemName, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(result => result.Item.ItemName, StringComparer.OrdinalIgnoreCase),
            1 => ascending
                ? rows.OrderBy(result => result.Item.Cost)
                : rows.OrderByDescending(result => result.Item.Cost),
            2 => ascending
                ? rows.OrderBy(result => result.Market.Sales24h)
                : rows.OrderByDescending(result => result.Market.Sales24h),
            3 => ascending
                ? rows.OrderBy(result => result.Market.UnitsSold24h)
                : rows.OrderByDescending(result => result.Market.UnitsSold24h),
            4 => ascending
                ? rows.OrderBy(result => result.Market.CurrentFloor ?? uint.MaxValue)
                : rows.OrderByDescending(result => result.Market.CurrentFloor ?? 0),
            5 => ascending
                ? rows.OrderBy(result => result.GilPerCurrency ?? double.MaxValue)
                : rows.OrderByDescending(result => result.GilPerCurrency ?? 0),
            6 => ascending
                ? rows.OrderBy(result => result.FinalScore)
                : rows.OrderByDescending(result => result.FinalScore),
            7 => ascending
                ? rows.OrderBy(result => result.Confidence, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(result => result.Confidence, StringComparer.OrdinalIgnoreCase),
            8 => ascending
                ? rows.OrderBy(result => VendorText(result.Item), StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(result => VendorText(result.Item), StringComparer.OrdinalIgnoreCase),
            9 => ascending
                ? rows.OrderBy(result => result.Item.SourceZone ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : rows.OrderByDescending(result => result.Item.SourceZone ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderByDescending(result => result.FinalScore),
        };

        return ordered
            .ThenBy(result => result.Item.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string VendorText(SpendableCurrencyItem item)
    {
        return string.Join(" / ", new[] { item.SourceVendorName, item.SourceShopName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
