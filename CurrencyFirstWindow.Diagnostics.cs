using Dalamud.Bindings.ImGui;

namespace CurrencyProfitScanner;

public sealed partial class CurrencyFirstWindow
{
    private void DrawAdvancedDiagnostics(bool forceOpen = false)
    {
        if (!forceOpen && !ImGui.CollapsingHeader("Advanced diagnostics"))
        {
            return;
        }

        if (forceOpen)
        {
            CurrencyUi.Section("Advanced diagnostics");
        }

        var candidate = this.scannerService.CandidateSourceStatus;
        ImGui.TextUnformatted($"Candidate source: {candidate.CandidateSourceType}");
        ImGui.TextUnformatted($"Candidate status: {candidate.CandidateLoadStatus}");
        ImGui.TextUnformatted($"Items loaded: {candidate.CandidateCount:N0}");
        ImGui.TextUnformatted($"Invalid: {candidate.CandidateInvalidCount:N0}");
        ImGui.TextUnformatted($"Non-marketable: {candidate.CandidateUnmarketableCount:N0}");
        ImGui.TextUnformatted($"Duplicates: {candidate.CandidateDuplicateCount:N0}");
        ImGui.Separator();
        ImGui.TextUnformatted($"Universalis: {this.universalisClient.Status}");
        ImGui.TextUnformatted($"Items requested: {this.universalisClient.LastItemsRequested:N0}");
        ImGui.TextUnformatted($"Items returned: {this.universalisClient.LastItemsReturned:N0}");
    }
}
