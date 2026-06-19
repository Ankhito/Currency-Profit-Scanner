using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CurrencyProfitScanner;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/cps";

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    private readonly PluginConfiguration configuration;
    private readonly CurrencyCandidateSource candidateSource;
    private readonly UniversalisClient universalisClient;
    private readonly ProfitScannerService scannerService;
    private readonly IpcDiagnosticsService ipcDiagnosticsService;
    private readonly NavigationIpcService navigationIpcService;
    private readonly LuminaDiscoveryService luminaDiscoveryService;
    private readonly CurrencyFirstWindow scannerWindow;

    public Plugin()
    {
        this.configuration = PluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        this.configuration.Initialize(PluginInterface);

        this.candidateSource = new CurrencyCandidateSource(DataManager, PluginInterface.ConfigDirectory.FullName);
        this.universalisClient = new UniversalisClient(Log);
        this.scannerService = new ProfitScannerService(this.candidateSource, this.universalisClient, this.configuration, Log);
        this.ipcDiagnosticsService = new IpcDiagnosticsService();
        this.navigationIpcService = new NavigationIpcService(PluginInterface, Log);
        this.luminaDiscoveryService = new LuminaDiscoveryService(DataManager);
        this.scannerWindow = new CurrencyFirstWindow(this.configuration, this.scannerService, this.universalisClient, this.ipcDiagnosticsService, this.navigationIpcService);

        PluginInterface.UiBuilder.Draw += this.DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += this.OpenMainUi;

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the Currency Profit Scanner window.",
        });
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= this.DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= this.OpenMainUi;
        this.scannerWindow.Dispose();
        this.scannerService.Dispose();
        this.universalisClient.Dispose();
    }

    private void OnCommand(string command, string arguments) => this.scannerWindow.IsOpen = true;

    private void OpenConfigUi() => this.scannerWindow.IsOpen = true;

    private void OpenMainUi() => this.scannerWindow.IsOpen = true;

    private void DrawUi() => this.scannerWindow.Draw();
}
