using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using RevitCleanupManager.Core.Commands;
using RevitCleanupManager.Core.Models;
using RevitCleanupManager.Core.Scanners;

namespace RevitCleanupManager.UI.ViewModels
{
    /// <summary>Cleanup tab (purgeable families, unplaced views, links, etc.) -- unchanged from v1.</summary>
    public class DashboardViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly ModelHealthAnalyzer _healthAnalyzer = new ModelHealthAnalyzer();
        private readonly CleanupExecutor _executor = new CleanupExecutor();

        public ObservableCollection<CategoryTabViewModel> Tabs { get; } = new ObservableCollection<CategoryTabViewModel>();
        public ObservableCollection<HealthMetric> HealthMetrics { get; } = new ObservableCollection<HealthMetric>();

        private string _statusText = "Ready. Click \"Rescan Model\" to begin.";
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
        private int _overallScore;
        public int OverallScore { get => _overallScore; set => SetField(ref _overallScore, value); }
        private string _overallGrade = "-";
        public string OverallGrade { get => _overallGrade; set => SetField(ref _overallGrade, value); }
        private int _totalCandidates;
        public int TotalCandidates { get => _totalCandidates; set => SetField(ref _totalCandidates, value); }
        private int _totalSelected;
        public int TotalSelected { get => _totalSelected; set => SetField(ref _totalSelected, value); }

        public ICommand ScanCommand { get; }
        public ICommand SelectAllSafeCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand RunCleanupCommand { get; }

        private static readonly (CleanupCategory, string)[] CategoryLabels =
        {
            (CleanupCategory.PurgeableFamily, "Purgeable Families/Types"),
            (CleanupCategory.UnplacedView, "Unplaced Views"),
            (CleanupCategory.UnplacedSchedule, "Unplaced Schedules"),
            (CleanupCategory.UnplacedLegend, "Unplaced Legends"),
            (CleanupCategory.UnusedFilter, "Unused Filters"),
            (CleanupCategory.UnusedViewTemplate, "Unused View Templates"),
            (CleanupCategory.RevitLink, "Revit Links"),
            (CleanupCategory.CadImportOrLink, "CAD Imports/Links"),
            (CleanupCategory.InPlaceFamily, "In-Place Families"),
            (CleanupCategory.UnusedGroup, "Unused Groups"),
        };

        public DashboardViewModel(Document doc)
        {
            _doc = doc;
            foreach (var (category, label) in CategoryLabels) Tabs.Add(new CategoryTabViewModel(category, label));

            ScanCommand = new RelayCommand(_ => Scan());
            SelectAllSafeCommand = new RelayCommand(_ => { foreach (var t in Tabs) t.SelectAllSafe(); RecomputeSelection(); });
            SelectAllCommand = new RelayCommand(_ => { foreach (var t in Tabs) t.SelectAll(); RecomputeSelection(); });
            SelectNoneCommand = new RelayCommand(_ => { foreach (var t in Tabs) t.SelectNone(); RecomputeSelection(); });
            RunCleanupCommand = new RelayCommand(_ => RunCleanup(), _ => TotalSelected > 0);

            Scan();
        }

        public void Scan()
        {
            StatusText = "Scanning model...";
            var scanResult = ScannerFactory.RunAll(_doc, msg => StatusText = msg);

            foreach (var tab in Tabs)
            {
                tab.Load(scanResult.Get(tab.Category));
                foreach (var item in tab.Items) item.PropertyChanged += (_, __) => RecomputeSelection();
            }

            var healthReport = _healthAnalyzer.Analyze(_doc, scanResult, _doc.PathName);
            HealthMetrics.Clear();
            foreach (var m in healthReport.Metrics) HealthMetrics.Add(m);
            OverallScore = healthReport.OverallScore;
            OverallGrade = healthReport.OverallGrade;

            TotalCandidates = scanResult.TotalCount;
            RecomputeSelection();
            StatusText = $"Scan complete. {TotalCandidates} cleanup candidates found across {Tabs.Count} categories.";
        }

        private void RecomputeSelection()
        {
            TotalSelected = Tabs.Sum(t => t.Items.Count(i => i.IsSelected));
            OnPropertyChanged(nameof(TotalSelected));
        }

        private void RunCleanup()
        {
            var selectedItems = Tabs.SelectMany(t => t.Items).Where(i => i.IsSelected).Select(i => i.Model).ToList();
            if (selectedItems.Count == 0) return;

            var confirm = MessageBox.Show($"This will permanently delete {selectedItems.Count} selected item(s) in one undoable batch.\n\nContinue?",
                "Confirm Cleanup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            StatusText = "Deleting selected items...";
            var runReport = _executor.DeleteSelected(_doc, selectedItems);
            StatusText = $"Cleanup finished: {runReport.SuccessCount} succeeded, {runReport.FailureCount} failed. Rescanning...";

            if (runReport.FailureCount > 0)
            {
                var failures = string.Join("\n", runReport.Lines.Where(l => !l.Success).Select(l => $"- {l.Name}: {l.Message}"));
                MessageBox.Show($"{runReport.FailureCount} item(s) could not be deleted (likely still referenced elsewhere):\n\n{failures}",
                    "Some Items Skipped", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            Scan();
        }
    }
}
