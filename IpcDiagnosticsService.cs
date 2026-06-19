namespace CurrencyProfitScanner;

/*
 * IPC discovery note, 2026-06-19:
 * Targeted repo searches were performed for GetIpcSubscriber, GetIpcProvider,
 * ICallGateSubscriber, ICallGateProvider, IPC, Ipc, pluginInterface,
 * DalamudPluginInterface, Allagan, Inventory, Market, Universalis, Currency,
 * and Retainer.
 *
 * No exact provider/subscriber names or typed call-gate signatures were found
 * in this repo. No IPC integrations are registered or used by this MVP.
 * Do not add IPC here without proving the exact provider/subscriber name,
 * generic type signature, argument meaning, return type, error behavior, and
 * dependency source from local source or documentation.
 */
public sealed class IpcDiagnosticsService
{
    public string Status => "No verified IPC integrations used.";

    public string ContractsFound => "None verified in local repo/docs.";
}
