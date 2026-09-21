using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Autoclicker
{
    internal static class AppInfo
    {
        internal const string RepositoryUrl = "https://github.com/Syn1x/autoclicker";
        internal static string Version { get { return typeof(AppInfo).Assembly.GetName().Version.ToString(3); } }
        internal static string SettingsPath
        {
            get
            {
                string root = VelopackLocator.Current.RootAppDir;
                if (String.IsNullOrEmpty(root))
                    root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Syn1x.Autoclicker");
                return Path.Combine(root, "Autoclicker.settings");
            }
        }
    }

    internal interface IUpdateBackend
    {
        bool Available { get; }
        bool ReadyToApply { get; }
        Task<string> CheckAsync();
        Task DownloadAsync(Action<int> progress, CancellationToken cancellation);
        void ApplyOnExit();
    }

    internal sealed class VelopackBackend : IUpdateBackend
    {
        private readonly UpdateManager manager;
        private UpdateInfo pending;
        internal VelopackBackend()
        {
            manager = new UpdateManager(new GithubSource(AppInfo.RepositoryUrl, null, false));
        }
        public bool Available { get { return manager.IsInstalled; } }
        public bool ReadyToApply { get { return Available && manager.UpdatePendingRestart != null; } }
        public async Task<string> CheckAsync()
        {
            pending = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            return pending == null ? null : pending.TargetFullRelease.Version.ToString();
        }
        public Task DownloadAsync(Action<int> progress, CancellationToken cancellation)
        {
            if (pending == null) throw new InvalidOperationException("Check for an update before downloading.");
            return manager.DownloadUpdatesAsync(pending, progress, cancellation);
        }
        public void ApplyOnExit()
        {
            // Called only after the form, mouse timer, sound and keyboard hook
            // have all closed. The helper exits when installation is complete.
            manager.WaitExitThenApplyUpdates(null, silent: true, restart: false);
        }
    }

    internal sealed class UpdateCoordinator : IDisposable
    {
        private readonly IUpdateBackend backend;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool closed;
        private bool ready;
        private bool applyQueued;
        internal event Action Changed;
        internal bool Busy { get; private set; }
        internal bool CanCheck { get { return !closed && backend.Available && !Busy && !ready; } }
        internal string Status { get; private set; }

        internal UpdateCoordinator(IUpdateBackend updateBackend)
        {
            backend = updateBackend;
            ready = backend.ReadyToApply;
            Status = ready ? "Update ready / installs on close" : backend.Available
                ? "Updates check automatically on launch" : "Install a release to enable updates";
        }

        private void Notify()
        {
            Action handler = Changed;
            if (!closed && handler != null) handler();
        }

        internal async Task CheckAsync()
        {
            if (!CanCheck) return;
            Busy = true;
            Status = "Checking for updates...";
            Notify();
            try
            {
                string version = await backend.CheckAsync();
                if (closed) return;
                if (version == null) { Status = "Up to date"; return; }
                Status = "Downloading v" + version + "...";
                Notify();
                var progress = new Progress<int>(value =>
                {
                    if (!closed && Busy && !ready)
                    {
                        Status = "Downloading v" + version + " / " + Math.Max(0, Math.Min(100, value)) + "%";
                        Notify();
                    }
                });
                await backend.DownloadAsync(value => ((IProgress<int>)progress).Report(value), cancellation.Token);
                if (closed) return;
                ready = backend.ReadyToApply;
                if (!ready) throw new InvalidOperationException("The downloaded update was not prepared.");
                Status = "v" + version + " ready / installs on close";
            }
            catch (OperationCanceledException)
            {
                if (!closed) Status = "Update download cancelled / try again";
            }
            catch (Exception)
            {
                Status = "Updates unavailable / try again later";
            }
            finally
            {
                Busy = false;
                Notify();
            }
        }

        internal void ApplyOnExit()
        {
            if (applyQueued || !ready) return;
            try { backend.ApplyOnExit(); applyQueued = true; }
            catch (Exception) { /* Keep the verified package for the next launch. */ }
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            cancellation.Cancel();
            Changed = null;
            // Downloads observe this token asynchronously; do not dispose its
            // source until that operation has finished using it.
        }
    }
}
