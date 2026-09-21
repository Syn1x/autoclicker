using System;
using System.IO;
using System.Reflection;
using Velopack;
using Velopack.Locators;

namespace Autoclicker
{
    internal static class PackageUpdateProbe
    {
        internal static int Run(string root, string feed, string report)
        {
            try
            {
                string current = Path.Combine(root, "current");
                string appExe = Path.Combine(current, "Autoclicker.exe");
                string version = AssemblyName.GetAssemblyName(appExe).Version.ToString(3);
                var locator = new TestVelopackLocator("Syn1x.Autoclicker", version,
                    Path.Combine(root, "packages"), current, root, Path.Combine(root, "Update.exe"),
                    channel: "win", processPath: appExe);
                var manager = new UpdateManager(feed, locator: locator);
                var update = manager.CheckForUpdates();
                if (update == null) throw new Exception("No newer package was found.");
                manager.DownloadUpdates(update);
                if (manager.UpdatePendingRestart == null) throw new Exception("The package was not staged.");
                if (!KeySettings.TrySave(Path.Combine(root, "Autoclicker.settings"), 120))
                    throw new Exception("Test setting could not be saved.");
                manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
                File.WriteAllText(report, "PASS: checked and verified package " + update.TargetFullRelease.Version +
                    "; queued real update helper after test process exit. Verify installed version and settings separately.");
                return 0;
            }
            catch (Exception error) { File.WriteAllText(report, "FAIL: " + error); return 1; }
        }
    }
}
