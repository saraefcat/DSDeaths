using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;

namespace DSDeaths.Live {
    internal enum EldenRingOfflineLaunchErrorCode {
        None,
        InvalidExecutablePath,
        WrongExecutableName,
        ExecutableNotFound,
        AppIdFileConflict,
        AppIdFileReadFailed,
        AppIdFileWriteFailed,
        ProcessCheckFailed,
        AlreadyRunning,
        ProcessStartFailed
    }

    internal sealed class EldenRingOfflineLaunchPreparation {
        internal EldenRingOfflineLaunchPreparation(
            string executablePath,
            string appIdFilePath,
            bool appIdFileCreated) {
            ExecutablePath = executablePath;
            AppIdFilePath = appIdFilePath;
            AppIdFileCreated = appIdFileCreated;
        }

        internal string ExecutablePath { get; private set; }
        internal string AppIdFilePath { get; private set; }
        internal bool AppIdFileCreated { get; private set; }
    }

    internal static class EldenRingOfflineLauncher {
        internal const string ExecutableFileName = "eldenring.exe";
        internal const string AppIdFileName = "steam_appid.txt";
        internal const string SteamAppId = "1245620";

        internal static string FindDefaultExecutablePath() {
            string[] programFolders = {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };
            foreach (string programFolder in programFolders) {
                if (string.IsNullOrEmpty(programFolder)) {
                    continue;
                }

                string candidate = Path.Combine(
                    programFolder,
                    "Steam",
                    "steamapps",
                    "common",
                    "ELDEN RING",
                    "Game",
                    ExecutableFileName);
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }
            return null;
        }

        internal static bool TryValidateExecutable(
            string executablePath,
            out string normalizedPath,
            out EldenRingOfflineLaunchErrorCode errorCode,
            out string error) {
            normalizedPath = null;
            if (string.IsNullOrWhiteSpace(executablePath)) {
                errorCode = EldenRingOfflineLaunchErrorCode.InvalidExecutablePath;
                error = "No executable path was supplied.";
                return false;
            }

            try {
                normalizedPath = Path.GetFullPath(executablePath);
            } catch (Exception exception) when (
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is PathTooLongException ||
                exception is SecurityException) {
                errorCode = EldenRingOfflineLaunchErrorCode.InvalidExecutablePath;
                error = exception.Message;
                return false;
            }

            if (!string.Equals(
                    Path.GetFileName(normalizedPath),
                    ExecutableFileName,
                    StringComparison.OrdinalIgnoreCase)) {
                errorCode = EldenRingOfflineLaunchErrorCode.WrongExecutableName;
                error = normalizedPath;
                return false;
            }

            if (!File.Exists(normalizedPath)) {
                errorCode = EldenRingOfflineLaunchErrorCode.ExecutableNotFound;
                error = normalizedPath;
                return false;
            }

            errorCode = EldenRingOfflineLaunchErrorCode.None;
            error = null;
            return true;
        }

        internal static bool TryPrepare(
            string executablePath,
            out EldenRingOfflineLaunchPreparation preparation,
            out EldenRingOfflineLaunchErrorCode errorCode,
            out string error) {
            preparation = null;
            string normalizedPath;
            if (!TryValidateExecutable(
                    executablePath,
                    out normalizedPath,
                    out errorCode,
                    out error)) {
                return false;
            }

            string directory = Path.GetDirectoryName(normalizedPath);
            string appIdFilePath = Path.Combine(directory, AppIdFileName);
            bool created = false;

            if (File.Exists(appIdFilePath)) {
                if (!TryValidateAppIdFile(appIdFilePath, out errorCode, out error)) {
                    return false;
                }
            } else {
                try {
                    using (var stream = new FileStream(
                        appIdFilePath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.Read)) {
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) {
                            writer.WriteLine(SteamAppId);
                        }
                    }
                    created = true;
                } catch (IOException exception) {
                    if (!File.Exists(appIdFilePath) ||
                        !TryValidateAppIdFile(appIdFilePath, out errorCode, out error)) {
                        if (errorCode == EldenRingOfflineLaunchErrorCode.None) {
                            errorCode = EldenRingOfflineLaunchErrorCode.AppIdFileWriteFailed;
                            error = exception.Message;
                        }
                        return false;
                    }
                } catch (Exception exception) when (
                    exception is UnauthorizedAccessException ||
                    exception is SecurityException ||
                    exception is PathTooLongException ||
                    exception is NotSupportedException) {
                    errorCode = EldenRingOfflineLaunchErrorCode.AppIdFileWriteFailed;
                    error = exception.Message;
                    return false;
                }
            }

            preparation = new EldenRingOfflineLaunchPreparation(
                normalizedPath,
                appIdFilePath,
                created);
            errorCode = EldenRingOfflineLaunchErrorCode.None;
            error = null;
            return true;
        }

        internal static bool TryStart(
            EldenRingOfflineLaunchPreparation preparation,
            out Process process,
            out EldenRingOfflineLaunchErrorCode errorCode,
            out string error) {
            process = null;
            if (preparation == null) {
                errorCode = EldenRingOfflineLaunchErrorCode.InvalidExecutablePath;
                error = "Offline launch was not prepared.";
                return false;
            }

            if (!TryEnsureGameNotRunning(out errorCode, out error)) {
                return false;
            }

            try {
                process = Process.Start(new ProcessStartInfo {
                    FileName = preparation.ExecutablePath,
                    WorkingDirectory = Path.GetDirectoryName(preparation.ExecutablePath),
                    UseShellExecute = true
                });
                errorCode = EldenRingOfflineLaunchErrorCode.None;
                error = null;
                return true;
            } catch (Exception exception) when (
                exception is Win32Exception ||
                exception is InvalidOperationException ||
                exception is IOException ||
                exception is UnauthorizedAccessException) {
                errorCode = EldenRingOfflineLaunchErrorCode.ProcessStartFailed;
                error = exception.Message;
                return false;
            }
        }

        internal static bool TryEnsureGameNotRunning(
            out EldenRingOfflineLaunchErrorCode errorCode,
            out string error) {
            return TryEnsureGameNotRunning(
                Process.GetProcessesByName,
                out errorCode,
                out error);
        }

        internal static bool TryEnsureGameNotRunning(
            Func<string, Process[]> getProcessesByName,
            out EldenRingOfflineLaunchErrorCode errorCode,
            out string error) {
            if (getProcessesByName == null) {
                throw new ArgumentNullException(nameof(getProcessesByName));
            }

            Process[] processes = null;
            try {
                processes = getProcessesByName(
                    Path.GetFileNameWithoutExtension(ExecutableFileName));
                if (processes != null && processes.Length > 0) {
                    errorCode = EldenRingOfflineLaunchErrorCode.AlreadyRunning;
                    error = null;
                    return false;
                }

                errorCode = EldenRingOfflineLaunchErrorCode.None;
                error = null;
                return true;
            } catch (Exception exception) when (
                exception is Win32Exception ||
                exception is InvalidOperationException ||
                exception is NotSupportedException) {
                errorCode = EldenRingOfflineLaunchErrorCode.ProcessCheckFailed;
                error = exception.Message;
                return false;
            } finally {
                if (processes != null) {
                    foreach (Process runningProcess in processes) {
                        runningProcess.Dispose();
                    }
                }
            }
        }

        private static bool TryValidateAppIdFile(
            string appIdFilePath,
            out EldenRingOfflineLaunchErrorCode errorCode,
            out string error) {
            string content;
            try {
                content = File.ReadAllText(appIdFilePath).Trim();
            } catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is SecurityException ||
                exception is PathTooLongException ||
                exception is NotSupportedException) {
                errorCode = EldenRingOfflineLaunchErrorCode.AppIdFileReadFailed;
                error = exception.Message;
                return false;
            }

            if (!string.Equals(content, SteamAppId, StringComparison.Ordinal)) {
                errorCode = EldenRingOfflineLaunchErrorCode.AppIdFileConflict;
                error = appIdFilePath;
                return false;
            }

            errorCode = EldenRingOfflineLaunchErrorCode.None;
            error = null;
            return true;
        }
    }
}
