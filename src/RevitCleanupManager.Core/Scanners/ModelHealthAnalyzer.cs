using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitCleanupManager.Core.Models;

namespace RevitCleanupManager.Core.Scanners
{
    public class ModelHealthAnalyzer
    {
        public ModelHealthReport Analyze(Document doc, ScanResult scanResult, string modelFilePath)
        {
            var report = new ModelHealthReport();
            report.WarningCount = doc.GetWarnings().Count;
            report.UnplacedViewCount = scanResult.Get(CleanupCategory.UnplacedView).Count;
            report.InPlaceFamilyCount = scanResult.Get(CleanupCategory.InPlaceFamily).Count;
            report.DwgImportCount = scanResult.Get(CleanupCategory.CadImportOrLink).Count;
            report.GroupCount = new FilteredElementCollector(doc).OfClass(typeof(GroupType)).GetElementCount();
            report.UnusedFilterCount = scanResult.Get(CleanupCategory.UnusedFilter).Count;

            if (!string.IsNullOrEmpty(modelFilePath) && File.Exists(modelFilePath))
                report.FileSizeBytes = new FileInfo(modelFilePath).Length;

            Add(report, "Warnings", report.WarningCount.ToString(), RateWarnings(report.WarningCount), "Resolve warnings by type via Manage > Review Warnings.");
            Add(report, "File Size", FormatBytes(report.FileSizeBytes), RateFileSize(report.FileSizeBytes), "Purge unused, remove in-place families, compact the file.");
            Add(report, "Unplaced Views", report.UnplacedViewCount.ToString(), RateCount(report.UnplacedViewCount, 20, 50), "Delete or place working/coordination views.");
            Add(report, "In-Place Families", report.InPlaceFamilyCount.ToString(), RateCount(report.InPlaceFamilyCount, 5, 15), "Convert recurring in-place families to loadable families.");
            Add(report, "CAD Imports/Links", report.DwgImportCount.ToString(), RateCount(report.DwgImportCount, 5, 15), "Prefer links over imports; remove CAD no longer needed.");
            Add(report, "Unused Filters", report.UnusedFilterCount.ToString(), RateCount(report.UnusedFilterCount, 20, 50), "Purge filters not applied to any view.");
            Add(report, "Groups", report.GroupCount.ToString(), RateCount(report.GroupCount, 30, 80), "Excessive/nested groups slow down regeneration.");

            report.OverallScore = ComputeOverallScore(report);
            report.OverallGrade = ScoreToGrade(report.OverallScore);
            return report;
        }

        private static void Add(ModelHealthReport r, string name, string raw, HealthRating rating, string rec)
            => r.Metrics.Add(new HealthMetric { Name = name, RawValue = raw, Rating = rating, Recommendation = rec });

        private static HealthRating RateWarnings(int c) => c < 100 ? HealthRating.Good : c < 500 ? HealthRating.Fair : c < 1500 ? HealthRating.Poor : HealthRating.Critical;
        private static HealthRating RateFileSize(long bytes) { var mb = bytes / (1024.0 * 1024.0); return mb < 150 ? HealthRating.Good : mb < 300 ? HealthRating.Fair : mb < 500 ? HealthRating.Poor : HealthRating.Critical; }
        private static HealthRating RateCount(int c, int fair, int poor) => c < fair / 2 ? HealthRating.Good : c < fair ? HealthRating.Fair : c < poor ? HealthRating.Poor : HealthRating.Critical;

        private static int ComputeOverallScore(ModelHealthReport report)
        {
            int total = 0, weightSum = 0;
            var weights = new[] { 30, 25, 10, 10, 10, 10, 5 };
            for (int i = 0; i < report.Metrics.Count; i++)
            {
                int w = i < weights.Length ? weights[i] : 5;
                int score = report.Metrics[i].Rating switch { HealthRating.Good => 100, HealthRating.Fair => 70, HealthRating.Poor => 40, HealthRating.Critical => 10, _ => 50 };
                total += score * w; weightSum += w;
            }
            return weightSum == 0 ? 0 : total / weightSum;
        }

        private static string ScoreToGrade(int s) => s >= 90 ? "A" : s >= 75 ? "B" : s >= 60 ? "C" : s >= 40 ? "D" : "F";
        private static string FormatBytes(long bytes) { double mb = bytes / (1024.0 * 1024.0); return mb >= 1024 ? $"{mb / 1024.0:0.0} GB" : $"{mb:0.0} MB"; }
    }
}
