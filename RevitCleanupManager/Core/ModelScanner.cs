using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitCleanupManager.Models;

namespace RevitCleanupManager.Core
{
    /// <summary>
    /// Read-only scan of the active document. Every method here only *reads* the model -
    /// nothing is deleted here. Deletion lives in PurgeService so scanning is always safe
    /// to re-run.
    /// </summary>
    public class ModelScanner
    {
        private readonly Document _doc;

        public ModelScanner(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public ScanResult ScanAll()
        {
            var result = new ScanResult();

            // Views on sheets - shared lookup used by views/schedules/legends scans.
            HashSet<ElementId> placedViewIds = GetPlacedViewIds();

            ScanUnplacedViews(result, placedViewIds);
            ScanUnusedFilters(result);
            ScanUnusedViewTemplates(result);
            ScanUnplacedSchedules(result, placedViewIds);
            ScanUnplacedLegends(result, placedViewIds);
            ScanRevitLinks(result);
            ScanCadImportsAndLinks(result);
            ScanUnusedGroups(result);
            ScanPurgeableFamilies(result);
            ScanWarningElements(result);

            result.Health = ModelHealthAnalyzer.Build(_doc, result);
            return result;
        }

        // ---------------------------------------------------------------
        // Shared helper: every ElementId a Viewport (sheet placement) points at,
        // covering ordinary views, schedules and legends alike.
        // ---------------------------------------------------------------
        private HashSet<ElementId> GetPlacedViewIds()
        {
            var placed = new HashSet<ElementId>();

            foreach (Viewport vp in new FilteredElementCollector(_doc)
                         .OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                placed.Add(vp.ViewId);
            }

            // Schedules placed on sheets are ScheduleSheetInstance, not Viewport.
            foreach (ScheduleSheetInstance ssi in new FilteredElementCollector(_doc)
                         .OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
            {
                placed.Add(ssi.ScheduleId);
            }

            return placed;
        }

        // ---------------------------------------------------------------
        // 1. Unplaced views (plans, sections, elevations, 3D, drafting)
        // ---------------------------------------------------------------
        private void ScanUnplacedViews(ScanResult result, HashSet<ElementId> placedViewIds)
        {
            var candidates = new FilteredElementCollector(_doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .Where(v => v.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.Elevation
                    or ViewType.Section or ViewType.ThreeD or ViewType.Detail or ViewType.DraftingView
                    or ViewType.EngineeringPlan or ViewType.AreaPlan or ViewType.Walkthrough
                    or ViewType.Rendering)
                .Where(v => !placedViewIds.Contains(v.Id))
                .Where(v => !v.IsAssemblyView) // assembly views managed by their assembly, skip
                ;

            foreach (View v in candidates)
            {
                result.Items[CleanupCategory.UnplacedViews].Add(new CleanupItem
                {
                    Id = v.Id,
                    Name = v.Name,
                    TypeOrCategory = v.ViewType.ToString(),
                    Detail = v.get_Parameter(BuiltInParameter.VIEW_PHASE)?.AsValueString() ?? "",
                    Category = CleanupCategory.UnplacedViews,
                    Reason = "Not placed on any sheet."
                });
            }
        }

        // ---------------------------------------------------------------
        // 2. Unused filters (ParameterFilterElement not applied to any view)
        // ---------------------------------------------------------------
        private void ScanUnusedFilters(ScanResult result)
        {
            var usedFilterIds = new HashSet<ElementId>();

            foreach (View v in new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>())
            {
                if (!v.AreGraphicsOverridesAllowed()) continue;
                try
                {
                    foreach (ElementId fid in v.GetFilters())
                        usedFilterIds.Add(fid);
                }
                catch
                {
                    // Some view types (e.g. schedules) throw on GetFilters - safe to skip.
                }
            }

            var allFilters = new FilteredElementCollector(_doc)
                .OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>();

            foreach (var f in allFilters.Where(f => !usedFilterIds.Contains(f.Id)))
            {
                result.Items[CleanupCategory.UnusedFilters].Add(new CleanupItem
                {
                    Id = f.Id,
                    Name = f.Name,
                    TypeOrCategory = "Filter",
                    Category = CleanupCategory.UnusedFilters,
                    Reason = "Not applied (visibility or override) in any view."
                });
            }
        }

        // ---------------------------------------------------------------
        // 3. Unused view templates
        // ---------------------------------------------------------------
        private void ScanUnusedViewTemplates(ScanResult result)
        {
            var allViews = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>().ToList();
            var templates = allViews.Where(v => v.IsTemplate).ToList();
            var nonTemplateViews = allViews.Where(v => !v.IsTemplate).ToList();

            var usedTemplateIds = new HashSet<ElementId>(
                nonTemplateViews
                    .Select(v => v.ViewTemplateId)
                    .Where(id => id != ElementId.InvalidElementId));

            // NOTE: a template can also be set as a discipline's "default view template"
            // (Manage > Default View Templates) without being applied to any view yet.
            // That setting isn't exposed as a simple ElementId lookup in the public API,
            // so it isn't excluded here - double-check Manage > Default View Templates
            // before bulk-deleting templates this tool flags as unused.
            foreach (var t in templates.Where(t => !usedTemplateIds.Contains(t.Id)))
            {
                result.Items[CleanupCategory.UnusedViewTemplates].Add(new CleanupItem
                {
                    Id = t.Id,
                    Name = t.Name,
                    TypeOrCategory = t.ViewType.ToString(),
                    Category = CleanupCategory.UnusedViewTemplates,
                    Reason = "Not applied to any view and not a discipline default template."
                });
            }
        }

        // ---------------------------------------------------------------
        // 4. Schedules not placed on any sheet
        // ---------------------------------------------------------------
        private void ScanUnplacedSchedules(ScanResult result, HashSet<ElementId> placedViewIds)
        {
            var schedules = new FilteredElementCollector(_doc)
                .OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .Where(s => !s.IsTemplate)
                // Skip internal schedules Revit creates for itself (e.g. "<Revision Schedule>").
                .Where(s => !s.Name.StartsWith("<"))
                .Where(s => !placedViewIds.Contains(s.Id));

            foreach (var s in schedules)
            {
                result.Items[CleanupCategory.UnplacedSchedules].Add(new CleanupItem
                {
                    Id = s.Id,
                    Name = s.Name,
                    TypeOrCategory = "Schedule",
                    Category = CleanupCategory.UnplacedSchedules,
                    Reason = "Not placed on any sheet - confirm it isn't used for export/takeoff before deleting.",
                    IsSelected = false // extra caution: schedules are often used unplaced, default unchecked
                });
            }
        }

        // ---------------------------------------------------------------
        // 5. Legends not placed on any sheet
        // ---------------------------------------------------------------
        private void ScanUnplacedLegends(ScanResult result, HashSet<ElementId> placedViewIds)
        {
            var legends = new FilteredElementCollector(_doc)
                .OfClass(typeof(View)).Cast<View>()
                .Where(v => v.ViewType == ViewType.Legend)
                .Where(v => !placedViewIds.Contains(v.Id));

            foreach (var l in legends)
            {
                result.Items[CleanupCategory.UnplacedLegends].Add(new CleanupItem
                {
                    Id = l.Id,
                    Name = l.Name,
                    TypeOrCategory = "Legend",
                    Category = CleanupCategory.UnplacedLegends,
                    Reason = "Not placed on any sheet."
                });
            }
        }

        // ---------------------------------------------------------------
        // 6. Revit links (for review - deletion defaults to unchecked, high impact)
        // ---------------------------------------------------------------
        private void ScanRevitLinks(ScanResult result)
        {
            foreach (RevitLinkType lt in new FilteredElementCollector(_doc)
                         .OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
            {
                bool loaded = RevitLinkType.IsLoaded(_doc, lt.Id);
                var instance = new FilteredElementCollector(_doc)
                    .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                    .FirstOrDefault(i => i.GetTypeId() == lt.Id);

                result.Items[CleanupCategory.RevitLinks].Add(new CleanupItem
                {
                    Id = lt.Id,
                    Name = lt.Name,
                    TypeOrCategory = "RVT Link",
                    Detail = loaded ? "Loaded" : "Unloaded / not found",
                    Category = CleanupCategory.RevitLinks,
                    Reason = loaded
                        ? "Currently loaded - review before removing, this affects coordination."
                        : "Unloaded or the linked file can't be found - candidate for cleanup.",
                    IsSelected = false
                });
            }
        }

        // ---------------------------------------------------------------
        // 7. CAD imports (embedded) and CAD links
        // ---------------------------------------------------------------
        private void ScanCadImportsAndLinks(ScanResult result)
        {
            foreach (ImportInstance ii in new FilteredElementCollector(_doc)
                         .OfClass(typeof(ImportInstance)).Cast<ImportInstance>())
            {
                bool isLinked = ii.IsLinked;
                string viewName = "Model (all views)";
                if (!ii.ViewSpecific)
                {
                    viewName = "Model (all views)";
                }
                else
                {
                    var view = _doc.GetElement(ii.OwnerViewId) as View;
                    viewName = view?.Name ?? "Unknown view";
                }

                result.Items[CleanupCategory.CadImportsAndLinks].Add(new CleanupItem
                {
                    Id = ii.Id,
                    Name = ii.Category?.Name ?? "CAD Import",
                    TypeOrCategory = isLinked ? "Linked CAD" : "Imported CAD (exploded/embedded)",
                    Detail = viewName,
                    Category = CleanupCategory.CadImportsAndLinks,
                    Reason = isLinked
                        ? "Linked CAD file - review before removing."
                        : "Imported/embedded CAD geometry bloats file size and usually should be deleted " +
                          "or replaced with a link.",
                    IsSelected = !isLinked // imported/embedded CAD defaults checked, links default unchecked
                });
            }
        }

        // ---------------------------------------------------------------
        // 8. Unused groups (model + detail groups with zero placed instances)
        // ---------------------------------------------------------------
        private void ScanUnusedGroups(ScanResult result)
        {
            foreach (GroupType gt in new FilteredElementCollector(_doc)
                         .OfClass(typeof(GroupType)).Cast<GroupType>())
            {
                int placedInstances = gt.Groups?.Size ?? 0;
                if (placedInstances > 0) continue;

                result.Items[CleanupCategory.UnusedGroups].Add(new CleanupItem
                {
                    Id = gt.Id,
                    Name = gt.Name,
                    TypeOrCategory = "Group Type",
                    Category = CleanupCategory.UnusedGroups,
                    Reason = "No placed instances of this group remain in the model."
                });
            }
        }

        // ---------------------------------------------------------------
        // 9. Purgeable families (loadable families with zero used types)
        //    NOTE: Revit's native "Purge Unused" also cleans system families,
        //    materials, line patterns etc. via an internal algorithm that is
        //    NOT exposed through the public API. Use PurgeService.RunNativePurge()
        //    (PostCommand) to trigger that dialog for full coverage.
        // ---------------------------------------------------------------
        private void ScanPurgeableFamilies(ScanResult result)
        {
            var usedTypeIds = new HashSet<ElementId>(
                new FilteredElementCollector(_doc)
                    .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .Select(fi => fi.GetTypeId()));

            // Types also used as nested-family definitions, or referenced by schedules/
            // tags/keynotes, are intentionally NOT excluded here - this is a first-pass
            // heuristic. Always review before deleting.
            var families = new FilteredElementCollector(_doc)
                .OfClass(typeof(Family)).Cast<Family>()
                .Where(f => f.IsEditable); // skip in-place/system pseudo-families we can't purge

            foreach (Family fam in families)
            {
                var symbolIds = fam.GetFamilySymbolIds();
                if (symbolIds.Count == 0) continue;

                bool anyUsed = symbolIds.Any(usedTypeIds.Contains);
                if (anyUsed) continue;

                result.Items[CleanupCategory.PurgeableFamilies].Add(new CleanupItem
                {
                    Id = fam.Id,
                    Name = fam.Name,
                    TypeOrCategory = fam.FamilyCategory?.Name ?? "Family",
                    Detail = $"{symbolIds.Count} type(s), 0 placed",
                    Category = CleanupCategory.PurgeableFamilies,
                    Reason = "None of this family's types are placed anywhere in the model."
                });
            }
        }

        // ---------------------------------------------------------------
        // 10. Elements currently flagged in Revit's warning list (info only,
        //     surfaced for the health score - not a one-click delete category).
        // ---------------------------------------------------------------
        private void ScanWarningElements(ScanResult result)
        {
            IList<FailureMessage> warnings;
            try
            {
                warnings = _doc.GetWarnings();
            }
            catch
            {
                return;
            }

            foreach (var w in warnings.Take(500)) // cap for UI performance on huge models
            {
                var ids = w.GetFailingElements();
                string firstName = "";

                // FIXED: Replaced ids[0] with ids.First()
                if (ids.Count > 0 && _doc.GetElement(ids.First()) is Element el)
                    firstName = el.Name;

                result.Items[CleanupCategory.ElementsWithWarnings].Add(new CleanupItem
                {
                    // FIXED: Replaced ids[0] with ids.First()
                    Id = ids.Count > 0 ? ids.First() : ElementId.InvalidElementId,
                    Name = firstName,
                    TypeOrCategory = w.GetSeverity().ToString(),
                    Detail = $"{ids.Count} element(s) affected",
                    Category = CleanupCategory.ElementsWithWarnings,
                    Reason = w.GetDescriptionText(),
                    IsSelected = false
                });
            }
        }
    }
}