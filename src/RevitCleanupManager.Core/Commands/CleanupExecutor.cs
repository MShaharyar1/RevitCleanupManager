using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitCleanupManager.Core.Models;

namespace RevitCleanupManager.Core.Commands
{
    public class CleanupResultLine { public string Name { get; set; } public bool Success { get; set; } public string Message { get; set; } }
    public class CleanupRunReport
    {
        public List<CleanupResultLine> Lines { get; } = new List<CleanupResultLine>();
        public int SuccessCount => Lines.Count(l => l.Success);
        public int FailureCount => Lines.Count(l => !l.Success);
    }

    public class CleanupExecutor
    {
        public CleanupRunReport DeleteSelected(Document doc, IEnumerable<CleanupItem> items)
        {
            var report = new CleanupRunReport();
            var selected = items.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return report;

            using (var group = new TransactionGroup(doc, "Revit Cleanup Manager - Batch Cleanup"))
            {
                group.Start();
                foreach (var item in selected)
                {
                    using (var t = new Transaction(doc, $"Delete: {item.Name}"))
                    {
                        try
                        {
                            t.Start();
                            var el = doc.GetElement(item.Id);
                            if (el == null)
                            {
                                t.RollBack();
                                report.Lines.Add(new CleanupResultLine { Name = item.Name, Success = true, Message = "Already removed (dependency of a prior deletion)." });
                                continue;
                            }
                            doc.Delete(item.Id);
                            t.Commit();
                            report.Lines.Add(new CleanupResultLine { Name = item.Name, Success = true, Message = "Deleted" });
                        }
                        catch (Exception ex)
                        {
                            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                            report.Lines.Add(new CleanupResultLine { Name = item.Name, Success = false, Message = ex.Message });
                        }
                    }
                }
                group.Assimilate();
            }
            return report;
        }
    }
}
