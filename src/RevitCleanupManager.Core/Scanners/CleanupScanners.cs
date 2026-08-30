using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitCleanupManager.Core.Models;

namespace RevitCleanupManager.Core.Scanners
{
    /// <summary>
    /// Uses Document.GetUnusedElements (Revit 2022+), Revit's own API replication of the
    /// "Purge Unused" command, filtered down to families/types for the dashboard grid.
    /// </summary>
    public class PurgeableFamilyScanner : ICleanupScanner
    {
        public CleanupCategory Category => CleanupCategory.PurgeableFamily;
        public List<CleanupItem> Scan(Document doc)
        {
            var results = new List<CleanupItem>();
            ICollection<ElementId> unusedIds;
            try { unusedIds = doc.GetUnusedElements(new HashSet<ElementId>()); }
            catch (Exception) { return results; }

            foreach (var id in unusedIds)
            {
                var el = doc.GetElement(id);
                if (el == null) continue;
                if (el is Family family)
                    results.Add(new CleanupItem(id, Category, family.Name) { TypeOrFamilyName = "Family", Details = $"Category: {family.FamilyCategory?.Name ?? "Unknown"}" });
                else if (el is ElementType elType && !(el is ViewFamilyType))
                    results.Add(new CleanupItem(id, Category, elType.Name) { TypeOrFamilyName = elType.FamilyName, Details = $"Type in category: {elType.Category?.Name ?? "Unknown"}" });
            }
            return results;
        }
    }

    public class UnplacedViewScanner : ICleanupScanner
    {
        public CleanupCategory Category => CleanupCategory.UnplacedView;
        public List<CleanupItem> Scan(Document doc)
        {
            var results = new List<CleanupItem>();
            var placed = new HashSet<ElementId>(new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Select(vp => vp.ViewId));
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType != ViewType.Schedule && v.ViewType != ViewType.Legend
                    && v.ViewType != ViewType.DrawingSheet && v.ViewType != ViewType.SystemBrowser
                    && v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.Internal && v.ViewType != ViewType.Undefined);
            foreach (var v in views)
            {
                if (placed.Contains(v.Id)) continue;
                results.Add(new CleanupItem(v.Id, Category, v.Name) { TypeOrFamilyName = v.ViewType.ToString(), Details = $"Not placed on any sheet. Type: {v.ViewType}" });
            }
            return results;
        }
    }

    public class UnplacedScheduleScanner : ICleanupScanner
    {
        public CleanupCategory Category => CleanupCategory.UnplacedSchedule;
        public List<CleanupItem> Scan(Document doc)
        {
            var results = new List<CleanupItem>();
            var placed = new HashSet<ElementId>(new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>().Select(s => s.ScheduleId));
            var schedules = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule);
            foreach (var s in schedules)
            {
                if (placed.Contains(s.Id)) continue;
                results.Add(new CleanupItem(s.Id, Category, s.Name) { TypeOrFamilyName = "Schedule", Details = "Not placed on any sheet" });
            }
            return results;
        }
    }

    public class UnplacedLegendScanner : ICleanupScanner
    {
        public CleanupCategory Category => CleanupCategory.UnplacedLegend;
        public List<CleanupItem> Scan(Document doc)
        {
            var results = new List<CleanupItem>();
            var placed = new HashSet<ElementId>(new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Select(vp => vp.ViewId));
            var legends = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate);
            foreach (var v in legends)
            {
                if (placed.Contains(v.Id)) continue;
                results.Add(new CleanupItem(v.Id, Category, v.Name) { TypeOrFamilyName = "Legend", Details = "Not placed on any sheet", IsSafeToAutoSelect = false });
            }
            return results;
        }
    }

    public class UnusedFilterScanner : ICleanupScanner
    {
        public CleanupCategory Category => CleanupCategory.UnusedFilter;
        public List<CleanupItem> Scan(Document doc)
        {
            var results = new List<CleanupItem>();
            var allFilters = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>().ToList();
            var used = new HashSet<ElementId>();
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => v.AreGraphicsOverridesAllowed() || v.IsTemplate);
            foreach (var v in views)
            {
                ICollection<ElementId> ids;
                try { ids = v.GetFilters(); } catch { continue; }
                foreach (var id in ids) used.Add(id);
            }
            foreach (var f in allFilters)
            {
                if (used.Contains(f.Id)) continue;
                results.Add(new CleanupItem(f.Id, Category, f.Name) { TypeOrFamilyName = "Filter", Details = "Not applied to any view or view template" });
            }
            return results;
        }
    }

    public class UnusedViewTemplateScanner : ICleanupScanner
    {
        public CleanupCategory Category => CleanupCategory.UnusedViewTemplate;
        public List<CleanupItem> Scan(Document doc)
        {
            var results = new List<CleanupItem>();
            var templates = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => v.IsTemplate).ToList();
            var assigned = new HashSet<ElementId>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewTemplateId != ElementId.InvalidElementId).Select(v => v.ViewTemplateId));
            foreach (var t in templates)
            {
                if (assigned.Contains(t.Id)) continue;
                results.Add(new CleanupItem(t.Id, Category, t.Name) { TypeOrFamilyName = "View Template", Details = "Not assigned to any view (may still be a default template -- verify)", IsSafeToAutoSelect = false });
            }
            return results;
        }
    }

    public class RevitLinkScanner : ICleanupScanner
    {
        public CleanupCategory Category => CleanupCategory.RevitLink;
        public List<CleanupItem> Scan(Document doc)
        {
            var results = new List<CleanupItem>();
            var linkTypes = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>();
            foreach (var lt in linkTypes)
            {
                var instance = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().FirstOrDefault(li => li.GetTypeId() == lt.Id);
                results.Add(new CleanupItem(lt.Id, Category, lt.Name) { TypeOrFamilyName = "Revit Link", Details = $"Status: {lt.GetLinkedFileStatus()}, Placed: {instance != null}", IsSafeToAutoSelect = false });
            }
            return results;
        }
    }

    public class CadImportScanner : ICleanupScanner
    {
        public CleanupCategory Category => CleanupCategory.CadImportOrLink;
        public List<CleanupItem> Scan(Document doc)
        {
            var results = new List<CleanupItem>();
            var links = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>();
            foreach (var link in links)
            {
                var typeElem = doc.GetElement(link.GetTypeId());
                string name = typeElem?.Name ?? link.Category?.Name ?? $"Import {link.Id}";
                var ownerView = link.OwnerViewId != ElementId.InvalidElementId ? doc.GetElement(link.OwnerViewId) as View : null;
                results.Add(new CleanupItem(link.Id, Category, name)
                {
                    TypeOrFamilyName = link.IsLinked ? "Linked CAD" : "Imported CAD (exploded/embedded)",
                    Details = ownerView != null ? $"Scoped to view: {ownerView.Name}" : "Visible in all model views",
                    IsSafeToAutoSelect = false
                });
            }
            return results;
        }
    }

    public class InPlaceFamilyScanner : ICleanupScanner
    {
        public CleanupCategory Category => CleanupCategory.InPlaceFamily;
        public List<CleanupItem> Scan(Document doc)
        {
            var results = new List<CleanupItem>();
            var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().Where(f => f.IsInPlace);
            foreach (var f in families)
                results.Add(new CleanupItem(f.Id, Category, f.Name) { TypeOrFamilyName = "In-Place Family", Details = $"Category: {f.FamilyCategory?.Name ?? "Unknown"} -- review before removing", IsSafeToAutoSelect = false });
            return results;
        }
    }

    public class UnusedGroupScanner : ICleanupScanner
    {
        public CleanupCategory Category => CleanupCategory.UnusedGroup;
        public List<CleanupItem> Scan(Document doc)
        {
            var results = new List<CleanupItem>();
            var placed = new HashSet<ElementId>(new FilteredElementCollector(doc).OfClass(typeof(Group)).Cast<Group>().Select(g => g.GroupType.Id));
            var groupTypes = new FilteredElementCollector(doc).OfClass(typeof(GroupType)).Cast<GroupType>();
            foreach (var gt in groupTypes)
            {
                if (placed.Contains(gt.Id)) continue;
                results.Add(new CleanupItem(gt.Id, Category, gt.Name) { TypeOrFamilyName = "Group Type", Details = "No placed instances in the model" });
            }
            return results;
        }
    }

    public static class ScannerFactory
    {
        public static List<ICleanupScanner> GetAllScanners() => new List<ICleanupScanner>
        {
            new PurgeableFamilyScanner(), new UnplacedViewScanner(), new UnplacedScheduleScanner(),
            new UnplacedLegendScanner(), new UnusedFilterScanner(), new UnusedViewTemplateScanner(),
            new RevitLinkScanner(), new CadImportScanner(), new InPlaceFamilyScanner(), new UnusedGroupScanner(),
        };

        public static ScanResult RunAll(Document doc, Action<string> onProgress = null)
        {
            var result = new ScanResult();
            foreach (var scanner in GetAllScanners())
            {
                try { onProgress?.Invoke($"Scanning: {scanner.Category}..."); result.Add(scanner.Category, scanner.Scan(doc)); }
                catch (Exception ex) { onProgress?.Invoke($"Scanner {scanner.Category} failed: {ex.Message}"); result.Add(scanner.Category, new List<CleanupItem>()); }
            }
            return result;
        }
    }
}
