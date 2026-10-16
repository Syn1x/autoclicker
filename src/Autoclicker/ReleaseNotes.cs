using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Autoclicker
{
    internal static class ReleaseNotes
    {
        internal static string ReadCurrent()
        {
            using (Stream stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("Autoclicker.ReleaseNotes.md"))
            {
                if (stream == null) return "Release notes are not included in this build.";
                using (var reader = new StreamReader(stream))
                    return ToPlainText(reader.ReadToEnd());
            }
        }

        internal static string ToPlainText(string markdown)
        {
            string text = (markdown ?? "").Replace("**", "").Replace("`", "");
            text = Regex.Replace(text, @"(?m)^- ", "• ");
            text = Regex.Replace(text, @"(?m)^#{1,6} ", "");
            return text.Trim().Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        }

        private static Version ReadSeenVersion(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 128) return null;
            Version seen;
            return Version.TryParse(File.ReadAllText(path).Trim(), out seen) ? seen : null;
        }

        internal static bool ShouldShow(string settingsPath, string version)
        {
            Version current;
            if (String.IsNullOrEmpty(settingsPath) || !Version.TryParse(version, out current)) return false;
            try
            {
                Version seen = ReadSeenVersion(settingsPath + ".release-seen");
                return seen == null || seen < current;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static bool MarkSeen(string settingsPath, string version)
        {
            Version current;
            if (String.IsNullOrEmpty(settingsPath) || !Version.TryParse(version, out current)) return false;
            string path = settingsPath + ".release-seen";
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Version seen = ReadSeenVersion(path);
                if (seen != null && seen >= current) return true;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(temporary, version);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally { UpdateFiles.TryDelete(temporary); }
        }
    }
}
