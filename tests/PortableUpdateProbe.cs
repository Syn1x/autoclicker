using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Autoclicker
{
    internal static class PortableUpdateProbe
    {
        internal static int RunPublicUpdate(string target, string report)
        {
            try
            {
                Version current = new Version(AssemblyName.GetAssemblyName(target).Version.ToString(3));
                using (var backend = new PortableUpdateBackend(target, current, new HttpClient(),
                    Path.Combine(Path.GetDirectoryName(report), "public-update-jobs")))
                {
                    string next = backend.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();
                    Check(next != null, "No newer public standalone release was found.");
                    backend.DownloadAsync(value => { }, CancellationToken.None).GetAwaiter().GetResult();
                    Check(backend.ReadyToApply, "Public release was not verified.");
                    backend.ApplyOnExit();
                    File.WriteAllText(report, "Verified public release " + next + "; helper will replace the test EXE after this process exits.");
                }
                return 0;
            }
            catch (Exception error) { File.WriteAllText(report, "FAIL: " + error); return 1; }
        }

        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        internal static async Task Run(string reports)
        {
            string root = Path.Combine(reports, "portable-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string executable = typeof(AppInfo).Assembly.Location;
            byte[] payload = File.ReadAllBytes(executable);
            var release = new UpdateManifest { Version = AppInfo.Version, Size = payload.Length, Sha256 = UpdateFiles.Hash(executable) };
            byte[] manifest;
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(UpdateManifest)).WriteObject(stream, release);
                manifest = stream.ToArray();
            }
            string target = Path.Combine(root, "autoclicker.exe");
            File.Copy(executable, target);
            string jobs = Path.Combine(root, "jobs");
            var source = new Source(manifest, payload);
            using (var backend = new PortableUpdateBackend(target, new Version(AppInfo.Version), new HttpClient(source), jobs))
                Check(await backend.CheckAsync(CancellationToken.None) == null && source.Downloads == 0, "Current version should not download.");
            source = new Source(manifest, payload);
            using (var backend = new PortableUpdateBackend(target, new Version(0, 0, 1), new HttpClient(source), jobs))
            {
                Check(await backend.CheckAsync(CancellationToken.None) == AppInfo.Version, "New release was not found.");
                int progress = 0;
                await backend.DownloadAsync(value => progress = value, CancellationToken.None);
                Check(backend.ReadyToApply && progress == 100, "Valid standalone download should be ready.");
            }
            Check(Directory.GetDirectories(jobs).Length == 0, "Unapplied download must be cleaned up.");

            byte[] corrupt = (byte[])payload.Clone();
            corrupt[corrupt.Length - 1] ^= 1;
            source = new Source(manifest, corrupt);
            using (var backend = new PortableUpdateBackend(target, new Version(0, 0, 1), new HttpClient(source), jobs))
            {
                await backend.CheckAsync(CancellationToken.None);
                await MustFail(() => backend.DownloadAsync(value => { }, CancellationToken.None), "Bad checksum was accepted.");
                Check(!backend.ReadyToApply, "Corrupt update became ready.");
            }
            source = new Source(manifest, new byte[10]);
            using (var backend = new PortableUpdateBackend(target, new Version(0, 0, 1), new HttpClient(source), jobs))
            {
                await backend.CheckAsync(CancellationToken.None);
                await MustFail(() => backend.DownloadAsync(value => { }, CancellationToken.None), "Truncated update was accepted.");
            }
            source = new Source(manifest, payload) { BlockDownload = true };
            using (var backend = new PortableUpdateBackend(target, new Version(0, 0, 1), new HttpClient(source), jobs))
            using (var cancel = new CancellationTokenSource())
            {
                await backend.CheckAsync(cancel.Token);
                Task download = backend.DownloadAsync(value => { }, cancel.Token);
                cancel.Cancel();
                await MustFail(() => download, "Cancelled download succeeded.");
                Check(!backend.ReadyToApply, "Cancelled update became ready.");
            }
            Check(Directory.GetDirectories(jobs).Length == 0, "Failed downloads left pending files.");
            source = new Source(System.Text.Encoding.UTF8.GetBytes("{\"version\":\"../bad\",\"sha256\":\"abc\",\"size\":1}"), payload);
            using (var backend = new PortableUpdateBackend(target, new Version(0, 0, 1), new HttpClient(source), jobs))
                await MustFail(() => backend.CheckAsync(CancellationToken.None), "Invalid manifest was accepted.");

            string job = Path.Combine(jobs, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(job);
            File.WriteAllBytes(Path.Combine(job, "update.exe"), payload);
            var plan = new UpdatePlan { Target = target, OriginalHash = UpdateFiles.Hash(target), Release = release, ParentId = Int32.MaxValue };
            File.AppendAllText(target, "changed by user");
            string changedHash = UpdateFiles.Hash(target);
            bool rejected = false;
            try { PortableUpdater.Apply(plan, job); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && UpdateFiles.Hash(target) == changedHash, "Updater overwrote a changed target.");
            plan.OriginalHash = changedHash;
            File.WriteAllBytes(Path.Combine(job, "update.exe"), corrupt);
            rejected = false;
            try { PortableUpdater.Apply(plan, job); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && UpdateFiles.Hash(target) == changedHash, "Helper accepted a corrupt staged update.");
            File.WriteAllBytes(Path.Combine(job, "update.exe"), payload);
            using (FileStream locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                rejected = false;
                try { PortableUpdater.Apply(plan, job); } catch (IOException) { rejected = true; }
                Check(rejected, "Locked target should not be replaced.");
            }
            Check(UpdateFiles.Hash(target) == changedHash, "Locked target was damaged.");

            // Exercise the real helper process with only one EXE in the app folder.
            string appFolder = Path.Combine(root, "folder with spaces & apostrophe's");
            Directory.CreateDirectory(appFolder);
            string portable = Path.Combine(appFolder, "my autoclicker.exe");
            File.WriteAllBytes(portable, payload);
            File.AppendAllText(portable, "old version trailer");
            plan.Target = portable;
            plan.OriginalHash = UpdateFiles.Hash(portable);
            string helper = Path.Combine(job, "updater.exe");
            File.Copy(executable, helper);
            string planFile = Path.Combine(job, "plan.json");
            string ready = Path.Combine(root, "host-ready.txt");
            using (Process host = Start(typeof(PortableUpdateProbe).Assembly.Location, "--hold \"" + ready + "\""))
            {
                Stopwatch wait = Stopwatch.StartNew();
                while (!File.Exists(ready) && wait.ElapsedMilliseconds < 5000) Thread.Sleep(20);
                Check(File.Exists(ready), "Parent process did not start.");
                plan.ParentId = host.Id;
                plan.ParentStart = host.StartTime.ToUniversalTime().Ticks;
                UpdateFiles.WriteJson(planFile, plan);
                using (Process update = Start(helper, "--apply-update \"" + planFile + "\""))
                {
                    Thread.Sleep(100);
                    Check(UpdateFiles.Hash(portable) == plan.OriginalHash, "Helper replaced the EXE before its parent exited.");
                    Check(host.WaitForExit(5000), "Parent test process did not exit.");
                    Check(update.WaitForExit(10000) && update.ExitCode == 0, "Standalone update helper failed.");
                }
            }
            Check(UpdateFiles.Hash(portable) == release.Sha256, "Self-update did not replace the original file.");
            Check(Directory.GetFiles(appFolder).Length == 1, "Self-update left extra files beside the EXE.");
            string preview = Path.Combine(root, "standalone-preview.png");
            using (Process app = Start(portable, "--preview \"" + preview + "\""))
                Check(app.WaitForExit(10000) && app.ExitCode == 0 && File.Exists(preview), "Standalone EXE required missing dependencies.");
            foreach (AssemblyName dependency in typeof(AppInfo).Assembly.GetReferencedAssemblies())
                Check(dependency.Name == "mscorlib" || dependency.Name.StartsWith("System"), "Non-framework runtime dependency: " + dependency.Name);
            using (var backend = new PortableUpdateBackend(portable, new Version(AppInfo.Version), new HttpClient(new Source(manifest, payload)), jobs)) { }
            Check(!Directory.Exists(job), "Next launch did not clean the exited helper.");
            File.WriteAllText(Path.Combine(reports, "PortableUpdateProbe.txt"),
                "PASS: single EXE launch, bounded manifest, version selection, SHA-256, truncated/corrupt/cancelled downloads, changed/locked target protection, real helper waits for parent, atomic replacement, custom filename and special paths, helper exit and cleanup. No real input sent.");
        }

        private static Process Start(string executable, string arguments)
        {
            return Process.Start(new ProcessStartInfo(executable, arguments) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        private static async Task MustFail(Func<Task> action, string message)
        {
            try { await action(); }
            catch (Exception) { return; }
            throw new Exception(message);
        }

        private sealed class Source : HttpMessageHandler
        {
            private readonly byte[] manifest, payload;
            internal int Downloads;
            internal bool BlockDownload;
            internal Source(byte[] json, byte[] exe) { manifest = json; payload = exe; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                if (request.RequestUri.AbsoluteUri == PortableUpdateBackend.ManifestUrl)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(manifest) };
                Check(request.RequestUri.AbsoluteUri == AppInfo.RepositoryUrl + "/releases/download/v" + AppInfo.Version + "/autoclicker.exe", "Unexpected update URL.");
                Downloads++;
                if (BlockDownload) await Task.Delay(Timeout.Infinite, cancellation);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            }
        }
    }
}
