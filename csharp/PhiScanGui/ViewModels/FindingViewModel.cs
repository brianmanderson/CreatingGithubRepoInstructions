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

        public FindingViewModel(Finding finding)
        {
            Finding = finding;
            // HIGH findings start checked: the default action is to ignore them.
            _isChecked = finding.Severity == Severity.High;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
