using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using RevitCleanupManager.Models;

namespace RevitCleanupManager.Core
{
    /// <summary>
    /// IMPORTANT: The dashboard is a modeless WPF window, so its button click handlers run
    /// OUTSIDE Revit's API execution context. Calling doc.Delete(), starting a Transaction,
    /// etc. directly from a WPF event handler throws an invalid-context exception.
    /// The fix is Revit's ExternalEvent pattern: the WPF window raises an ExternalEvent,
    /// Revit calls back into these handlers on its own thread when it's safe to do so,
    /// and results are marshaled back to the UI thread via Dispatcher.Invoke.
    /// </summary>
    public class ScanEventHandler : IExternalEventHandler
    {
        public Action<ScanResult>? OnScanComplete;
        public Action<string>? OnError;

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    OnError?.Invoke("No active document.");
                    return;
                }
                var scanner = new ModelScanner(doc);
                var result = scanner.ScanAll();
                OnScanComplete?.Invoke(result);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex.Message);
            }
        }

        public string GetName() => "Revit Cleanup Manager - Scan";
    }

    public class CleanEventHandler : IExternalEventHandler
    {
        /// <summary>Set by the dashboard immediately before Raise() is called.</summary>
        public List<CleanupItem> ItemsToClean { get; set; } = new();
        public Action<PurgeResult>? OnCleanComplete;
        public Action<string>? OnError;

        public void Execute(UIApplication app)
        {
            try
            {
                var service = new PurgeService(app);
                var result = service.DeleteSelected(ItemsToClean);
                OnCleanComplete?.Invoke(result);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex.Message);
            }
        }

        public string GetName() => "Revit Cleanup Manager - Clean Selected";
    }

    public class NativePurgeEventHandler : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            new PurgeService(app).RunNativePurgeUnused();
        }

        public string GetName() => "Revit Cleanup Manager - Native Purge";
    }
}
