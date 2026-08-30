using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using RevitCleanupManager.UI;

namespace RevitCleanupManager.Addin
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ShowDashboardCommand : IExternalCommand
    {
        private static MainWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active document. Open a Revit model first.";
                return Result.Failed;
            }

            try
            {
                if (_window != null && _window.IsLoaded)
                {
                    _window.Activate();
                    return Result.Succeeded;
                }

                _window = new MainWindow(uiDoc.Document);
                new System.Windows.Interop.WindowInteropHelper(_window) { Owner = commandData.Application.MainWindowHandle };
                _window.Show();
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
