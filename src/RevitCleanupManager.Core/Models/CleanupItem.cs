using Autodesk.Revit.DB;

namespace RevitCleanupManager.Core.Models
{
    public enum CleanupCategory
    {
        PurgeableFamily, UnplacedView, UnusedFilter, UnusedViewTemplate, UnplacedSchedule,
        UnplacedLegend, RevitLink, CadImportOrLink, InPlaceFamily, UnusedGroup
    }

    public class CleanupItem
    {
        public ElementId Id { get; set; }
        public CleanupCategory Category { get; set; }
        public string Name { get; set; }
        public string TypeOrFamilyName { get; set; }
        public string Details { get; set; }
        public bool IsSelected { get; set; }
        public bool IsSafeToAutoSelect { get; set; } = true;

        public CleanupItem(ElementId id, CleanupCategory category, string name)
        {
            Id = id; Category = category; Name = name;
        }
    }
}
