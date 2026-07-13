using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PhiScanGui
{
    /// <summary>
    /// Filesystem probes that cannot hang the caller. <see cref="Directory.Exists"/>
    /// returns false for a missing path, but on an UNREACHABLE UNC host it first
    /// blocks the calling thread for the full SMB/TCP timeout (tens of seconds).
    /// When that call sits on the UI thread — e.g. the folder picker resolving its
    /// initial folder — the whole app locks. These helpers bound the wait.
    /// </summary>
    public static class SafeDirectory
    {
        /// <summary>
        /// <see cref="Directory.Exists"/> bounded by <paramref name="timeout"/>.
        /// Returns false if the path is missing, empty, errors, or does not answer
        /// within the timeout. A timed-out probe is abandoned (it completes later on
        /// its worker thread and is discarded) — the point is that the CALLER never
        /// waits longer than the timeout.
        /// </summary>
        public static bool ExistsWithTimeout(string path, TimeSpan timeout)
        {
            return ExistsWithTimeout(path, timeout, Directory.Exists);
        }

        /// <summary>
        /// Testable core: the existence check is injected so tests can simulate a
        /// hanging or throwing probe without needing a real unreachable share.
        /// </summary>
        internal static bool ExistsWithTimeout(string path, TimeSpan timeout, Func<string, bool> existsProbe)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                // Run the (potentially blocking) probe on a worker thread and wait
                // only up to the timeout. `&& task.Result` is guarded by `task.Wait`
                // returning true, so we never read Result while it would still block.
                Task<bool> task = Task.Run(() => existsProbe(path));
                return task.Wait(timeout) && task.Result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Directory existence check for '{path}' failed: {ex.Message}");
                return false;
            }
        }
    }
}
