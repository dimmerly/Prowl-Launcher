namespace Prowl.Launcher;

public static class Constants
{
    public static class Defaults
    {
        public const bool CloseOnEditorLaunch = true;
        public const bool LauncherPrereleases = true;
        public const bool EditorPrereleases = true;
        public const bool LauncherAutoUpdate = true;
        public const bool ShowFps = false;
        public const float UiScale = 1;
        public const float MinUiScale = 0.5f;
        public const float MaxUiScale = 1.5f;
        public const string Locale = "en";
        public const string ProwlRepository = "ProwlEngine/Prowl";
        public const string LauncherRepository = "dimmerly/Prowl-Launcher";
    }

    public static class Network
    {
        public const string GitHubUrl = "https://github.com/";
        public const string GitHubApiUrl = "https://api.github.com/";
        public const string UserAgent = "Prowl-Launcher/1.0";
        public const int ReleasesPerPage = 100;
        public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan ReleaseCheckTimeout = TimeSpan.FromSeconds(15);
        public const int TransferBufferSize = 81920;
        public const int ProgressIntervalMilliseconds = 100;
    }

    public static class Storage
    {
        public const Environment.SpecialFolder HomeBaseFolder = Environment.SpecialFolder.ApplicationData;
        public const string ApplicationFolderName = "Prowl";
        public const string LauncherFolderName = "Launcher";
        public const string HomeEnvironment = "PROWL_LAUNCHER_HOME";
        public const string LauncherManifest = "launcher-installation.json";
        public const long MaxExpandedArchiveBytes = 8L * 1024 * 1024 * 1024;
        public const int SettingsLockRetryMilliseconds = 20;
        public const int SampleLockRetryMilliseconds = 100;
        public static readonly TimeSpan SettingsLockTimeout = TimeSpan.FromSeconds(5);
        public static IReadOnlyList<string> SupportedPlatforms
        {
            get;
        } = Array.AsReadOnly<string>(
            ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"]);
    }

    public static class Startup
    {
        public const string ReadyEnvironment = "PROWL_LAUNCHER_READY_PIPE";
        public const string VersionPattern = @"\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?(?:\+[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?";
        public static readonly TimeSpan ReadyPipeTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan LauncherTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan StabilityDelay = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan SdkCheckTimeout = TimeSpan.FromSeconds(10);
        public const int SampleStartupMilliseconds = 1500;
    }

    public static class Layout
    {
        public const string WindowTitle = "Prowl Launcher";
        public const int WindowWidth = 1200;
        public const int WindowHeight = 840;
        public const float SidebarWidth = 72;
        public const float SidebarPadding = 8;
        public const float TitleBarHeight = 34;
        public const float LanguagePickerWidth = 86;
        public const float FpsWidth = 84;
        public const float OperationHeight = 148;
        public const float SampleGap = 12;
        public const int ProgressDelayMilliseconds = 650;
    }

    internal static class Rendering
    {
        internal const int BlurBaseShift = 2;
        internal const int MaxBlurLevels = 6;
    }

    internal static class WindowsMessages
    {
        internal const uint NcCalcSize = 0x0083;
        internal const uint NcHitTest = 0x0084;
        internal const uint GetMinMaxInfo = 0x0024;
        internal const uint EnterSizeMove = 0x0231;
        internal const uint ExitSizeMove = 0x0232;
        internal const nuint SubclassId = 1;
    }
}
