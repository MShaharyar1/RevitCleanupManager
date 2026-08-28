using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitCleanupManager.Models;

namespace RevitCleanupManager.Core
{
    /// <summary>
    /// Turns raw scan counts into a single 0-100 health score plus human-readable notes.
    /// The weighting below is a reasonable starting point - tune the penalty constants
    /// to match your firm's own standards/thresholds.
    /// </summary>
    public static class ModelHealthAnalyzer
    {
        public static ModelHealthReport Build(Document doc, ScanResult scan)
        {
            var report = new ModelHealthReport();

            report.PurgeableFamilyCount = scan.Items[CleanupCategory.PurgeableFamilies].Count;
            report.UnplacedViewCount = scan.Items[CleanupCategory.UnplacedViews].Count;
            report.UnusedFilterCount = scan.Items[CleanupCategory.UnusedFilters].Count;
            report.UnusedViewTemplateCount = scan.Items[CleanupCategory.UnusedViewTemplates].Count;
            report.UnusedGroupCount = scan.Items[CleanupCategory.UnusedGroups].Count;

            report.CadImportCount = scan.Items[CleanupCategory.CadImportsAndLinks]
                .Count(i => i.TypeOrCategory.StartsWith("Imported"));
            report.LinkedCadCount = scan.Items[CleanupCategory.CadImportsAndLinks]
                .Count(i => i.TypeOrCategory.StartsWith("Linked"));
            report.RevitLinkCount = scan.Items[CleanupCategory.RevitLinks].Count;

            try
            {
                report.WarningCount = doc.GetWarnings().Count;
                report.CriticalWarningCount = doc.GetWarnings()
                    .Count(w => w.GetSeverity() == FailureSeverity.Error);
            }
            catch { /* GetWarnings can be slow/unavailable on some detached states - ignore */ }

            try
            {
                report.TotalElementCount = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType().GetElementCount();
            }
            catch { }

            try
            {
                string? path = doc.PathName;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    report.FileSizeMb = Math.Round(new FileInfo(path).Length / 1024.0 / 1024.0, 1);
            }
            catch { }

            // --- Scoring: start at 100, subtract weighted penalties, floor at 0 ---
            int score = 100;

            score -= (int)Penalty(report.PurgeableFamilyCount, perItem: 0.5, cap: 15,
                report.PurgeableFamilyCount > 0, report.Notes,
                $"{report.PurgeableFamilyCount} purgeable family/families found.");

            score -= (int)Penalty(report.UnplacedViewCount, perItem: 0.3, cap: 10,
                report.UnplacedViewCount > 15, report.Notes,
                $"{report.UnplacedViewCount} views are not placed on any sheet.");

            score -= (int)Penalty(report.UnusedFilterCount, perItem: 0.5, cap: 8,
                report.UnusedFilterCount > 5, report.Notes,
                $"{report.UnusedFilterCount} unused filters.");

            score -= (int)Penalty(report.UnusedViewTemplateCount, perItem: 1, cap: 8,
                report.UnusedViewTemplateCount > 3, report.Notes,
                $"{report.UnusedViewTemplateCount} unused view templates.");

            score -= (int)Penalty(report.CadImportCount, perItem: 3, cap: 20,
                report.CadImportCount > 0, report.Notes,
                $"{report.CadImportCount} imported/embedded CAD file(s) - these bloat file size significantly.");

            score -= (int)Penalty(report.UnusedGroupCount, perItem: 1, cap: 6,
                report.UnusedGroupCount > 0, report.Notes,
                $"{report.UnusedGroupCount} unused group type(s).");

            score -= (int)Penalty(report.WarningCount, perItem: 0.1, cap: 15,
                report.WarningCount > 50, report.Notes,
                $"{report.WarningCount} warnings in the model ({report.CriticalWarningCount} critical).");

            if (report.FileSizeMb > 500)
            {
                score -= 10;
                report.Notes.Add($"File size is {report.FileSizeMb} MB - large for smooth performance.");
            }
            else if (report.FileSizeMb > 250)
            {
                score -= 5;
                report.Notes.Add($"File size is {report.FileSizeMb} MB - keep an eye on it.");
            }

            report.Score = Math.Max(0, Math.Min(100, score));

            if (report.Notes.Count == 0)
                report.Notes.Add("No major issues found - model looks clean.");

            return report;
        }

        private static double Penalty(int count, double perItem, double cap, bool noteWorthy,
            System.Collections.Generic.List<string> notes, string note)
        {
            if (count <= 0) return 0;
            if (noteWorthy) notes.Add(note);
            return Math.Min(count * perItem, cap);
        }
    }
}
