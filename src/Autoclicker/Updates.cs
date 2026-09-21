using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

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
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string path = Path.Combine(local, "Autoclicker", "Autoclicker.settings");
                if (!File.Exists(path))
                {
                    // Migrate only the keyboard binding; never copy an installed application.
                    string[] legacy = {
                        Path.Combine(local, "Syn1x.Autoclicker", "Autoclicker.settings"),
                        Path.Combine(Path.GetDirectoryName(typeof(AppInfo).Assembly.Location), "Autoclicker.settings")
                    };
                    foreach (string candidate in legacy)
                        if (File.Exists(candidate)) { KeySettings.TrySave(path, KeySettings.Load(candidate)); break; }
                }
                return path;
            }
        }
    }

    internal interface IUpdateBackend
    {
        bool Available { get; }
        bool ReadyToApply { get; }
        Task<string> CheckAsync(CancellationToken cancellation);
        Task DownloadAsync(Action<int> progress, CancellationToken cancellation);
        void ApplyOnExit();
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
            Status = ready ? "Update ready / applies on close" : backend.Available
                ? "Updates check automatically on launch" : "Run this EXE from a writable folder";
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
                string version = await backend.CheckAsync(cancellation.Token);
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
                Status = "v" + version + " ready / applies on close";
            }
            catch (OperationCanceledException)
            {
                if (!closed) Status = "Update download cancelled / try again";
            }
            catch (UnauthorizedAccessException)
            {
                Status = "Move this EXE to a writable folder to update";
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
            catch (Exception) { /* The original EXE remains usable; a later check can retry. */ }
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
