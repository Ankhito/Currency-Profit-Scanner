using Dalamud.Configuration;
using Dalamud.Plugin;

namespace CurrencyProfitScanner;

[Serializable]
public sealed class PluginConfiguration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public int Version { get; set; } = 1;

    public string PreferredWorldOrDc { get; set; } = string.Empty;

    public int CacheTtlMinutes { get; set; } = 10;

    public int MinimumSales24h { get; set; } = 3;

    public bool HideStaleData { get; set; }

    public bool HideNoMovementItems { get; set; } = true;

    public float TaxBufferMultiplier { get; set; } = 0.95f;

    public int StaleDataThresholdMinutes { get; set; } = 120;

    public string CurrencyFilter { get; set; } = string.Empty;

    public void Initialize(IDalamudPluginInterface pluginInterface) => this.pluginInterface = pluginInterface;

    public void Save() => this.pluginInterface?.SavePluginConfig(this);
}
