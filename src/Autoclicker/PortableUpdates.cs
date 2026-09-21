using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Autoclicker
{
    [DataContract]
    internal sealed class UpdateManifest
    {
        [DataMember(Name = "version")] internal string Version;
        [DataMember(Name = "sha256")] internal string Sha256;
        [DataMember(Name = "size")] internal long Size;

        internal System.Version Validate()
        {
            System.Version version;
            if (Version == null || !Regex.IsMatch(Version, @"\A[0-9]+\.[0-9]+\.[0-9]+\z") ||
                !System.Version.TryParse(Version, out version) || Sha256 == null ||
                !Regex.IsMatch(Sha256, @"\A[0-9a-fA-F]{64}\z") || Size <= 0 || Size > 20 * 1024 * 1024)
                throw new InvalidDataException("Invalid update manifest.");
            return version;
        }
    }

    [DataContract]
    internal sealed class UpdatePlan
    {
        [DataMember] internal string Target;
        [DataMember] internal string OriginalHash;
        [DataMember] internal UpdateManifest Release;
        [DataMember] internal int ParentId;
        [DataMember] internal long ParentStart;
    }

    internal static class UpdateFiles
    {
        internal static T ReadJson<T>(Stream stream)
        {
            return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        internal static void WriteJson<T>(string path, T value)
        {
            using (FileStream file = File.Create(path))
                new DataContractJsonSerializer(typeof(T)).WriteObject(file, value);
        }

        internal static string Hash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream file = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }

        internal static void ValidateExecutable(string path, UpdateManifest release)
        {
            release.Validate();
            if (new FileInfo(path).Length != release.Size ||
                !String.Equals(Hash(path), release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update download did not match its checksum.");
            AssemblyName assembly = AssemblyName.GetAssemblyName(path);
            if (assembly.Name != "Autoclicker" || assembly.Version.ToString(3) != release.Version)
                throw new InvalidDataException("The update is not the expected Autoclicker version.");
        }

        internal static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static void DeleteJob(string root, string directory)
        {
            // Never recursively remove a location outside this updater's own job root.
            string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            string job = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            Guid id;
            if (!String.Equals(Path.GetDirectoryName(job), parent, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(job), "N", out id)) return;
            try
            {
                if (Directory.Exists(job) && (File.GetAttributes(job) & FileAttributes.ReparsePoint) == 0)
                {
                    // Jobs contain only our own files; don't descend into unexpected subdirectories.
                    if (Directory.GetDirectories(job).Length != 0) return;
                    foreach (string file in Directory.GetFiles(job)) File.Delete(file);
                    Directory.Delete(job);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal sealed class PortableUpdateBackend : IUpdateBackend, IDisposable
    {
        internal const string ManifestUrl = AppInfo.RepositoryUrl + "/releases/latest/download/autoclicker-update.json";
        private readonly string executable;
        private readonly string temporaryRoot;
        private readonly Version currentVersion;
        private readonly HttpClient client;
        private UpdateManifest release;
        private UpdatePlan plan;
        private string job;
        private bool launched;
        public bool Available { get { return File.Exists(executable); } }
        public bool ReadyToApply { get { return plan != null; } }

        internal PortableUpdateBackend() : this(typeof(AppInfo).Assembly.Location,
            new Version(AppInfo.Version), new HttpClient(), Path.Combine(Path.GetTempPath(), "Autoclicker-updates")) { }

        internal PortableUpdateBackend(string executablePath, Version version, HttpClient httpClient, string tempRoot)
        {
            executable = Path.GetFullPath(executablePath);
            currentVersion = version;
            temporaryRoot = Path.GetFullPath(tempRoot);
            client = httpClient;
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Autoclicker/" + AppInfo.Version);
            CleanupCompletedJobs();
        }

        private void CleanupCompletedJobs()
        {
            try
            {
                if (!Directory.Exists(temporaryRoot)) return;
                foreach (string directory in Directory.GetDirectories(temporaryRoot))
                {
                    // The helper cannot delete its own running EXE. The next launch cleans it up.
                    if (File.Exists(Path.Combine(directory, "completed.txt")) ||
                        Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-1))
                        UpdateFiles.DeleteJob(temporaryRoot, directory);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public async Task<string> CheckAsync(CancellationToken cancellation)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            cancellation = deadline.Token;
            release = null;
            using (HttpResponseMessage response = await client.GetAsync(ManifestUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (MemoryStream json = new MemoryStream())
                {
                    byte[] buffer = new byte[4096];
                    int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) != 0)
                    {
                        if (json.Length + count > 16384) throw new InvalidDataException("Update manifest is too large.");
                        json.Write(buffer, 0, count);
                    }
                    json.Position = 0;
                    UpdateManifest candidate = UpdateFiles.ReadJson<UpdateManifest>(json);
                    Version next = candidate.Validate();
                    if (next <= currentVersion) return null;
                    release = candidate;
                    return release.Version;
                }
            }
        }

        public async Task DownloadAsync(Action<int> progress, CancellationToken cancellation)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            cancellation = deadline.Token;
            if (release == null) throw new InvalidOperationException("Check for an update first.");
            if (plan != null) return;
            // Detect a read-only download location before preparing an update we cannot apply.
            string probe = Path.Combine(Path.GetDirectoryName(executable), ".autoclicker-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (FileStream writable = new FileStream(probe, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            job = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(job);
            string download = Path.Combine(job, "update.exe");
            try
            {
                // Only our repository's version-specific EXE can be downloaded; the manifest cannot supply a URL.
                string url = AppInfo.RepositoryUrl + "/releases/download/v" + release.Version + "/autoclicker.exe";
                using (HttpResponseMessage response = await client.GetAsync(url,
                    HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength != release.Size)
                        throw new InvalidDataException("Unexpected update length.");
                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream output = new FileStream(download, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        32768, FileOptions.Asynchronous))
                    {
                        byte[] buffer = new byte[32768];
                        long received = 0;
                        int count;
                        while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) != 0)
                        {
                            received += count;
                            if (received > release.Size) throw new InvalidDataException("Update exceeded its expected size.");
                            await output.WriteAsync(buffer, 0, count, cancellation).ConfigureAwait(false);
                            progress((int)(received * 100 / release.Size));
                        }
                    }
                }
                cancellation.ThrowIfCancellationRequested();
                UpdateFiles.ValidateExecutable(download, release);
                using (Process parent = Process.GetCurrentProcess())
                    plan = new UpdatePlan { Target = executable, OriginalHash = UpdateFiles.Hash(executable),
                        Release = release, ParentId = parent.Id, ParentStart = parent.StartTime.ToUniversalTime().Ticks };
            }
            catch
            {
                UpdateFiles.DeleteJob(temporaryRoot, job);
                job = null;
                throw;
            }
        }

        public void ApplyOnExit()
        {
            if (plan == null || launched) return;
            string helper = Path.Combine(job, "updater.exe");
            string planPath = Path.Combine(job, "plan.json");
            UpdateFiles.WriteJson(planPath, plan);
            File.Copy(executable, helper, true);
            // A temporary copy of this same standalone EXE does only the file replacement.
            using (Process process = Process.Start(new ProcessStartInfo(helper,
                "--apply-update \"" + planPath + "\"") {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                }))
            {
                if (process == null) throw new IOException("Could not start the update helper.");
                launched = true;
            }
        }

        public void Dispose()
        {
            client.Dispose();
            if (!launched && job != null) UpdateFiles.DeleteJob(temporaryRoot, job);
        }
    }

    internal static class PortableUpdater
    {
        internal static int RunHelper(string planPath)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(planPath));
            try
            {
                if (!String.Equals(directory, Path.GetDirectoryName(typeof(AppInfo).Assembly.Location), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update plan must be beside the temporary helper.");
                UpdatePlan plan;
                using (FileStream input = File.OpenRead(planPath)) plan = UpdateFiles.ReadJson<UpdatePlan>(input);
                ValidatePlan(plan, directory);
                try
                {
                    using (Process parent = Process.GetProcessById(plan.ParentId))
                        if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStart && !parent.WaitForExit(15000))
                            throw new IOException("Autoclicker is still running. The original EXE was kept.");
                }
                catch (ArgumentException) { } // The original process has already exited.
                Apply(plan, directory);
                File.WriteAllText(Path.Combine(directory, "completed.txt"), "Updated " + plan.Release.Version);
                return 0;
            }
            catch (Exception error)
            {
                try { File.WriteAllText(Path.Combine(directory, "completed.txt"), "Update failed: " + error.Message); }
                catch (Exception) { }
                return 1;
            }
        }

        private static void ValidatePlan(UpdatePlan plan, string directory)
        {
            if (plan == null || plan.Release == null || plan.Target == null || plan.OriginalHash == null ||
                !Regex.IsMatch(plan.OriginalHash, @"\A[0-9a-fA-F]{64}\z") || plan.ParentId <= 0 ||
                !Path.IsPathRooted(plan.Target) || !plan.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(Path.GetDirectoryName(Path.GetFullPath(plan.Target)), Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid update plan.");
            plan.Release.Validate();
        }

        internal static void Apply(UpdatePlan plan, string directory)
        {
            ValidatePlan(plan, directory);
            string download = Path.Combine(directory, "update.exe");
            UpdateFiles.ValidateExecutable(download, plan.Release);
            string target = Path.GetFullPath(plan.Target);
            string suffix = Guid.NewGuid().ToString("N");
            string incoming = Path.Combine(Path.GetDirectoryName(target), ".autoclicker-" + suffix + ".new");
            string backup = Path.Combine(Path.GetDirectoryName(target), ".autoclicker-" + suffix + ".old");
            try
            {
                File.Copy(download, incoming);
                UpdateFiles.ValidateExecutable(incoming, plan.Release);
                for (int attempt = 0; ; attempt++)
                {
                    // Refuse to overwrite a file that the user or another updater has changed.
                    if (!String.Equals(UpdateFiles.Hash(target), plan.OriginalHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The original EXE changed; the update was not applied.");
                    try
                    {
                        // The verified replacement is on the same volume. Failure keeps the original intact.
                        File.Replace(incoming, target, backup, true);
                        break;
                    }
                    catch (IOException) { if (attempt >= 39) throw; Thread.Sleep(250); }
                }
                UpdateFiles.TryDelete(backup);
            }
            finally { UpdateFiles.TryDelete(incoming); }
        }
    }
}
