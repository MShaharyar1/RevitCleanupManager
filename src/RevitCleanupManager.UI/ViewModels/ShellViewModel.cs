using Autodesk.Revit.DB;

namespace RevitCleanupManager.UI.ViewModels
{
    /// <summary>Top-level view model for the dashboard window: hosts the Cleanup tab and the QA/QC tab.</summary>
    public class ShellViewModel
    {
        public DashboardViewModel Cleanup { get; }
        public QaDashboardViewModel QaQc { get; }

        public ShellViewModel(Document doc)
        {
            Cleanup = new DashboardViewModel(doc);
            QaQc = new QaDashboardViewModel(doc);
        }
    }
}
