using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Autoclicker
{
    internal static class IntegrationMain
    {
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--public-update")
                return PortableUpdateProbe.RunPublicUpdate(args[1], args[2]);
            if (args.Length == 2 && args[0] == "--hold")
            {
                File.WriteAllText(args[1], "ready");
                Thread.Sleep(1500);
                return 0;
            }
            try
            {
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string root = Path.GetFullPath(args[0]);
                Directory.CreateDirectory(root);
                TestUpdates().GetAwaiter().GetResult();
                PortableUpdateProbe.Run(root).GetAwaiter().GetResult();
                Check(Verification.Run(Path.Combine(root, "engine.txt")) == 0, "Engine verification failed.");
                foreach (Type probe in new Type[] { typeof(KeyBindingProbe), typeof(CheckboxPaintProbe) })
                {
                    int code = (int)probe.GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,
                        new object[] { new string[] { Path.Combine(root, probe.Name + ".txt") } });
                    Check(code == 0, probe.Name + " failed.");
                }
                CrosshairProbe.Run(root);
                InputCommitProbe.Run(root);
                UpdateUiProbe.Run(root);
                CompactLayoutProbe.Run(root);
                Console.WriteLine("PASS: standalone EXE, real self-update helper, update validation/cancellation, engine, keyboard, timer, settings, close, checkbox repaint, crosshair styles/color/click-through/focus/lifecycle. No real input sent.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static async Task TestUpdates()
        {
            FakeBackend backend = new FakeBackend();
            using (UpdateCoordinator updates = new UpdateCoordinator(backend))
            {
                await updates.CheckAsync();
                Check(backend.Checks == 1 && backend.Downloads == 0 && updates.Status == "Up to date", "No update must avoid a download.");
                await updates.ConfirmAsync();
                Check(backend.Downloads == 0 && !updates.RestartToApply(), "Confirmation/restart must do nothing before an update is found.");
                backend.Version = "1.0.1";
                backend.FailDownload = true;
                await updates.CheckAsync();
                Check(backend.Downloads == 0 && updates.CanConfirm && updates.ActionText == "Confirm update",
                    "Detecting an update must require confirmation before downloading.");
                await updates.CheckAsync();
                Check(backend.Checks == 2, "An update waiting for confirmation must not be replaced by a duplicate check.");
                await updates.ConfirmAsync();
                updates.ApplyOnExit();
                Check(backend.Applies == 0 && updates.CanConfirm && !updates.CanRestart, "Failed downloads must never install and must allow confirmation to retry.");
                backend.FailDownload = false;
                await updates.ConfirmAsync();
                Check(backend.ReadyToApply && updates.CanRestart && updates.ActionText == "Restart to update" && backend.Applies == 0,
                    "A completed download must offer restart without automatically closing the app.");
                updates.Dispose();
                updates.ApplyOnExit();
                updates.ApplyOnExit();
                Check(backend.Applies == 1 && !backend.Restart, "An ordinary close applies a confirmed update once without reopening.");
            }
            backend = new FakeBackend { Version = "1.0.1" };
            using (var updates = new UpdateCoordinator(backend))
            {
                await updates.CheckAsync();
                updates.Dispose();
                await updates.ConfirmAsync();
                updates.ApplyOnExit();
                Check(backend.Downloads == 0 && backend.Applies == 0, "Closing without confirmation must not download or apply anything.");
            }
            backend = new FakeBackend { Version = "1.0.1", CheckGate = new TaskCompletionSource<string>() };
            using (UpdateCoordinator updates = new UpdateCoordinator(backend))
            {
                Task check = updates.CheckAsync();
                await updates.CheckAsync();
                Check(backend.Checks == 1, "Repeated clicks must not run duplicate checks.");
                updates.Dispose();
                backend.CheckGate.SetResult("1.0.1");
                await check;
                updates.ApplyOnExit();
                Check(backend.Downloads == 0 && backend.Applies == 0, "Closing during a check must not start a download.");
            }
            backend = new FakeBackend { Version = "1.0.1", WaitForCancellation = true };
            using (UpdateCoordinator updates = new UpdateCoordinator(backend))
            {
                await updates.CheckAsync();
                Task check = updates.ConfirmAsync();
                await updates.ConfirmAsync();
                Check(backend.Downloads == 1, "Repeated confirmations must not run duplicate downloads.");
                updates.Dispose();
                await check;
                updates.ApplyOnExit();
                Check(backend.Cancelled && backend.Applies == 0, "Closing must cancel an incomplete download.");
            }
            backend = new FakeBackend { ReadyToApply = true, FailApply = true };
            using (UpdateCoordinator updates = new UpdateCoordinator(backend))
            {
                await updates.CheckAsync();
                Check(!updates.RestartToApply() && updates.CanRestart, "Helper launch failure must leave restart available to retry.");
                backend.FailApply = false;
                Check(updates.RestartToApply() && !updates.RestartToApply(), "Restart must queue the helper exactly once.");
                updates.Dispose();
                updates.ApplyOnExit();
                Check(backend.Checks == 0 && backend.Applies == 1 && backend.Restart, "A restart request must reopen after applying and not launch another helper on close.");
            }
            backend = new FakeBackend { FailCheck = true };
            using (UpdateCoordinator updates = new UpdateCoordinator(backend))
            {
                await updates.CheckAsync();
                Check(updates.CanCheck && !updates.Busy, "Offline checks must remain recoverable.");
            }
        }

        internal sealed class FakeBackend : IUpdateBackend
        {
            public bool Available { get { return true; } }
            public bool ReadyToApply { get; set; }
            internal string Version;
            internal int Checks, Downloads, Applies;
            internal bool FailDownload, FailCheck, WaitForCancellation, Cancelled, FailApply, Restart;
            internal TaskCompletionSource<string> CheckGate;
            public Task<string> CheckAsync(CancellationToken cancellation)
            {
                Checks++;
                if (FailCheck) throw new IOException("Offline");
                return CheckGate == null ? Task.FromResult(Version) : CheckGate.Task;
            }
            public async Task DownloadAsync(Action<int> progress, CancellationToken cancellation)
            {
                Downloads++;
                if (FailDownload) throw new IOException("Checksum failed");
                if (WaitForCancellation)
                {
                    try { await Task.Delay(Timeout.Infinite, cancellation); }
                    catch (OperationCanceledException) { Cancelled = true; throw; }
                }
                cancellation.ThrowIfCancellationRequested();
                ReadyToApply = true;
            }
            public void ApplyOnExit(bool restart = false)
            {
                if (FailApply) throw new IOException("Helper unavailable");
                Applies++;
                Restart = restart;
            }
        }
    }
}
