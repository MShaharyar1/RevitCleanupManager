# Revit Cleanup Manager

A single-dashboard Revit add-in: scan the active model once, review everything flagged
for cleanup across tabs (purgeable families, unplaced views, unused filters/templates,
unplaced schedules/legends, links, CAD imports, unused groups, warnings), and delete
everything you've checked in one click — with a model health score at the top.

## Project layout

```
RevitCleanupManager.sln
RevitCleanupManager/
  RevitCleanupManager.csproj      Multi-version build (2024/2025/2026)
  RevitCleanupManager.addin       Manifest Revit reads on startup
  App.cs                          IExternalApplication - builds the ribbon button
  Commands/ShowDashboardCommand.cs
  Core/
    ModelScanner.cs               Read-only scan logic, one method per category
    ModelHealthAnalyzer.cs        Turns scan counts into a 0-100 score
    PurgeService.cs               Deletion logic (runs inside its own Transaction)
    ExternalEventHandlers.cs      Required plumbing - see "Why ExternalEvent" below
  Models/ScanModels.cs            CleanupItem, ScanResult, ModelHealthReport
  UI/
    DashboardWindow.xaml          Dark-themed dashboard layout
    DashboardWindow.xaml.cs       Wires buttons/grids to the scan & purge services
```

## Getting it running

1. **Open `RevitCleanupManager.sln` in Visual Studio 2022.**
2. Pick a Solution Configuration matching the Revit version you want to test against:
   `Debug 2024`, `Debug 2025`, or `Debug 2026`. This sets the `RevitVersion` MSBuild
   property, which controls both the target framework (.NET Framework 4.8 for 2024,
   .NET 8 for 2025/2026) and which `Nice3point.Revit.Api.RevitAPI` NuGet package
   version gets restored.
3. Restore NuGet packages (Visual Studio does this automatically on build). These
   packages ship the Revit API DLLs as metadata references only — they are **not**
   copied into your output folder, so nothing conflicts with Revit's own copies at
   runtime.
4. Build. The post-build step in the `.csproj` copies the compiled DLL + `.addin`
   manifest straight into
   `%AppData%\Autodesk\Revit\Addins\<year>\RevitCleanupManager\`, so it'll be picked
   up the next time you launch that year's Revit.
5. **To debug with F5:** Project Properties → Debug → set "Start external program" to
   your `Revit.exe` path (e.g. `C:\Program Files\Autodesk\Revit 2025\Revit.exe`).
   Open a project, then click **Cleanup Tools → Model Cleanup → Cleanup Manager** on
   the ribbon.

## Why `ExternalEvent` shows up everywhere

The dashboard is a **modeless** WPF window (`window.Show()`, not `ShowDialog()`), so you
can keep working in Revit while it's open. That means every button click runs *outside*
Revit's API execution context — calling `doc.Delete()` directly from a click handler
throws an invalid-context exception. `ExternalEventHandlers.cs` implements Revit's
standard fix: the window raises an `ExternalEvent`, Revit calls back into the handler on
its own thread when it's safe, and the result is marshaled back to the UI thread via
`Dispatcher.Invoke`. If you add new one-click actions, follow the same pattern rather
than calling the Revit API straight from XAML code-behind.

## Known limitations (be aware before you rely on this)

- **"Purgeable Families" is a heuristic, not Revit's native Purge Unused.** The public
  Revit API does not expose Autodesk's internal purge algorithm (which also cleans up
  materials, line patterns, fill patterns, unused system family types, etc.). This tool
  detects loadable families with zero placed instances, which covers the most common
  and highest-value case. For full coverage, use the **"Run Native Purge Dialog"**
  button, which triggers Revit's own Purge Unused command via `PostCommand` — that
  still requires clicking through Revit's dialog, since `PostCommand` can't run headless.
- **Unused view templates** doesn't check the "default view template per discipline"
  setting (Manage → Default View Templates), since that isn't exposed as a simple
  lookup in the public API. Double-check that dialog before bulk-deleting templates.
- **Schedules and Revit/CAD links default to unchecked** in the UI on purpose — an
  unplaced schedule might still be used for exports/takeoffs, and deleting a link
  affects team coordination. Review the "Reason" column before checking these.
- **Deleting is one Undo away.** Every "Clean Selected" click runs as a single Revit
  transaction, so `Ctrl+Z` immediately after restores everything if something looks wrong.
- This was written and structured for you to compile/test in your own Revit + Visual
  Studio environment — I don't have a way to build or run it against real Revit here,
  so treat it as a solid, realistic starting point rather than a drop-in-and-forget binary.
  Test on a copy of a real project file before trusting it on production models.

## Extending it

- Add a new category: add an enum value to `CleanupCategory`, a `Scan...()` method in
  `ModelScanner.cs`, an entry in `TabOrder` in `DashboardWindow.xaml.cs`, and (if it
  needs custom scoring) a penalty line in `ModelHealthAnalyzer.cs`.
- Want per-category "Clean Selected" buttons instead of one global button? The data is
  already split by category in `ScanResult.Items` — swap `CleanSelectedButton_Click`'s
  source from "all tabs" to `GetActiveGridItems()`.
- Want scheduled/automatic scans (e.g. run on every project open)? Hook `ScanEventHandler`
  into `ControlledApplication.DocumentOpened` in `App.cs`.
