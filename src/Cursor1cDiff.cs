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
    /// - For 2-file compare: reorder by 1C dump names
    ///   (external file = right, higher storage version = right, main config is the highest version)
    /// </summary>
    internal static class Program
    {
        // (Основная конфигурация) / (Конфигурация хранилища 1050)
        private static readonly Regex StorageVersionRx = new Regex(
            @"Конфигурация\s+хранилища\s+(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // (Файл C:\cfg\1Cv8.cf) / (Файл) / «внешний файл»
        private static readonly Regex ExternalFileRx = new Regex(
            @"\(Файл(?:\s|\)|$)|внешн\w*\s+файл",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private const string MainConfigMarker = "Основная конфигурация";

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args != null && args.Length == 1 && args[0] == "--self-test")
                    return RunOrderSelfTest();

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
        /// 1C often swaps left/right depending on compare target (storage, file, EPF, …).
        /// Right side is the more senior dump name:
        /// - external file «(Файл …)» is always right
        /// - higher «Конфигурация хранилища N» is right
        /// - «Основная конфигурация» counts as the highest version
        /// </summary>
        private static void OrderByOneCName(string a, string b, out string left, out string right)
        {
            int cmp = CompareSeniority(ParseRole(a), ParseRole(b));
            if (cmp > 0)
            {
                left = b;
                right = a;
            }
            else
            {
                left = a;
                right = b;
            }
        }

        /// <summary>
        /// Positive when <paramref name="a"/> is more senior than <paramref name="b"/> and belongs on the right.
        /// </summary>
        private static int CompareSeniority(FileRole a, FileRole b)
        {
            if (a.IsExternal != b.IsExternal)
                return a.IsExternal ? 1 : -1;
            if (a.IsMain != b.IsMain)
                return a.IsMain ? 1 : -1;
            if (a.StorageVersion.HasValue && b.StorageVersion.HasValue)
                return a.StorageVersion.Value.CompareTo(b.StorageVersion.Value);
            if (a.StorageVersion.HasValue != b.StorageVersion.HasValue)
                return a.StorageVersion.HasValue ? 1 : -1;
            return 0;
        }

        private static FileRole ParseRole(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            bool isExternal = ExternalFileRx.IsMatch(name);
            bool isMain = name.IndexOf(MainConfigMarker, StringComparison.OrdinalIgnoreCase) >= 0;
            int? version = null;
            Match m = StorageVersionRx.Match(name);
            if (m.Success)
            {
                int v;
                if (int.TryParse(m.Groups[1].Value, out v))
                    version = v;
            }
            return new FileRole(isExternal, isMain, version);
        }

        private struct FileRole
        {
            public readonly bool IsExternal;
            public readonly bool IsMain;
            public readonly int? StorageVersion;

            public FileRole(bool isExternal, bool isMain, int? storageVersion)
            {
                IsExternal = isExternal;
                IsMain = isMain;
                StorageVersion = storageVersion;
            }
        }

        private static int RunOrderSelfTest()
        {
            string main = @"C:\t\Модуль(Основная конфигурация).txt";
            string filesModule = @"C:\t\ОбщийМодуль.РаботаСФайлами(Основная конфигурация).txt";
            string storageLow = @"C:\t\Модуль(Конфигурация хранилища 3393).txt";
            string storageLowOther = @"C:\t\Другой(Конфигурация хранилища 3393).txt";
            string storageHigh = @"C:\t\Модуль(Конфигурация хранилища 3400).txt";
            string external = @"C:\t\Модуль(Файл __adm-fs_1Cv8.cf).txt";
            string externalNested = @"C:\t\Форма(Файл __Чередников Дмитрий (Дзета)_1Cv8.cf).txt";
            string externalPhrase = @"C:\t\Модуль(внешний файл).txt";
            string extension = @"C:\t\Форма(Расширение конфигурации).txt";
            string plainA = @"C:\t\a.txt";
            string plainB = @"C:\t\b.txt";

            int failed = 0;
            failed += ExpectRight(external, main, external, "файл правее основной");
            failed += ExpectRight(main, external, external, "файл правее основной при обратном порядке аргументов");
            failed += ExpectRight(externalNested, main, externalNested, "файл с вложенными скобками в пути");
            failed += ExpectRight(externalPhrase, main, externalPhrase, "подпись «внешний файл»");
            failed += ExpectRight(storageLow, external, external, "файл правее версии хранилища");
            failed += ExpectRight(extension, external, external, "файл правее расширения");
            failed += ExpectRight(filesModule, external, external, "слово Файл в имени модуля не делает сторону внешней");
            failed += ExpectRight(storageLow, main, main, "основная старше версии хранилища");
            failed += ExpectRight(main, storageHigh, main, "основная старше даже большего номера");
            failed += ExpectRight(filesModule, storageHigh, filesModule, "основная в модуле про файлы старше версии");
            failed += ExpectRight(storageLow, storageHigh, storageHigh, "старший номер хранилища справа");
            failed += ExpectRight(storageHigh, storageLow, storageHigh, "старший номер справа при обратном порядке");
            failed += ExpectRight(storageLow, extension, storageLow, "версия хранилища старше стороны без роли");
            failed += ExpectRight(plainA, plainB, plainB, "без ролей сохраняется порядок аргументов");
            failed += ExpectRight(storageLow, storageLowOther, storageLowOther, "равные версии не меняются местами");

            if (failed != 0)
            {
                Console.Error.WriteLine("self-test failed: " + failed);
                return 1;
            }
            Console.WriteLine("self-test ok");
            return 0;
        }

        private static int ExpectRight(string a, string b, string expectedRight, string title)
        {
            string left, right;
            OrderByOneCName(a, b, out left, out right);
            if (right == expectedRight)
                return 0;
            Console.Error.WriteLine("FAIL: " + title);
            Console.Error.WriteLine("  right=" + right);
            return 1;
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
