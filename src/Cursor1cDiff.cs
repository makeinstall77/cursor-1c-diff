using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Cursor1cDiff
{
    /// <summary>
    /// Silent launcher for 1C Configurator external compare/merge via Cursor.
    /// - No console window (winexe)
    /// - Copies 1C .txt dumps to .bsl so BSL language mode / LS work
    /// - For 2-file compare: reorder by 1C dump names (main config = right, storage version = left)
    /// </summary>
    internal static class Program
    {
        // (Основная конфигурация) / (Конфигурация хранилища 1050)
        private static readonly Regex StorageVersionRx = new Regex(
            @"Конфигурация\s+хранилища\s+(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private const string MainConfigMarker = "Основная конфигурация";

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args == null || args.Length < 2 || args.Length > 4)
                {
                    Fatal("Ожидается 2, 3 или 4 пути к файлам из параметров 1С.");
                    return 1;
                }

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string cursorExe = Path.Combine(localAppData, @"Programs\cursor\Cursor.exe");
                string cliJs = Path.Combine(localAppData, @"Programs\cursor\resources\app\out\cli.js");

                if (!File.Exists(cursorExe) || !File.Exists(cliJs))
                {
                    Fatal("Не найден Cursor CLI:\n" + cursorExe + "\n" + cliJs);
                    return 1;
                }

                string tmpDir = Path.Combine(Path.GetTempPath(), "1c-cursor-diff");
                Directory.CreateDirectory(tmpDir);

                if (args.Length == 2)
                {
                    string leftSrc, rightSrc;
                    OrderByOneCName(args[0], args[1], out leftSrc, out rightSrc);
                    string left = CopyAsBsl(leftSrc, tmpDir, "left");
                    string right = CopyAsBsl(rightSrc, tmpDir, "right");
                    return RunCursor(cursorExe, cliJs,
                        "--new-window", "--wait", "--diff", left, right);
                }

                if (args.Length == 3)
                {
                    // base, second, merged
                    string baseFile = CopyAsBsl(args[0], tmpDir, "base");
                    string second = CopyAsBsl(args[1], tmpDir, "second");
                    string mergedOut = TempBsl(tmpDir, "merged");
                    File.Copy(args[0], mergedOut, true);

                    int rc = RunCursor(cursorExe, cliJs,
                        "--new-window", "--wait", "--merge",
                        baseFile, second, baseFile, mergedOut);

                    if (File.Exists(mergedOut))
                        File.Copy(mergedOut, args[2], true);
                    return rc;
                }

                // oldVendor, base, second, merged
                string oldVendor = CopyAsBsl(args[0], tmpDir, "old");
                string baseCfg = CopyAsBsl(args[1], tmpDir, "base");
                string secondCfg = CopyAsBsl(args[2], tmpDir, "second");
                string merged = TempBsl(tmpDir, "merged");
                if (File.Exists(args[3]))
                    File.Copy(args[3], merged, true);
                else
                    File.Copy(args[1], merged, true);

                int code = RunCursor(cursorExe, cliJs,
                    "--new-window", "--wait", "--merge",
                    baseCfg, secondCfg, oldVendor, merged);

                if (File.Exists(merged))
                    File.Copy(merged, args[3], true);
                return code;
            }
            catch (Exception ex)
            {
                Fatal(ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// 1C often swaps left/right depending on compare target (storage, EPF, …).
        /// Prefer order from dump file names:
        /// - «Основная конфигурация» → right
        /// - «Конфигурация хранилища N» → left; if both have N → lower version left
        /// </summary>
        private static void OrderByOneCName(string a, string b, out string left, out string right)
        {
            FileRole roleA = ParseRole(a);
            FileRole roleB = ParseRole(b);

            if (roleA.StorageVersion.HasValue && roleB.StorageVersion.HasValue)
            {
                if (roleA.StorageVersion.Value <= roleB.StorageVersion.Value)
                {
                    left = a;
                    right = b;
                }
                else
                {
                    left = b;
                    right = a;
                }
                return;
            }

            if (roleA.IsMain && !roleB.IsMain)
            {
                left = b;
                right = a;
                return;
            }
            if (roleB.IsMain && !roleA.IsMain)
            {
                left = a;
                right = b;
                return;
            }

            if (roleA.StorageVersion.HasValue && !roleB.StorageVersion.HasValue)
            {
                left = a;
                right = b;
                return;
            }
            if (roleB.StorageVersion.HasValue && !roleA.StorageVersion.HasValue)
            {
                left = b;
                right = a;
                return;
            }

            left = a;
            right = b;
        }

        private static FileRole ParseRole(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            bool isMain = name.IndexOf(MainConfigMarker, StringComparison.OrdinalIgnoreCase) >= 0;
            int? version = null;
            Match m = StorageVersionRx.Match(name);
            if (m.Success)
            {
                int v;
                if (int.TryParse(m.Groups[1].Value, out v))
                    version = v;
            }
            return new FileRole(isMain, version);
        }

        private struct FileRole
        {
            public readonly bool IsMain;
            public readonly int? StorageVersion;

            public FileRole(bool isMain, int? storageVersion)
            {
                IsMain = isMain;
                StorageVersion = storageVersion;
            }
        }

        private static string CopyAsBsl(string src, string tmpDir, string tag)
        {
            if (!File.Exists(src))
                throw new FileNotFoundException("Файл не найден: " + src);

            string baseName = Path.GetFileNameWithoutExtension(src);
            if (string.IsNullOrEmpty(baseName))
                baseName = tag;

            string dest = Path.Combine(tmpDir, tag + "-" + Sanitize(baseName) + ".bsl");
            File.Copy(src, dest, true);
            return dest;
        }

        private static string TempBsl(string tmpDir, string tag)
        {
            return Path.Combine(tmpDir, tag + "-" + Guid.NewGuid().ToString("N") + ".bsl");
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static int RunCursor(string cursorExe, string cliJs, params string[] cliArgs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = cursorExe,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(cursorExe) ?? Environment.CurrentDirectory
            };

            // Same as resources\app\bin\cursor.cmd
            psi.EnvironmentVariables["ELECTRON_RUN_AS_NODE"] = "1";
            psi.EnvironmentVariables["VSCODE_DEV"] = "";

            var sb = new StringBuilder();
            AppendArg(sb, cliJs);
            foreach (string a in cliArgs)
            {
                sb.Append(' ');
                AppendArg(sb, a);
            }
            psi.Arguments = sb.ToString();

            using (var p = Process.Start(psi))
            {
                if (p == null)
                    throw new InvalidOperationException("Не удалось запустить Cursor.");
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        private static void AppendArg(StringBuilder sb, string arg)
        {
            if (string.IsNullOrEmpty(arg))
            {
                sb.Append("\"\"");
                return;
            }

            bool quote = arg.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0;
            if (!quote)
            {
                sb.Append(arg);
                return;
            }

            sb.Append('"');
            for (int i = 0; i < arg.Length; i++)
            {
                if (arg[i] == '"')
                    sb.Append('\\');
                sb.Append(arg[i]);
            }
            sb.Append('"');
        }

        private static void Fatal(string message)
        {
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    message,
                    "Cursor 1C Diff",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            catch
            {
                // ignore UI failures
            }
        }
    }
}
