using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace PhiScanGui
{
    /// <summary>
    /// Modern Vista-style folder picker — the same Explorer-chrome dialog family as the
    /// WPF file dialogs (address bar, quick access, resizable), but for choosing a folder.
    /// .NET Framework 4.8 has no built-in modern folder dialog (Microsoft.Win32's
    /// OpenFolderDialog only exists on .NET 8+), and WinForms' FolderBrowserDialog shows
    /// the old tree picker here, so this drives the IFileOpenDialog COM interface with the
    /// pick-folders option directly.
    /// </summary>
    public static class ModernFolderPicker
    {
        /// <summary>
        /// Shows the folder picker. Returns the chosen folder path, or null if the user
        /// cancelled. Throws only on a genuine COM failure — callers should catch and
        /// fall back to a classic dialog so the button never appears to do nothing.
        /// </summary>
        public static string PickFolder(string title, string initialFolder, IntPtr ownerHandle)
        {
            IFileOpenDialog dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            try
            {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);

                if (!string.IsNullOrEmpty(title))
                {
                    dialog.SetTitle(title);
                }

                SetInitialFolder(dialog, initialFolder);

                int hr = dialog.Show(ownerHandle);
                if (hr == ERROR_CANCELLED)
                {
                    return null;
                }
                if (hr != S_OK)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                IShellItem item;
                dialog.GetResult(out item);
                try
                {
                    string path;
                    item.GetDisplayName(SIGDN_FILESYSPATH, out path);
                    return path;
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        /// <summary>
        /// Shows the modern folder picker, falling back to the classic WinForms
        /// FolderBrowserDialog if the modern (COM) picker is unavailable, so the caller
        /// never has to see the button silently do nothing. Returns the chosen folder,
        /// or null if the user cancelled either dialog.
        /// </summary>
        public static string PickFolderWithFallback(string title, string initialFolder, IntPtr ownerHandle)
        {
            try
            {
                return PickFolder(title, initialFolder, ownerHandle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Modern folder picker unavailable, using the classic dialog: {ex.Message}");
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = title;
                    dialog.ShowNewFolderButton = true;
                    // Timeout-bounded: an unreachable UNC initial folder must not hang
                    // the UI thread here (see SafeDirectory).
                    if (SafeDirectory.ExistsWithTimeout(initialFolder, InitialFolderProbeTimeout))
                    {
                        dialog.SelectedPath = initialFolder;
                    }
                    return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                        ? dialog.SelectedPath
                        : null;
                }
            }
        }

        private static void SetInitialFolder(IFileOpenDialog dialog, string initialFolder)
        {
            // Timeout-bounded existence check: Directory.Exists on an unreachable UNC
            // host blocks for the full network timeout, and this runs on the UI thread
            // inside the modal dialog — an unbounded call here locks the whole app.
            if (!SafeDirectory.ExistsWithTimeout(initialFolder, InitialFolderProbeTimeout))
            {
                return;
            }

            try
            {
                Guid shellItemGuid = typeof(IShellItem).GUID;
                IShellItem item;
                SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref shellItemGuid, out item);
                if (item != null)
                {
                    try
                    {
                        dialog.SetFolder(item);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(item);
                    }
                }
            }
            catch
            {
                // Pre-navigating to the last folder is a nicety, not a requirement.
            }
        }

        // Cap on how long resolving the initial folder may block. Long enough for a
        // healthy share to answer, short enough that an unreachable one is a brief
        // pause, not a lock.
        private static readonly TimeSpan InitialFolderProbeTimeout = TimeSpan.FromSeconds(2);

        private const int S_OK = 0;
        private const int ERROR_CANCELLED = unchecked((int)0x800704C7);
        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [In] ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        // COM class for the shell FileOpenDialog.
        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRCW
        {
        }

        // IFileOpenDialog flattened over its bases (IModalWindow → IFileDialog →
        // IFileOpenDialog). The declaration order IS the vtable layout, so every base
        // method must be present in order; unused ones are stubbed as no-arg voids to
        // occupy their slot. Do not reorder.
        [ComImport]
        [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            // IModalWindow
            [PreserveSig]
            int Show(IntPtr parent);

            // IFileDialog
            void SetFileTypes();
            void SetFileTypeIndex();
            void GetFileTypeIndex();
            void Advise();
            void Unadvise();
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder();
            void GetCurrentSelection();
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName();
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel();
            void SetFileNameLabel();
            void GetResult(out IShellItem ppsi);
            void AddPlace();
            void SetDefaultExtension();
            void Close();
            void SetClientGuid();
            void ClearClientData();
            void SetFilter();

            // IFileOpenDialog
            void GetResults();
            void GetSelectedItems();
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler();
            void GetParent();
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes();
            void Compare();
        }
    }
}
