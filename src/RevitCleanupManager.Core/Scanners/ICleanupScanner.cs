using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitCleanupManager.Core.Models;

namespace RevitCleanupManager.Core.Scanners
{
    public interface ICleanupScanner
    {
        CleanupCategory Category { get; }
        List<CleanupItem> Scan(Document doc);
    }
}
