namespace SmartInspectConsole.Backend;

public sealed class SmartInspectBackendOptions
{
    public int MaxLogEntries { get; set; } = 20_000;
    public int QueryDefaultLimit { get; set; } = 100;
    public int QueryMaxLimit { get; set; } = 1_000;
}
