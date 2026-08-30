using System;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace RevitCleanupManager.Addin
{
    public class App : IExternalApplication
    {
        private const string TabName = "Cleanup Tools";
        private const string PanelName = "Cleanup Manager";

        public Result OnStartup(UIControlledApplication application)
        {
            try { application.CreateRibbonTab(TabName); } catch { /* tab already exists */ }

            var panel = application.CreateRibbonPanel(TabName, PanelName);
            var assemblyPath = Assembly.GetExecutingAssembly().Location;

            var buttonData = new PushButtonData(
                "RevitCleanupManagerDashboard",
                "Cleanup" + Environment.NewLine + "Manager",
                assemblyPath,
                "RevitCleanupManager.Addin.ShowDashboardCommand")
            {
                ToolTip = "Scan for purgeable families, unplaced views, unused filters/templates, links, and imports; " +
                          "run model QA/QC (naming, missing parameters, duplicate marks); and round-trip data to Excel -- all from one dashboard."
            };

            var button = panel.AddItem(buttonData) as PushButton;

            try
            {
                var iconPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(assemblyPath) ?? "", "Resources", "icon32.png");
                if (System.IO.File.Exists(iconPath))
                    button.LargeImage = new BitmapImage(new Uri(iconPath));
            }
            catch { /* icon is optional */ }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
    }
}
