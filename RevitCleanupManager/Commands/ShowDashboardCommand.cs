using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCleanupManager.UI;

namespace RevitCleanupManager.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ShowDashboardCommand : IExternalCommand
    {
        // Keep a single instance so re-clicking the ribbon button brings the
        // existing window to front instead of stacking duplicates.
        private static DashboardWindow? _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;

            if (uiDoc == null)
            {
                message = "Open a Revit project (not just the home screen) before running Cleanup Manager.";
                return Result.Failed;
            }

            try
            {
                if (_window == null || !_window.IsLoaded)
                {
                    _window = new DashboardWindow(uiApp);
                    _window.Closed += (_, _) => _window = null;
                    _window.Show();
                }
                else
                {
                    _window.Activate();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
