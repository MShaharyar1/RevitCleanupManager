using System;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace RevitCleanupManager
{
    /// <summary>
    /// Entry point loaded by Revit at startup (registered in RevitCleanupManager.addin).
    /// Creates the ribbon tab/panel/button that opens the cleanup dashboard.
    /// </summary>
    public class App : IExternalApplication
    {
        private const string TabName = "Cleanup Tools";
        private const string PanelName = "Model Cleanup";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch
            {
                // Tab already exists (e.g. another add-in on the same tab) - ignore.
            }

            RibbonPanel panel = application.CreateRibbonPanel(TabName, PanelName);

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            var buttonData = new PushButtonData(
                "RevitCleanupManagerDashboard",
                "Cleanup" + Environment.NewLine + "Manager",
                assemblyPath,
                "RevitCleanupManager.Commands.ShowDashboardCommand")
            {
                ToolTip = "Scan the model for purgeable families, unplaced views, unused filters, " +
                          "templates, schedules, legends, links and imports - and clean them up from one dashboard.",
                LongDescription = "Opens the Revit Cleanup Manager dashboard. Scan the active model, " +
                                   "review everything that can be safely removed, and clean it up in a single click."
            };

            PushButton button = panel.AddItem(buttonData) as PushButton;

            TrySetIcon(button, "RevitCleanupManager.Resources.icon32.png", "RevitCleanupManager.Resources.icon16.png");

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static void TrySetIcon(PushButton? button, string large, string small)
        {
            if (button == null) return;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var largeStream = asm.GetManifestResourceStream(large);
                using var smallStream = asm.GetManifestResourceStream(small);
                if (largeStream != null)
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.StreamSource = largeStream;
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.EndInit();
                    button.LargeImage = img;
                }
                if (smallStream != null)
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.StreamSource = smallStream;
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.EndInit();
                    button.Image = img;
                }
            }
            catch
            {
                // Icons are optional - button still works without them.
            }
        }
    }
}
