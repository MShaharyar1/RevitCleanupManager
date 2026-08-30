using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Microsoft.Win32;
using RevitCleanupManager.Core.Commands;
using RevitCleanupManager.Core.Config;
using RevitCleanupManager.Core.Excel;
using RevitCleanupManager.Core.Models;
using RevitCleanupManager.Core.QaRules;

namespace RevitCleanupManager.UI.ViewModels
{
    public class QaDashboardViewModel : ObservableObject
    {
        private readonly Document _doc;
        private readonly ExcelRoundTripService _excel = new ExcelRoundTripService();
        private readonly ParameterUpdateExecutor _updateExecutor = new ParameterUpdateExecutor();
        private readonly ParameterGridBuilder _gridBuilder = new ParameterGridBuilder();
        private QaConfig _config;

        public ObservableCollection<QaIssueViewModel> AllIssues { get; } = new ObservableCollection<QaIssueViewModel>();
        public ObservableCollection<QaIssueViewModel> FilteredIssues { get; } = new ObservableCollection<QaIssueViewModel>();
        public ObservableCollection<CategoryFilterOption> CategoryFilterOptions { get; } = new ObservableCollection<CategoryFilterOption>();

        private CategoryFilterOption _selectedCategoryFilter;
        public CategoryFilterOption SelectedCategoryFilter
        {
            get => _selectedCategoryFilter;
            set { if (SetField(ref _selectedCategoryFilter, value)) ApplyFilter(); }
        }

        private string _statusText = "Ready. Click \"Run QA/QC Scan\" to check the model.";
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

        private int _selectedCount;
        public int SelectedCount { get => _selectedCount; set => SetField(ref _selectedCount, value); }

        // --- Bulk Parameter Editor (Param/Family/Sheet Manager style) fields ---
        private string _bulkCategories = "Doors, Windows, Rooms";
        public string BulkCategories { get => _bulkCategories; set => SetField(ref _bulkCategories, value); }

        private string _bulkParameters = "Mark, Comments";
        public string BulkParameters { get => _bulkParameters; set => SetField(ref _bulkParameters, value); }

        public ICommand ScanCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand ApplySelectedFixesCommand { get; }
        public ICommand ExportIssuesCommand { get; }
        public ICommand ImportIssuesCommand { get; }
        public ICommand ExportBulkParametersCommand { get; }
        public ICommand ImportBulkParametersCommand { get; }

        public QaDashboardViewModel(Document doc)
        {
            _doc = doc;
            _config = LoadConfig();

            ScanCommand = new RelayCommand(_ => Scan());
            SelectAllCommand = new RelayCommand(_ => { foreach (var i in FilteredIssues) i.IsSelected = true; RecomputeSelection(); });
            SelectNoneCommand = new RelayCommand(_ => { foreach (var i in FilteredIssues) i.IsSelected = false; RecomputeSelection(); });
            ApplySelectedFixesCommand = new RelayCommand(_ => ApplySelectedFixes(), _ => SelectedCount > 0);
            ExportIssuesCommand = new RelayCommand(_ => ExportIssues(), _ => AllIssues.Count > 0);
            ImportIssuesCommand = new RelayCommand(_ => ImportIssues());
            ExportBulkParametersCommand = new RelayCommand(_ => ExportBulkParameters());
            ImportBulkParametersCommand = new RelayCommand(_ => ImportBulkParameters());

            Scan();
        }

        private QaConfig LoadConfig()
        {
            try
            {
                var dllDir = Path.GetDirectoryName(typeof(QaDashboardViewModel).Assembly.Location);
                var configPath = Path.Combine(dllDir ?? "", "QaConfig.json");
                return QaConfig.LoadOrDefault(configPath);
            }
            catch
            {
                return QaConfig.Default();
            }
        }

        public void Scan()
        {
            StatusText = "Running QA/QC checks...";
            var issues = QaScanner.RunAll(_doc, _config, msg => StatusText = msg);

            AllIssues.Clear();
            foreach (var issue in issues)
            {
                var vm = new QaIssueViewModel(issue);
                vm.PropertyChanged += (_, __) => RecomputeSelection();
                AllIssues.Add(vm);
            }

            CategoryFilterOptions.Clear();
            CategoryFilterOptions.Add(new CategoryFilterOption { Name = "All Categories" });
            foreach (var cat in issues.Select(i => i.RevitCategory).Distinct().OrderBy(c => c))
                CategoryFilterOptions.Add(new CategoryFilterOption { Name = cat });
            SelectedCategoryFilter = CategoryFilterOptions.FirstOrDefault();

            ApplyFilter();
            StatusText = $"QA/QC scan complete. {AllIssues.Count} issue(s) found.";
        }

        private void ApplyFilter()
        {
            FilteredIssues.Clear();
            var source = SelectedCategoryFilter == null || SelectedCategoryFilter.Name == "All Categories"
                ? AllIssues
                : AllIssues.Where(i => i.RevitCategory == SelectedCategoryFilter.Name);
            foreach (var i in source) FilteredIssues.Add(i);
            RecomputeSelection();
        }

        private void RecomputeSelection() => SelectedCount = AllIssues.Count(i => i.IsSelected);

        private void ApplySelectedFixes()
        {
            var selected = AllIssues.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return;

            var confirm = MessageBox.Show($"Apply {selected.Count} fix(es) to the model? This is one undoable batch.",
                "Confirm Fixes", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            var updates = selected.Select(i => new ImportedUpdateRow { Id = i.Model.Id, ParameterName = i.Model.ParameterName, NewValue = i.ProposedValue });
            var results = _updateExecutor.Apply(_doc, updates);
            ReportResults(results);
            Scan();
        }

        private void ExportIssues()
        {
            var dlg = new SaveFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx", FileName = "RevitCleanupManager_QA_Issues.xlsx" };
            if (dlg.ShowDialog() != true) return;
            _excel.ExportQaIssues(AllIssues.Select(i => i.Model).ToList(), dlg.FileName);
            StatusText = $"Exported {AllIssues.Count} issue(s) to {dlg.FileName}";
        }

        private void ImportIssues()
        {
            var dlg = new OpenFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var updates = _excel.ImportQaFixes(dlg.FileName);
                if (updates.Count == 0)
                {
                    MessageBox.Show("No filled-in \"New Value\" cells were found in that file.", "Nothing to Import", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var confirm = MessageBox.Show($"Found {updates.Count} filled-in fix(es) in the file. Apply them to the model now?",
                    "Confirm Import", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;

                var results = _updateExecutor.Apply(_doc, updates);
                ReportResults(results);
                Scan();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read that Excel file: {ex.Message}", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportBulkParameters()
        {
            var categories = SplitCsv(BulkCategories);
            var parameters = SplitCsv(BulkParameters);
            if (categories.Count == 0 || parameters.Count == 0)
            {
                MessageBox.Show("Enter at least one category and one parameter name (comma-separated).", "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rows = _gridBuilder.Build(_doc, categories, parameters);
            if (rows.Count == 0)
            {
                MessageBox.Show("No elements found for those categories.", "Nothing to Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx", FileName = "RevitCleanupManager_Parameters.xlsx" };
            if (dlg.ShowDialog() != true) return;
            _excel.ExportParameterGrid(rows, parameters, dlg.FileName);
            StatusText = $"Exported {rows.Count} element(s) x {parameters.Count} parameter(s) to {dlg.FileName}";
        }

        private void ImportBulkParameters()
        {
            var dlg = new OpenFileDialog { Filter = "Excel Workbook (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var updates = _excel.ImportParameterGrid(dlg.FileName);
                if (updates.Count == 0)
                {
                    MessageBox.Show("No parameter cells with values were found in that file.", "Nothing to Import", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var confirm = MessageBox.Show($"Found {updates.Count} parameter value(s) to write back to the model. Apply now?",
                    "Confirm Import", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;

                var results = _updateExecutor.Apply(_doc, updates);
                ReportResults(results);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read that Excel file: {ex.Message}", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static System.Collections.Generic.List<string> SplitCsv(string csv) =>
            csv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        private void ReportResults(System.Collections.Generic.List<ParameterUpdateResult> results)
        {
            int success = results.Count(r => r.Success);
            int failed = results.Count - success;
            StatusText = $"Applied updates: {success} succeeded, {failed} failed.";
            if (failed > 0)
            {
                var lines = string.Join("\n", results.Where(r => !r.Success).Select(r => $"- {r.ElementName ?? r.Id.ToString()} [{r.ParameterName}]: {r.Message}"));
                MessageBox.Show($"{failed} update(s) could not be applied:\n\n{lines}", "Some Updates Skipped", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
