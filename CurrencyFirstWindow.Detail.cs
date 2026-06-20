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
        CurrencyUi.PushTheme();
        if (!ImGui.Begin($"Spend {currency.Name}###currency-detail", ref open))
        {
            this.detailOpen = open;
            ImGui.End();
            CurrencyUi.PopTheme();
            return;
        }

        this.detailOpen = open;
        ImGui.TextUnformatted(currency.Name);
        ImGui.TextUnformatted($"Shop items: {this.scannerService.GetAllItemsForCurrency(currency).Count:N0}");
        ImGui.TextUnformatted($"Sellable: {this.scannerService.GetSellableItemsForCurrency(currency).Count:N0}");
        ImGui.TextUnformatted($"Universalis target: {this.EffectiveWorldOrDc}");
        ImGui.TextUnformatted($"Market source: {this.universalisClient.Status}");
        var canRefresh = !this.scannerService.IsRefreshing && !string.IsNullOrWhiteSpace(this.EffectiveWorldOrDc);
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
        }

        ImGui.Separator();
        this.DrawProfitTable(currency);
        this.DrawItemBucket("Unlocks / Collectables", this.scannerService.GetItemsByKindForCurrency(currency, SpendableItemKind.Collectable));
        this.DrawItemBucket("Ventures", this.scannerService.GetItemsByKindForCurrency(currency, SpendableItemKind.Venture));
        this.DrawItemBucket("Other Purchasables", this.scannerService.GetOtherItemsForCurrency(currency));

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

        if (!ImGui.BeginTable($"profit-table-{currency.CurrencyId}-{currency.Name}", 12, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX))
        {
            return;
        }

        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Cost");
        ImGui.TableSetupColumn("Qty");
        ImGui.TableSetupColumn("Sales 24h");
        ImGui.TableSetupColumn("Units 24h");
        ImGui.TableSetupColumn("Floor");
        ImGui.TableSetupColumn("Gil/cur");
        ImGui.TableSetupColumn("Score");
        ImGui.TableSetupColumn("Market");
        ImGui.TableSetupColumn("Vendor/Shop");
        ImGui.TableSetupColumn("Zone");
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort);
        ImGui.TableHeadersRow();

        foreach (var result in rows)
        {
            var item = result.Item;
            ImGui.TableNextRow();
            this.Cell(item.ItemName);
            this.Cell(item.Cost.ToString("N0"));
            this.Cell(item.QuantityReceived.ToString("N0"));
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

    private void DrawItemBucket(string label, IReadOnlyList<SpendableCurrencyItem> items)
    {
        if (items.Count == 0 || !ImGui.CollapsingHeader($"{label} ({items.Count:N0})"))
        {
            return;
        }

        if (!ImGui.BeginTable($"{label}-table", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX))
        {
            return;
        }

        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("Cost");
        ImGui.TableSetupColumn("Vendor/Shop");
        ImGui.TableSetupColumn("Zone");
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort);
        ImGui.TableHeadersRow();

        foreach (var item in items)
        {
            ImGui.TableNextRow();
            this.Cell(item.ItemName);
            this.Cell(item.Cost.ToString("N0"));
            this.Cell(VendorText(item));
            this.Cell(item.SourceZone ?? "Unknown");
            ImGui.TableNextColumn();
            this.DrawItemActions(item);
        }

        ImGui.EndTable();
    }

    private void DrawItemActions(SpendableCurrencyItem item)
    {
        if (ImGui.Button($"Copy name##name-{item.ItemId}-{item.CurrencyId}-{item.Cost}"))
        {
            ImGui.SetClipboardText(item.ItemName);
        }

        ImGui.SameLine();
        if (ImGui.Button($"Copy ID##id-{item.ItemId}-{item.CurrencyId}-{item.Cost}"))
        {
            ImGui.SetClipboardText(item.ItemId.ToString());
        }
    }

    private static string VendorText(SpendableCurrencyItem item)
    {
        return string.Join(" / ", new[] { item.SourceVendorName, item.SourceShopName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
