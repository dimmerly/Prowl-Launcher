using System.Text.Json.Serialization;

namespace Prowl.Launcher;

[JsonConverter(typeof(JsonStringEnumConverter<BackupReason>))]
public enum BackupReason
{
    Manual,
    PreUpgrade
}

/// <summary>
///     The manifest stored next to a backup's zip. It is written last, so a backup without one is incomplete.
/// </summary>
public sealed class ProjectBackup
{
    public string Id
    {
        get;
        set;
    } = "";

    public string ProjectName
    {
        get;
        set;
    } = "";

    public string ProjectPath
    {
        get;
        set;
    } = "";

    public DateTimeOffset CreatedAt
    {
        get;
        set;
    }

    public BackupReason Reason
    {
        get;
        set;
    }

    /// <summary>The engine version the project was saved with when it was backed up.</summary>
    public string? ProjectVersion
    {
        get;
        set;
    }

    /// <summary>For pre-upgrade backups, the version the project was about to be upgraded to.</summary>
    public string? TargetVersion
    {
        get;
        set;
    }

    public int FileCount
    {
        get;
        set;
    }

    public long UncompressedBytes
    {
        get;
        set;
    }

    public long ArchiveBytes
    {
        get;
        set;
    }

    /// <summary>Where the backup's zip is, filled in when backups are listed.</summary>
    [JsonIgnore]
    public string Archive
    {
        get;
        set;
    } = "";

    /// <summary>Automatic backups are pruned to the retention setting; manual ones are kept until deleted.</summary>
    [JsonIgnore]
    public bool Automatic => Reason != BackupReason.Manual;
}
