using System.Collections.Generic;
using System.Linq;

namespace RevitCleanupManager.Core.Models
{
    public class ScanResult
    {
        public Dictionary<CleanupCategory, List<CleanupItem>> ItemsByCategory { get; } = new Dictionary<CleanupCategory, List<CleanupItem>>();
        public void Add(CleanupCategory category, List<CleanupItem> items) => ItemsByCategory[category] = items;
        public List<CleanupItem> Get(CleanupCategory category) => ItemsByCategory.TryGetValue(category, out var l) ? l : new List<CleanupItem>();
        public IEnumerable<CleanupItem> AllItems => ItemsByCategory.Values.SelectMany(x => x);
        public int TotalCount => AllItems.Count();
    }

    public class HealthMetric
    {
        public string Name { get; set; }
        public string RawValue { get; set; }
        public HealthRating Rating { get; set; }
        public string Recommendation { get; set; }
    }

    public enum HealthRating { Good, Fair, Poor, Critical }

    public class ModelHealthReport
    {
        public List<HealthMetric> Metrics { get; } = new List<HealthMetric>();
        public int OverallScore { get; set; }
        public string OverallGrade { get; set; }
        public long FileSizeBytes { get; set; }
        public int WarningCount { get; set; }
        public int UnplacedViewCount { get; set; }
        public int InPlaceFamilyCount { get; set; }
        public int DwgImportCount { get; set; }
        public int UnusedFilterCount { get; set; }
        public int GroupCount { get; set; }
    }
}
