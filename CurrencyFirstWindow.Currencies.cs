using Dalamud.Bindings.ImGui;

namespace CurrencyProfitScanner;

public sealed partial class CurrencyFirstWindow
{
    private void DrawCurrencies()
    {
        CurrencyUi.Section("Currencies");
        this.scannerService.RefreshCurrencyAmounts();
        if (this.scannerService.Currencies.Count == 0)
        {
            ImGui.TextWrapped("No currencies loaded yet. Add currencies/items to currency-candidates.json or use Lumina shop extraction, then reload.");
            if (ImGui.Button("Create seed template"))
            {
                this.CreateSeedTemplate();
            }
            return;
        }

        if (!ImGui.BeginTable("currencies", 9, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX))
        {
            return;
        }

        ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, 42);
        ImGui.TableSetupColumn("Currency");
        ImGui.TableSetupColumn("Amount");
        ImGui.TableSetupColumn("Full");
        ImGui.TableSetupColumn("Shop items");
        ImGui.TableSetupColumn("Sellable");
        ImGui.TableSetupColumn("Best gil/cur");
        ImGui.TableSetupColumn("Best item");
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.NoSort);
        ImGui.TableHeadersRow();

        foreach (var currency in this.scannerService.Currencies)
        {
            ImGui.TableNextRow();
            this.Cell(currency.IconId == 0 ? "-" : $"#{currency.IconId}");
            this.Cell(currency.Name);
            this.Cell(currency.CurrentAmount is null ? "Unknown" : $"{currency.CurrentAmount:N0}/{currency.MaxAmount?.ToString("N0") ?? "?"}");
            this.Cell(currency.CurrentAmount is not null && currency.MaxAmount is > 0 ? $"{currency.CurrentAmount.Value * 100d / currency.MaxAmount.Value:N0}%" : "Unknown");
            this.Cell(this.scannerService.GetAllItemsForCurrency(currency).Count.ToString("N0"));
            this.Cell(this.scannerService.GetSellableItemsForCurrency(currency).Count.ToString("N0"));
            this.Cell(FormatGil(this.scannerService.GetBestGilPerCurrency(currency)));
            this.Cell(this.scannerService.GetBestItemName(currency) ?? "Unknown");
            ImGui.TableNextColumn();
            if (ImGui.Button($"Spend it##{currency.CurrencyId}-{currency.Name}"))
            {
                this.scannerService.SelectCurrency(currency);
                this.detailOpen = true;
            }
        }

        ImGui.EndTable();
    }
}
