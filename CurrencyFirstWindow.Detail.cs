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
        if (!ImGui.Begin($"Spend {currency.Name}###currency-detail", ref open))
        {
            this.detailOpen = open;
            ImGui.End();
            return;
        }

        this.detailOpen = open;
        ImGui.TextUnformatted(currency.Name);
        ImGui.TextUnformatted($"Shop items: {this.scannerService.GetAllItemsForCurrency(currency).Count:N0}");
        ImGui.TextUnformatted($"Sellable: {this.scannerService.GetSellableItemsForCurrency(currency).Count:N0}");
        if (ImGui.Button(this.scannerService.IsRefreshing ? "Refreshing..." : "Refresh"))
        {
            _ = this.scannerService.RefreshSelectedCurrencyAsync(this.configuration.PreferredWorldOrDc);
        }

        ImGui.Separator();
        foreach (var result in this.scannerService.GetResultsForCurrency(currency))
        {
            ImGui.TextUnformatted($"{result.Item.ItemName} | sales {result.Market.Sales24h:N0} | {FormatGil(result.GilPerCurrency)} gil/cur | {result.Confidence}");
        }

        foreach (var item in this.scannerService.GetOtherItemsForCurrency(currency))
        {
            ImGui.TextDisabled($"{item.ItemName} | {item.Cost:N0} | {SourceText(item)}");
        }

        this.DrawAdvancedDiagnostics();
        ImGui.End();
    }
}
