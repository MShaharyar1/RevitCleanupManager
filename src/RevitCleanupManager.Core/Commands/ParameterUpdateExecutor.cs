using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RevitCleanupManager.Core.Excel;
using RevitCleanupManager.Core.Models;

namespace RevitCleanupManager.Core.Commands
{
    /// <summary>
    /// Applies a batch of (ElementId, ParameterName, NewValue) updates -- the shared
    /// mechanism behind "Apply QA/QC fixes", "rename this element", and "import bulk
    /// parameter edits from Excel". ParameterName "Name" (and "Sheet Number" for sheets)
    /// is special-cased to rename the element itself rather than looking up a parameter.
    /// </summary>
    public class ParameterUpdateExecutor
    {
        public List<ParameterUpdateResult> Apply(Document doc, IEnumerable<ImportedUpdateRow> updates)
        {
            var results = new List<ParameterUpdateResult>();
            var list = updates.ToList();
            if (list.Count == 0) return results;

            using (var group = new TransactionGroup(doc, "Revit Cleanup Manager - Apply Updates"))
            {
                group.Start();
                foreach (var u in list)
                {
                    using (var t = new Transaction(doc, $"Update {u.ParameterName} on {u.Id}"))
                    {
                        var result = new ParameterUpdateResult { Id = u.Id, ParameterName = u.ParameterName, NewValue = u.NewValue };
                        try
                        {
                            t.Start();
                            var el = doc.GetElement(u.Id);
                            if (el == null)
                            {
                                t.RollBack();
                                result.Success = false;
                                result.Message = "Element no longer exists in the model.";
                                results.Add(result);
                                continue;
                            }
                            result.ElementName = el.Name;

                            if (u.ParameterName == "Sheet Number" && el is ViewSheet sheet)
                            {
                                sheet.SheetNumber = u.NewValue;
                            }
                            else if (u.ParameterName == "Name")
                            {
                                SetElementName(el, u.NewValue);
                            }
                            else
                            {
                                var p = el.LookupParameter(u.ParameterName);
                                if (p == null || p.IsReadOnly)
                                {
                                    t.RollBack();
                                    result.Success = false;
                                    result.Message = p == null ? "Parameter not found on this element." : "Parameter is read-only.";
                                    results.Add(result);
                                    continue;
                                }
                                SetParameterValue(p, u.NewValue);
                            }

                            t.Commit();
                            result.Success = true;
                            result.Message = "Updated";
                        }
                        catch (Exception ex)
                        {
                            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                            result.Success = false;
                            result.Message = ex.Message;
                        }
                        results.Add(result);
                    }
                }
                group.Assimilate();
            }
            return results;
        }

        private static void SetElementName(Element el, string newName)
        {
            switch (el)
            {
                case Family f: f.Name = newName; break;
                case ElementType et: et.Name = newName; break;
                case View v: v.Name = newName; break;
                case Level lvl: lvl.Name = newName; break;
                case Grid g: g.Name = newName; break;
                default: el.Name = newName; break;
            }
        }

        private static void SetParameterValue(Parameter p, string value)
        {
            switch (p.StorageType)
            {
                case StorageType.String:
                    p.Set(value);
                    break;
                case StorageType.Double:
                    if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                        p.Set(d);
                    else
                        throw new FormatException($"'{value}' is not a valid number for this parameter.");
                    break;
                case StorageType.Integer:
                    if (int.TryParse(value, out var iVal))
                        p.Set(iVal);
                    else if (bool.TryParse(value, out var bVal)) // yes/no parameters
                        p.Set(bVal ? 1 : 0);
                    else
                        throw new FormatException($"'{value}' is not a valid integer/yes-no for this parameter.");
                    break;
                case StorageType.ElementId:
                    throw new NotSupportedException("Element/material-reference parameters aren't supported for bulk text edits yet.");
                default:
                    throw new NotSupportedException("Unsupported parameter storage type.");
            }
        }
    }
}
