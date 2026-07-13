using System.ComponentModel;
using PhiScanGui.Scanner;

namespace PhiScanGui.ViewModels
{
    public class FindingViewModel : INotifyPropertyChanged
    {
        public Finding Finding { get; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }

        public string SeverityText => Finding.Severity.ToString().ToUpperInvariant();
        public string RelativePath => Finding.RelativePath;
        public string Reason => Finding.Reason;
        public string GitignoreEntry => GitignoreUpdater.EntryFor(Finding);
        public bool IsCovered => Finding.IsCoveredByGitignore;
        public string CoveredText => IsCovered ? "✓ ignored" : "";

        public FindingViewModel(Finding finding)
        {
            Finding = finding;
            // Uncovered HIGH findings start checked: the default action is to ignore
            // them. Already-covered ones need no new .gitignore entry.
            _isChecked = finding.Severity == Severity.High && !finding.IsCoveredByGitignore;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
