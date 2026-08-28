using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using RevitCleanupManager.Core;
using RevitCleanupManager.Models;

namespace RevitCleanupManager.UI
{
    public partial class DashboardWindow : Window
    {
        private readonly UIApplication _uiApp;

        private readonly ScanEventHandler _scanHandler = new();
        private readonly CleanEventHandler _cleanHandler = new();
        private readonly NativePurgeEventHandler _nativePurgeHandler = new();

        private readonly ExternalEvent _scanEvent;
        private readonly ExternalEvent _cleanEvent;
        private readonly ExternalEvent _nativePurgeEvent;

        private ScanResult? _lastScan;
        private readonly Dictionary<CleanupCategory, DataGrid> _grids = new();

        // Friendly labels for tabs, in the order they should appear.
        private static readonly (CleanupCategory Category, string Label)[] TabOrder =
        {
            (CleanupCategory.PurgeableFamilies, "Purgeable Families"),
            (CleanupCategory.UnplacedViews, "Unplaced Views"),
            (CleanupCategory.UnusedFilters, "Unused Filters"),
            (CleanupCategory.UnusedViewTemplates, "Unused Templates"),
            (CleanupCategory.UnplacedSchedules, "Unplaced Schedules"),
            (CleanupCategory.UnplacedLegends, "Unplaced Legends"),
            (CleanupCategory.RevitLinks, "Revit Links"),
            (CleanupCategory.CadImportsAndLinks, "CAD Imports / Links"),
            (CleanupCategory.UnusedGroups, "Unused Groups"),
            (CleanupCategory.ElementsWithWarnings, "Warnings (review only)"),
        };

        public DashboardWindow(UIApplication uiApp)
        {
            InitializeComponent();
            _uiApp = uiApp;

            // ExternalEvents must be created while we're in a valid API context -
            // the DashboardWindow constructor is called from IExternalCommand.Execute, so this is safe.
            _scanEvent = ExternalEvent.Create(_scanHandler);
            _cleanEvent = ExternalEvent.Create(_cleanHandler);
            _nativePurgeEvent = ExternalEvent.Create(_nativePurgeHandler);

            _scanHandler.OnScanComplete += result => Dispatcher.Invoke(() => PopulateUi(result));
            _scanHandler.OnError += msg => Dispatcher.Invoke(() => SetStatus($"Scan failed: {msg}", isError: true));

            _cleanHandler.OnCleanComplete += result => Dispatcher.Invoke(() => OnCleanComplete(result));
            _cleanHandler.OnError += msg => Dispatcher.Invoke(() => SetStatus($"Cleanup failed: {msg}", isError: true));

            BuildTabs();
        }

        private void BuildTabs()
        {
            foreach (var (category, label) in TabOrder)
            {
                var grid = new DataGrid
                {
                    Margin = new Thickness(0, 8, 0, 0),
                    Columns =
                    {
                        new DataGridCheckBoxColumn
                        {
                            Header = "",
                            Binding = new System.Windows.Data.Binding(nameof(CleanupItem.IsSelected)) { Mode = System.Windows.Data.BindingMode.TwoWay },
                            Width = 32
                        },
                        new DataGridTextColumn
                        {
                            Header = "Name", Binding = new System.Windows.Data.Binding(nameof(CleanupItem.Name)), Width = new DataGridLength(2, DataGridLengthUnitType.Star)
                        },
                        new DataGridTextColumn
                        {
                            Header = "Type / Category", Binding = new System.Windows.Data.Binding(nameof(CleanupItem.TypeOrCategory)), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star)
                        },
                        new DataGridTextColumn
                        {
                            Header = "Detail", Binding = new System.Windows.Data.Binding(nameof(CleanupItem.Detail)), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star)
                        },
                        new DataGridTextColumn
                        {
                            Header = "Why it's flagged", Binding = new System.Windows.Data.Binding(nameof(CleanupItem.Reason)), Width = new DataGridLength(2.2, DataGridLengthUnitType.Star)
                        },
                    }
                };

                _grids[category] = grid;

                var tab = new TabItem
                {
                    Header = label,
                    Tag = category,
                    Content = grid
                };
                CategoryTabs.Items.Add(tab);
            }

            CategoryTabs.SelectionChanged += (_, _) => UpdateSelectionSummary();
        }

        // ---------------------------------------------------------------
        // Scan
        // ---------------------------------------------------------------
        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("Scanning model...");
            ScanButton.IsEnabled = false;
            _scanEvent.Raise();
        }

        private void PopulateUi(ScanResult result)
        {
            _lastScan = result;
            ScanButton.IsEnabled = true;

            foreach (var (category, _) in TabOrder)
                _grids[category].ItemsSource = result.Items[category];

            HealthScoreText.Text = result.Health.Score.ToString();
            HealthGradeText.Text = $"Grade {result.Health.Grade}  •  {result.Health.TotalElementCount:N0} elements" +
                                    (result.Health.FileSizeMb > 0 ? $"  •  {result.Health.FileSizeMb} MB" : "");
            HealthNotesList.ItemsSource = result.Health.Notes;

            SetStatus($"Scan complete - {result.TotalCount()} item(s) flagged across {TabOrder.Length} categories.");
            UpdateSelectionSummary();
        }

        // ---------------------------------------------------------------
        // Select all / none (active tab only)
        // ---------------------------------------------------------------
        private void SelectAllButton_Click(object sender, RoutedEventArgs e) => SetSelectionOnActiveTab(true);
        private void SelectNoneButton_Click(object sender, RoutedEventArgs e) => SetSelectionOnActiveTab(false);

        private void SetSelectionOnActiveTab(bool selected)
        {
            if (GetActiveGridItems() is { } items)
            {
                foreach (var item in items) item.IsSelected = selected;
                UpdateSelectionSummary();
            }
        }

        private IEnumerable<CleanupItem>? GetActiveGridItems()
        {
            if (CategoryTabs.SelectedItem is TabItem { Tag: CleanupCategory cat } && _lastScan != null)
                return _lastScan.Items[cat];
            return null;
        }

        private void UpdateSelectionSummary()
        {
            if (_lastScan == null) { SelectionSummaryText.Text = ""; return; }
            int totalSelected = _lastScan.Items.Values.Sum(list => list.Count(i => i.IsSelected));
            int totalFlagged = _lastScan.TotalCount();
            SelectionSummaryText.Text = $"{totalSelected} of {totalFlagged} flagged item(s) selected across all tabs.";
        }

        // ---------------------------------------------------------------
        // Clean selected (across ALL tabs at once - the "single click" cleanup)
        // ---------------------------------------------------------------
        private void CleanSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastScan == null) return;

            var selected = _lastScan.Items.Values.SelectMany(list => list).Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
            {
                SetStatus("Nothing selected to clean.", isError: true);
                return;
            }

            var confirm = MessageBox.Show(
                $"Delete {selected.Count} selected item(s)? This runs as a single undoable transaction " +
                "(Ctrl+Z in Revit will restore everything if needed).",
                "Confirm Cleanup", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            SetStatus("Cleaning up...");
            CleanSelectedButton.IsEnabled = false;
            _cleanHandler.ItemsToClean = selected;
            _cleanEvent.Raise();
        }

        private void OnCleanComplete(PurgeResult result)
        {
            CleanSelectedButton.IsEnabled = true;
            string msg = $"Cleanup done: {result.Deleted} deleted";
            if (result.Failed > 0) msg += $", {result.Failed} failed (still in use or dependent on other elements)";
            SetStatus(msg, isError: result.Failed > 0 && result.Deleted == 0);

            // Re-scan automatically so the dashboard reflects the model's new state.
            SetStatus(msg + " — rescanning...");
            _scanEvent.Raise();
        }

        // ---------------------------------------------------------------
        // Native purge (full-coverage fallback for materials, line patterns, etc.)
        // ---------------------------------------------------------------
        private void RunNativePurgeButton_Click(object sender, RoutedEventArgs e)
        {
            SetStatus("Opening Revit's native Purge Unused dialog...");
            _nativePurgeEvent.Raise();
        }

        private void SetStatus(string text, bool isError = false)
        {
            StatusText.Text = text;
            StatusText.Foreground = isError
                ? System.Windows.Media.Brushes.IndianRed
                : (System.Windows.Media.Brush)FindResource("SubTextBrush");
        }

        // ---------------------------------------------------------------
        // Developer Signature Easter Egg
        // ---------------------------------------------------------------
        private void Signature_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MessageBox.Show(
                "Developed with 🚀 by ENGR.M.SHAHARYAR.\nKeeping Revit models clean and healthy!",
                "About the Developer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}