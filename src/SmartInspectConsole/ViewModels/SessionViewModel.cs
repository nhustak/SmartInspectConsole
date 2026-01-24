namespace SmartInspectConsole.ViewModels;

/// <summary>
/// View model for a session filter.
/// </summary>
public class SessionViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private int _entryCount;

    /// <summary>
    /// Gets or sets the session name.
    /// </summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// Gets or sets the number of log entries in this session.
    /// </summary>
    public int EntryCount
    {
        get => _entryCount;
        set => SetProperty(ref _entryCount, value);
    }

    /// <summary>
    /// Special session representing all sessions.
    /// </summary>
    public static SessionViewModel All { get; } = new() { Name = "(All Sessions)" };

    public override string ToString() => Name;
}
