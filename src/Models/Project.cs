namespace Prowl.Launcher;

public sealed class Project
{
    public bool Favorite
    {
        get;
        set;
    }

    public string Name
    {
        get;
        set;
    } = "";

    public string Path
    {
        get;
        set;
    } = "";

    public string? EditorKey
    {
        get;
        set;
    }

    public DateTimeOffset LastOpened
    {
        get;
        set;
    }

    /// <summary>Names the project's folder of launcher backups, so they follow the project when it moves.</summary>
    public string? BackupId
    {
        get;
        set;
    }
}
