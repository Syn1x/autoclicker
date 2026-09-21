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
                backend.Version = "1.0.1";
                backend.FailDownload = true;
                await updates.CheckAsync();
                updates.ApplyOnExit();
                Check(backend.Applies == 0 && updates.CanCheck, "Failed downloads must never install and must allow retry.");
                backend.FailDownload = false;
                await updates.CheckAsync();
                Check(backend.ReadyToApply && !updates.CanCheck && backend.Applies == 0,
                    "A completed download must wait for app shutdown.");
                updates.Dispose();
                updates.ApplyOnExit();
                updates.ApplyOnExit();
                Check(backend.Applies == 1, "Closing must apply a prepared update exactly once.");
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
                Task check = updates.CheckAsync();
                updates.Dispose();
                await check;
                updates.ApplyOnExit();
                Check(backend.Cancelled && backend.Applies == 0, "Closing must cancel an incomplete download.");
            }
            backend = new FakeBackend { ReadyToApply = true };
            using (UpdateCoordinator updates = new UpdateCoordinator(backend))
            {
                await updates.CheckAsync();
                updates.Dispose();
                updates.ApplyOnExit();
                Check(backend.Checks == 0 && backend.Applies == 1, "Previously downloaded updates must survive a restart.");
            }
            backend = new FakeBackend { FailCheck = true };
            using (UpdateCoordinator updates = new UpdateCoordinator(backend))
            {
                await updates.CheckAsync();
                Check(updates.CanCheck && !updates.Busy, "Offline checks must remain recoverable.");
            }
        }

        private sealed class FakeBackend : IUpdateBackend
        {
            public bool Available { get { return true; } }
            public bool ReadyToApply { get; set; }
            internal string Version;
            internal int Checks, Downloads, Applies;
            internal bool FailDownload, FailCheck, WaitForCancellation, Cancelled;
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
            public void ApplyOnExit() { Applies++; }
        }
    }
}
