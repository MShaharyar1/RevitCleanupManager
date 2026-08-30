using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitCleanupManager.Core.Models;
using RevitCleanupManager.Core.QaRules;

namespace RevitCleanupManager.Core.Excel
{
    /// <summary>
    /// Builds the general-purpose bulk parameter grid (Diroots Param/Family/Sheet Manager
    /// style): pick categories + parameter names, get one row per element with every
    /// requested parameter's current value, ready to export/edit/reimport.
    /// </summary>
    public class ParameterGridBuilder
    {
        public List<ParameterGridRow> Build(Document doc, List<string> categoryNames, List<string> parameterNames)
        {
            var rows = new List<ParameterGridRow>();

            foreach (var categoryName in categoryNames)
            {
                IEnumerable<Element> elements;
                if (string.Equals(categoryName, "Sheets", System.StringComparison.OrdinalIgnoreCase))
                {
                    elements = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<Element>();
                }
                else
                {
                    var cat = QaRuleHelpersAccessor.GetCategoryByName(doc, categoryName);
                    if (cat == null) continue;
                    elements = new FilteredElementCollector(doc).OfCategoryId(cat.Id).WhereElementIsNotElementType();
                }

                foreach (var el in elements)
                {
                    var row = new ParameterGridRow
                    {
                        Id = el.Id,
                        RevitCategory = categoryName,
                        FamilyName = (el as FamilyInstance)?.Symbol?.Family?.Name ?? "",
                        TypeName = doc.GetElement(el.GetTypeId())?.Name ?? "",
                        ElementName = el.Name
                    };
                    foreach (var paramName in parameterNames)
                        row.Values[paramName] = QaRuleHelpersAccessor.GetParamValue(el, paramName) ?? "";

                    rows.Add(row);
                }
            }

            return rows;
        }
    }

    /// <summary>Thin public wrapper so the Excel layer can reuse QaRuleHelpers' internal lookups.</summary>
    public static class QaRuleHelpersAccessor
    {
        public static Category GetCategoryByName(Document doc, string name)
        {
            foreach (Category c in doc.Settings.Categories)
                if (string.Equals(c.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    return c;
            return null;
        }

        public static string GetParamValue(Element el, string paramName)
        {
            var p = el.LookupParameter(paramName);
            if (p == null) return null;
            if (!p.HasValue) return string.Empty;
            return p.StorageType switch
            {
                StorageType.String => p.AsString(),
                StorageType.Double => p.AsValueString(),
                StorageType.Integer => p.AsValueString() ?? p.AsInteger().ToString(),
                StorageType.ElementId => p.AsValueString(),
                _ => p.AsValueString()
            };
        }
    }
}
