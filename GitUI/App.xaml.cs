using System.Linq;
using System.Windows;
using GitUI.Services;

namespace GitUI;

public partial class App : Application
{
    /// <summary>True if the app was launched with --minimized (e.g. by Windows auto-start).</summary>
    public static bool StartMinimized { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartMinimized = e.Args.Any(a =>
            string.Equals(a, AutoStart.MinimizedFlag, System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "/minimized", System.StringComparison.OrdinalIgnoreCase));
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // The OS reclaims FileSystemWatcher handles when the process dies.
        // We deliberately do NOT call WatchManager.StopAll() here — it would persist
        // an empty config and break auto-resume on next launch.
        base.OnExit(e);
    }
}
