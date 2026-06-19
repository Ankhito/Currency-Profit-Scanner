using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace CurrencyProfitScanner;

public sealed class NavigationIpcService
{
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<string, object> lifestreamExecuteCommand;
    private readonly ICallGateSubscriber<bool> lifestreamIsBusy;
    private readonly ICallGateSubscriber<object> lifestreamAbort;
    private readonly ICallGateSubscriber<string, bool> lifestreamAethernetTeleport;
    private readonly ICallGateSubscriber<Vector3, bool, bool> vnavMoveTo;
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> vnavMoveCloseTo;
    private readonly ICallGateSubscriber<object> vnavStop;
    private readonly ICallGateSubscriber<bool> vnavIsRunning;
    private readonly ICallGateSubscriber<bool> vnavIsReady;

    public NavigationIpcService(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        this.lifestreamExecuteCommand = pluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
        this.lifestreamIsBusy = pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        this.lifestreamAbort = pluginInterface.GetIpcSubscriber<object>("Lifestream.Abort");
        this.lifestreamAethernetTeleport = pluginInterface.GetIpcSubscriber<string, bool>("Lifestream.AethernetTeleport");
        this.vnavMoveTo = pluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        this.vnavMoveCloseTo = pluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        this.vnavStop = pluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        this.vnavIsRunning = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        this.vnavIsReady = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
    }

    public bool IsLifestreamAvailable => this.lifestreamExecuteCommand.HasAction || this.lifestreamAethernetTeleport.HasFunction;

    public bool IsVnavmeshAvailable => this.vnavIsReady.HasFunction && (this.vnavMoveCloseTo.HasFunction || this.vnavMoveTo.HasFunction);

    public bool CanStopPathing => this.vnavStop.HasAction;

    public string LifestreamStatus => this.IsLifestreamAvailable
        ? "Available: Lifestream.ExecuteCommand(string) and/or Lifestream.AethernetTeleport(string)"
        : "Unavailable: verified Lifestream IPC providers not found.";

    public string VnavmeshStatus => this.IsVnavmeshAvailable
        ? "Available: vnavmesh.SimpleMove.PathfindAndMoveTo/CloseTo"
        : "Unavailable: verified vnavmesh movement IPC providers not found.";

    public string? LastNavigationError { get; private set; }

    public string ContractsUsed =>
        "Lifestream.ExecuteCommand: ICallGateSubscriber<string, object>; " +
        "Lifestream.IsBusy: ICallGateSubscriber<bool>; " +
        "Lifestream.Abort: ICallGateSubscriber<object>; " +
        "Lifestream.AethernetTeleport: ICallGateSubscriber<string, bool>; " +
        "vnavmesh.SimpleMove.PathfindAndMoveTo: ICallGateSubscriber<Vector3, bool, bool>; " +
        "vnavmesh.SimpleMove.PathfindAndMoveCloseTo: ICallGateSubscriber<Vector3, bool, float, bool>; " +
        "vnavmesh.Path.Stop: ICallGateSubscriber<object>; " +
        "vnavmesh.Path.IsRunning: ICallGateSubscriber<bool>; " +
        "vnavmesh.Nav.IsReady: ICallGateSubscriber<bool>.";

    public bool Teleport(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            this.LastNavigationError = "No verified Lifestream command target is attached to this item.";
            return false;
        }

        try
        {
            if (this.lifestreamExecuteCommand.HasAction)
            {
                this.lifestreamExecuteCommand.InvokeAction(command);
                this.LastNavigationError = null;
                return true;
            }

            this.LastNavigationError = "Lifestream.ExecuteCommand provider is unavailable.";
            return false;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Lifestream teleport IPC failed");
            this.LastNavigationError = ex.Message;
            return false;
        }
    }

    public bool PathTo(float x, float y, float z, float tolerance = 1.5f)
    {
        try
        {
            if (this.vnavIsReady.HasFunction && !this.vnavIsReady.InvokeFunc())
            {
                this.LastNavigationError = "vnavmesh reports navigation is not ready.";
                return false;
            }

            var destination = new Vector3(x, y, z);
            if (this.vnavMoveCloseTo.HasFunction)
            {
                var started = this.vnavMoveCloseTo.InvokeFunc(destination, false, tolerance);
                this.LastNavigationError = started ? null : "vnavmesh returned false for PathfindAndMoveCloseTo.";
                return started;
            }

            if (this.vnavMoveTo.HasFunction)
            {
                var started = this.vnavMoveTo.InvokeFunc(destination, false);
                this.LastNavigationError = started ? null : "vnavmesh returned false for PathfindAndMoveTo.";
                return started;
            }

            this.LastNavigationError = "Verified vnavmesh movement provider is unavailable.";
            return false;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "vnavmesh path IPC failed");
            this.LastNavigationError = ex.Message;
            return false;
        }
    }

    public void StopPathing()
    {
        try
        {
            if (this.vnavStop.HasAction)
            {
                this.vnavStop.InvokeAction();
                this.LastNavigationError = null;
            }
            else
            {
                this.LastNavigationError = "vnavmesh.Path.Stop provider is unavailable.";
            }
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "vnavmesh stop IPC failed");
            this.LastNavigationError = ex.Message;
        }
    }
}
