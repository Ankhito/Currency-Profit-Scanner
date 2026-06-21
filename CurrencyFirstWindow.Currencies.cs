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

        var visibleCurrencies = this.scannerService.Currencies
            .Where(currency => this.scannerService.GetSellableItemsForCurrency(currency).Count > 0)
            .ToList();
        var hiddenCount = this.scannerService.Currencies.Count - visibleCurrencies.Count;
        if (hiddenCount > 0)
        {
            ImGui.TextDisabled($"Hiding {hiddenCount:N0} tracked currenc{(hiddenCount == 1 ? "y" : "ies")} with no known marketable rewards.");
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollX |
            ImGuiTableFlags.Sortable |
            ImGuiTableFlags.SizingFixedFit;
        if (!ImGui.BeginTable("currencies", 9, flags))
        {
            return;
        }

        ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, 42);
        ImGui.TableSetupColumn("Currency", ImGuiTableColumnFlags.WidthFixed, 220f);
        ImGui.TableSetupColumn("Amount", ImGuiTableColumnFlags.WidthFixed, 120f);
        ImGui.TableSetupColumn("Full", ImGuiTableColumnFlags.WidthFixed, 60f);
        ImGui.TableSetupColumn("Shop", ImGuiTableColumnFlags.WidthFixed, 62f);
        ImGui.TableSetupColumn("Sellable", ImGuiTableColumnFlags.WidthFixed, 72f);
        ImGui.TableSetupColumn("Best gil/cur", ImGuiTableColumnFlags.WidthFixed, 96f);
        ImGui.TableSetupColumn("Best item", ImGuiTableColumnFlags.WidthFixed, 220f);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.WidthFixed, 82f);
        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableHeadersRow();

        foreach (var currency in this.SortCurrencies(visibleCurrencies))
        {
            var best = this.scannerService.GetBestResult(currency);
            ImGui.TableNextRow();
            this.IconCell(currency.IconId, 28f);
            this.Cell(currency.Name);
            this.Cell(currency.CurrentAmount is null ? "Unknown" : $"{currency.CurrentAmount:N0}/{currency.MaxAmount?.ToString("N0") ?? "?"}");
            this.Cell(currency.CurrentAmount is not null && currency.MaxAmount is > 0 ? $"{currency.CurrentAmount.Value * 100d / currency.MaxAmount.Value:N0}%" : "Unknown");
            this.Cell(this.scannerService.GetAllItemsForCurrency(currency).Count.ToString("N0"));
            this.Cell(this.scannerService.GetSellableItemsForCurrency(currency).Count.ToString("N0"));
            this.Cell(FormatGil(best?.GilPerCurrency));
            this.Cell(best?.Item.ItemName ?? "Unknown");
            ImGui.TableNextColumn();
            if (ImGui.Button($"Spend it##{currency.CurrencyId}-{currency.Name}"))
            {
                this.scannerService.SelectCurrency(currency);
                this.OpenDetailWindow();
                this.RefreshSelectedCurrency();
            }
        }

        ImGui.EndTable();
    }

    private IReadOnlyList<TrackedCurrencyModel> SortCurrencies(IReadOnlyList<TrackedCurrencyModel> currencies)
    {
        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.IsNull || sortSpecs.SpecsCount == 0)
        {
            return currencies;
        }

        var spec = sortSpecs.Specs;
        var ascending = spec.SortDirection == ImGuiSortDirection.Ascending;
        IOrderedEnumerable<TrackedCurrencyModel> ordered = spec.ColumnIndex switch
        {
            1 => ascending
                ? currencies.OrderBy(currency => currency.Name, StringComparer.OrdinalIgnoreCase)
                : currencies.OrderByDescending(currency => currency.Name, StringComparer.OrdinalIgnoreCase),
            2 => ascending
                ? currencies.OrderBy(currency => currency.CurrentAmount ?? 0)
                : currencies.OrderByDescending(currency => currency.CurrentAmount ?? 0),
            3 => ascending
                ? currencies.OrderBy(currency => FillPercent(currency))
                : currencies.OrderByDescending(currency => FillPercent(currency)),
            4 => ascending
                ? currencies.OrderBy(currency => this.scannerService.GetAllItemsForCurrency(currency).Count)
                : currencies.OrderByDescending(currency => this.scannerService.GetAllItemsForCurrency(currency).Count),
            5 => ascending
                ? currencies.OrderBy(currency => this.scannerService.GetSellableItemsForCurrency(currency).Count)
                : currencies.OrderByDescending(currency => this.scannerService.GetSellableItemsForCurrency(currency).Count),
            6 => ascending
                ? currencies.OrderBy(currency => this.scannerService.GetBestResult(currency)?.GilPerCurrency ?? double.MaxValue)
                : currencies.OrderByDescending(currency => this.scannerService.GetBestResult(currency)?.GilPerCurrency ?? 0),
            7 => ascending
                ? currencies.OrderBy(currency => this.scannerService.GetBestResult(currency)?.Item.ItemName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : currencies.OrderByDescending(currency => this.scannerService.GetBestResult(currency)?.Item.ItemName ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            _ => currencies.OrderBy(currency => currency.Name, StringComparer.OrdinalIgnoreCase),
        };

        return ordered.ThenBy(currency => currency.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static double FillPercent(TrackedCurrencyModel currency)
    {
        return currency.CurrentAmount is not null && currency.MaxAmount is > 0
            ? currency.CurrentAmount.Value / (double)currency.MaxAmount.Value
            : 0d;
    }
}
