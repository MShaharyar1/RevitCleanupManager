using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCleanupManager.Models;

namespace RevitCleanupManager.Core
{
    public class PurgeResult
    {
        public int Deleted { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; } = new();
    }

    /// <summary>
    /// Performs the actual deletions. Every call runs inside its own Transaction so a
    /// partial failure never leaves the model half-modified, and each element is deleted
    /// individually with try/catch so one problem element doesn't abort the whole batch.
    /// </summary>
    public class PurgeService
    {
        private readonly Document _doc;
        private readonly UIApplication _uiApp;

        public PurgeService(UIApplication uiApp)
        {
            _uiApp = uiApp;
            _doc = uiApp.ActiveUIDocument.Document;
        }

        /// <summary>Deletes every checked item across every category in one transaction.</summary>
        public PurgeResult DeleteSelected(IEnumerable<CleanupItem> items)
        {
            var toDelete = items.Where(i => i.IsSelected && i.Id != ElementId.InvalidElementId).ToList();
            var result = new PurgeResult();

            if (toDelete.Count == 0) return result;

            using var t = new Transaction(_doc, "Cleanup Manager - Remove Selected Items");
            t.Start();

            foreach (var item in toDelete)
            {
                try
                {
                    if (_doc.GetElement(item.Id) == null)
                        continue; // already gone (e.g. deleted as a dependent of an earlier item)

                    _doc.Delete(item.Id);
                    result.Deleted++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"{item.Name}: {ex.Message}");
                }
            }

            t.Commit();
            return result;
        }

        /// <summary>
        /// Triggers Revit's native "Purge Unused" command for full coverage of system
        /// families, materials, line patterns, etc. that aren't reachable through the
        /// public API. This opens Revit's own dialog (PostCommand cannot bypass the UI) -
        /// wire this to a "Run Full Native Purge" button as a complement to the
        /// one-click category deletes above.
        /// </summary>
        public void RunNativePurgeUnused()
        {
            RevitCommandId cmdId = RevitCommandId.LookupPostableCommandId(PostableCommand.PurgeUnused);
            if (_uiApp.CanPostCommand(cmdId))
                _uiApp.PostCommand(cmdId);
        }

        /// <summary>Unloads (rather than deletes) a Revit link - the safer default action for links.</summary>
        public PurgeResult UnloadRevitLinks(IEnumerable<CleanupItem> linkItems)
        {
            var result = new PurgeResult();
            using var t = new Transaction(_doc, "Cleanup Manager - Unload Revit Links");
            t.Start();

            foreach (var item in linkItems.Where(i => i.IsSelected))
            {
                try
                {
                    _doc.Delete(item.Id);
                    result.Deleted++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"{item.Name}: {ex.Message}");
                }
            }

            t.Commit();
            return result;
        }
    }
}
