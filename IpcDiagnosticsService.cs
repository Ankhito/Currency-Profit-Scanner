namespace CurrencyProfitScanner;

/*
 * IPC discovery note, 2026-06-19:
 * Targeted repo searches were performed for GetIpcSubscriber, GetIpcProvider,
 * ICallGateSubscriber, ICallGateProvider, IPC, Ipc, pluginInterface,
 * DalamudPluginInterface, Allagan, Inventory, Market, Universalis, Currency,
 * and Retainer.
 *
 * The initial scanner did not use IPC. Navigation IPC was later added only
 * after exact local source contracts were found in Huntsman/PositionalPilot
 * wrappers for Lifestream and vnavmesh.
 * Do not add more IPC here without proving the exact provider/subscriber name,
 * generic type signature, argument meaning, return type, error behavior, and
 * dependency source from local source or documentation.
 */
public sealed class IpcDiagnosticsService
{
    public string Status => "Verified navigation IPC only; no buying/listing/repricing IPC.";

    public string ContractsFound => "Lifestream and vnavmesh navigation contracts verified from local wrapper source.";
}
