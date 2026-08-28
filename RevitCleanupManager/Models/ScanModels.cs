using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Autodesk.Revit.DB;

namespace RevitCleanupManager.Models
{
    /// <summary>The category of item found by the scanner. Maps 1:1 to a dashboard tab.</summary>
    public enum CleanupCategory
    {
        PurgeableFamilies,
        UnplacedViews,
        UnusedFilters,
        UnusedViewTemplates,
        UnplacedSchedules,
        UnplacedLegends,
        RevitLinks,
        CadImportsAndLinks,
        UnusedGroups,
        ElementsWithWarnings
    }

    /// <summary>One row in a dashboard grid - a single Revit element flagged for possible cleanup.</summary>
    public class CleanupItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public ElementId Id { get; set; } = ElementId.InvalidElementId;
        public string Name { get; set; } = string.Empty;
        public string TypeOrCategory { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public CleanupCategory Category { get; set; }

        /// <summary>Human-readable reason this item is considered safe to remove, shown as a tooltip.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>False for items that were merely flagged for review but should default to unchecked (extra caution).</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Full result of one scan pass, grouped by category, plus the health report.</summary>
    public class ScanResult
    {
        public Dictionary<CleanupCategory, ObservableCollection<CleanupItem>> Items { get; } = new();
        public ModelHealthReport Health { get; set; } = new();

        public ScanResult()
        {
            foreach (CleanupCategory cat in System.Enum.GetValues(typeof(CleanupCategory)))
                Items[cat] = new ObservableCollection<CleanupItem>();
        }

        public int TotalCount()
        {
            int total = 0;
            foreach (var kv in Items) total += kv.Value.Count;
            return total;
        }
    }

    /// <summary>Aggregate model-health metrics + a 0-100 score used for the dashboard's health gauge.</summary>
    public class ModelHealthReport
    {
        public int WarningCount { get; set; }
        public int CriticalWarningCount { get; set; }
        public int PurgeableFamilyCount { get; set; }
        public int UnplacedViewCount { get; set; }
        public int UnusedFilterCount { get; set; }
        public int UnusedViewTemplateCount { get; set; }
        public int CadImportCount { get; set; }
        public int LinkedCadCount { get; set; }
        public int RevitLinkCount { get; set; }
        public int UnusedGroupCount { get; set; }
        public int TotalElementCount { get; set; }
        public double FileSizeMb { get; set; }

        public int Score { get; set; } = 100;
        public List<string> Notes { get; } = new();

        public string Grade =>
            Score >= 90 ? "A" :
            Score >= 75 ? "B" :
            Score >= 60 ? "C" :
            Score >= 40 ? "D" : "F";
    }
}
