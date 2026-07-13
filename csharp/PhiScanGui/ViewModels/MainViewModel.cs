using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using PhiScanGui.Scanner;

namespace PhiScanGui.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly PhiScanner _scanner = new PhiScanner();
        private CancellationTokenSource _cts;

        public ObservableCollection<FindingViewModel> Findings { get; } = new ObservableCollection<FindingViewModel>();

        private string _folderPath = "";
        public string FolderPath
        {
            get => _folderPath;
            set { _folderPath = value; OnPropertyChanged(nameof(FolderPath)); }
        }

        private string _status = "Pick a folder and click Scan. Do this BEFORE the folder becomes a git repository.";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            set { _isScanning = value; OnPropertyChanged(nameof(IsScanning)); }
        }

        public RelayCommand BrowseCommand { get; }
        public RelayCommand ScanCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand AddToGitignoreCommand { get; }
        public RelayCommand CopyReportCommand { get; }
        public RelayCommand OpenInExplorerCommand { get; }

        public FindingViewModel SelectedFinding { get; set; }

        public MainViewModel()
        {
            BrowseCommand = new RelayCommand(Browse, () => !IsScanning);
            ScanCommand = new RelayCommand(async () => await ScanAsync(),
                () => !IsScanning && Directory.Exists(FolderPath));
            CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsScanning);
            AddToGitignoreCommand = new RelayCommand(AddToGitignore,
                () => !IsScanning && Findings.Any(f => f.IsChecked));
            CopyReportCommand = new RelayCommand(CopyReport, () => Findings.Count > 0);
            OpenInExplorerCommand = new RelayCommand(OpenInExplorer, () => SelectedFinding != null);
        }

        private void Browse()
        {
            // Modern Explorer-chrome folder picker (address bar, quick access, resizable),
            // with a graceful fallback to the classic dialog if the COM picker is
            // unavailable. Owned by the main window so it stays modal to the app.
            IntPtr owner = Application.Current?.MainWindow != null
                ? new WindowInteropHelper(Application.Current.MainWindow).Handle
                : IntPtr.Zero;

            string picked = ModernFolderPicker.PickFolderWithFallback(
                "Pick the project folder to scan for PHI", FolderPath, owner);
            if (!string.IsNullOrEmpty(picked))
                FolderPath = picked;
        }

        private async Task ScanAsync()
        {
            Findings.Clear();
            IsScanning = true;
            _cts = new CancellationTokenSource();
            var progress = new Progress<string>(rel => Status = "Scanning: " + rel);
            var root = FolderPath;

            try
            {
                var results = await Task.Run(() => _scanner.Scan(root, progress, _cts.Token));
                foreach (var f in results)
                    Findings.Add(new FindingViewModel(f));

                int high = results.Count(f => f.Severity == Severity.High);
                int med = results.Count(f => f.Severity == Severity.Medium);
                int rev = results.Count(f => f.Severity == Severity.Review);
                int covered = results.Count(f => f.IsCoveredByGitignore);
                var coveredNote = covered == 0 ? "" :
                    $" {covered} already covered by .gitignore (safe from 'git add' — but NOT if git already tracks them).";
                Status = results.Count == 0
                    ? "No PHI indicators found. Reminder: a clean scan narrows the search, it does not prove absence."
                    : $"{results.Count} finding(s): HIGH={high}, MEDIUM={med}, REVIEW={rev}.{coveredNote} " +
                      "Best fix is moving data OUT of the folder; .gitignore is the backstop.";
            }
            catch (OperationCanceledException)
            {
                Status = "Scan cancelled.";
            }
            catch (Exception ex)
            {
                Status = "Scan failed: " + ex.Message;
            }
            finally
            {
                IsScanning = false;
                _cts.Dispose();
                _cts = null;
            }
        }

        private void AddToGitignore()
        {
            var selected = Findings.Where(f => f.IsChecked).Select(f => f.Finding).ToList();
            var preview = string.Join("\n", GitignoreUpdater.EntriesFor(selected));
            var confirm = MessageBox.Show(
                $"Append these entries to {Path.Combine(FolderPath, ".gitignore")}?\n\n{preview}\n\n" +
                "Remember: .gitignore does not untrack files git already tracks, and does not " +
                "protect anything already pushed. Moving data out of the folder is always better.",
                "Update .gitignore", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
                return;

            try
            {
                var added = GitignoreUpdater.Append(FolderPath, selected);
                Status = added.Count == 0
                    ? ".gitignore already covered everything selected — nothing added."
                    : $".gitignore: added {added.Count} entr{(added.Count == 1 ? "y" : "ies")}: {string.Join(", ", added)}";
            }
            catch (Exception ex)
            {
                Status = "Could not update .gitignore: " + ex.Message;
            }
        }

        private void CopyReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"PHI scan of {FolderPath} — {Findings.Count} finding(s)");
            foreach (var f in Findings)
                sb.AppendLine($"[{f.SeverityText}] {f.RelativePath}  <- {f.Reason}");
            Clipboard.SetText(sb.ToString());
            Status = "Report copied to clipboard.";
        }

        private void OpenInExplorer()
        {
            if (SelectedFinding == null) return;
            var full = Path.Combine(FolderPath, SelectedFinding.RelativePath.Replace('/', '\\'));
            if (File.Exists(full))
                Process.Start("explorer.exe", "/select,\"" + full + "\"");
            else if (Directory.Exists(full))
                Process.Start("explorer.exe", "\"" + full + "\"");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
