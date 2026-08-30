using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using RevitCleanupManager.Core.Config;
using RevitCleanupManager.Core.Models;

namespace RevitCleanupManager.Core.QaRules
{
    public interface IQaRule
    {
        string Name { get; }
        List<QaIssue> Check(Document doc, QaConfig config);
    }

    internal static class QaRuleHelpers
    {
        public static Category GetCategoryByName(Document doc, string name)
        {
            foreach (Category c in doc.Settings.Categories)
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                    return c;
            return null;
        }

        /// <summary>Gets a parameter's display string value, or null if the parameter doesn't exist / has no value.</summary>
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

    /// <summary>Checks Level, Sheet, View, and Grid names against configured regex patterns.</summary>
    public class NamingConventionRule : IQaRule
    {
        public string Name => "Naming Convention";

        public List<QaIssue> Check(Document doc, QaConfig config)
        {
            var issues = new List<QaIssue>();

            foreach (var rule in config.NamingRules)
            {
                Regex regex;
                try { regex = new Regex(rule.RegexPattern); } catch { continue; }

                IEnumerable<(ElementId Id, string Category, string Name)> targets = rule.TargetElementType switch
                {
                    "Level" => new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Select(l => (l.Id, "Levels", l.Name)),
                    "Grid" => new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().Select(g => (g.Id, "Grids", g.Name)),
                    "Sheet" => new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => (s.Id, "Sheets", s.SheetNumber)),
                    "View" => new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && v.ViewType != ViewType.DrawingSheet).Select(v => (v.Id, "Views", v.Name)),
                    _ => Enumerable.Empty<(ElementId, string, string)>()
                };

                foreach (var t in targets)
                {
                    if (string.IsNullOrEmpty(t.Name) || regex.IsMatch(t.Name)) continue;
                    issues.Add(new QaIssue
                    {
                        Id = t.Id,
                        RevitCategory = t.Category,
                        ElementName = t.Name,
                        IssueType = QaIssueType.NamingConvention,
                        ParameterName = rule.TargetElementType == "Sheet" ? "Sheet Number" : "Name",
                        CurrentValue = t.Name,
                        ProposedValue = t.Name,
                        RuleDescription = rule.Description,
                        Severity = QaSeverity.Warning
                    });
                }
            }
            return issues;
        }
    }

    /// <summary>Checks configured required parameters for empty/missing values across categories.</summary>
    public class MissingParameterRule : IQaRule
    {
        public string Name => "Missing Parameters";

        public List<QaIssue> Check(Document doc, QaConfig config)
        {
            var issues = new List<QaIssue>();

            foreach (var rule in config.RequiredParameters)
            {
                IEnumerable<Element> elements;
                if (string.Equals(rule.CategoryName, "All", StringComparison.OrdinalIgnoreCase))
                {
                    elements = new FilteredElementCollector(doc).WhereElementIsNotElementType().Where(e => e.Category != null);
                }
                else if (string.Equals(rule.CategoryName, "Sheets", StringComparison.OrdinalIgnoreCase))
                {
                    elements = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<Element>();
                }
                else
                {
                    var cat = QaRuleHelpers.GetCategoryByName(doc, rule.CategoryName);
                    if (cat == null) continue;
                    elements = new FilteredElementCollector(doc).OfCategoryId(cat.Id).WhereElementIsNotElementType();
                }

                foreach (var el in elements)
                {
                    var value = QaRuleHelpers.GetParamValue(el, rule.ParameterName);
                    if (!string.IsNullOrWhiteSpace(value)) continue;

                    issues.Add(new QaIssue
                    {
                        Id = el.Id,
                        RevitCategory = el.Category?.Name ?? rule.CategoryName,
                        ElementName = el.Name,
                        IssueType = QaIssueType.MissingParameter,
                        ParameterName = rule.ParameterName,
                        CurrentValue = value ?? "(parameter not found)",
                        ProposedValue = "",
                        RuleDescription = $"'{rule.ParameterName}' is required for {rule.CategoryName} but is empty.",
                        Severity = QaSeverity.Error
                    });
                }
            }
            return issues;
        }
    }

    /// <summary>Flags families, types, views, and sheets that still have an un-renamed default/generic name.</summary>
    public class DefaultNameRule : IQaRule
    {
        public string Name => "Default / Placeholder Names";

        public List<QaIssue> Check(Document doc, QaConfig config)
        {
            var issues = new List<QaIssue>();
            var patterns = config.DefaultNamePatterns.Select(p => { try { return new Regex(p); } catch { return null; } }).Where(r => r != null).ToList();
            if (patterns.Count == 0) return issues;

            bool IsDefault(string name) => !string.IsNullOrEmpty(name) && patterns.Any(p => p.IsMatch(name));

            foreach (var f in new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>())
                if (IsDefault(f.Name))
                    issues.Add(MakeIssue(f.Id, "Families", f.Name, "Family still has a default/placeholder name."));

            foreach (var t in new FilteredElementCollector(doc).WhereElementIsElementType().OfClass(typeof(ElementType)).Cast<ElementType>())
                if (IsDefault(t.Name))
                    issues.Add(MakeIssue(t.Id, t.Category?.Name ?? "Types", t.Name, "Type still has a default/placeholder name."));

            foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && v.ViewType != ViewType.DrawingSheet))
                if (IsDefault(v.Name))
                    issues.Add(MakeIssue(v.Id, "Views", v.Name, "View still has a default/placeholder name."));

            return issues;
        }

        private static QaIssue MakeIssue(ElementId id, string category, string name, string rule) => new QaIssue
        {
            Id = id, RevitCategory = category, ElementName = name, IssueType = QaIssueType.DefaultFamilyOrTypeName,
            ParameterName = "Name", CurrentValue = name, ProposedValue = name, RuleDescription = rule, Severity = QaSeverity.Info
        };
    }

    /// <summary>Flags duplicate "Mark" values within the same category (e.g. two doors both marked "D01").</summary>
    public class DuplicateMarkRule : IQaRule
    {
        public string Name => "Duplicate Marks";

        public List<QaIssue> Check(Document doc, QaConfig config)
        {
            var issues = new List<QaIssue>();

            foreach (var categoryName in config.DuplicateMarkCategories)
            {
                var cat = QaRuleHelpers.GetCategoryByName(doc, categoryName);
                if (cat == null) continue;

                var elements = new FilteredElementCollector(doc).OfCategoryId(cat.Id).WhereElementIsNotElementType().ToList();
                var groups = elements
                    .Select(e => (Element: e, Mark: QaRuleHelpers.GetParamValue(e, "Mark")))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Mark))
                    .GroupBy(x => x.Mark);

                foreach (var g in groups.Where(g => g.Count() > 1))
                {
                    foreach (var (element, mark) in g)
                    {
                        issues.Add(new QaIssue
                        {
                            Id = element.Id, RevitCategory = categoryName, ElementName = element.Name,
                            IssueType = QaIssueType.DuplicateValue, ParameterName = "Mark", CurrentValue = mark, ProposedValue = mark,
                            RuleDescription = $"{g.Count()} elements in {categoryName} share Mark '{mark}'.", Severity = QaSeverity.Error
                        });
                    }
                }
            }
            return issues;
        }
    }

    /// <summary>Flags host-based elements with no valid Level association.</summary>
    public class LevelAssociationRule : IQaRule
    {
        public string Name => "Level Association";

        public List<QaIssue> Check(Document doc, QaConfig config)
        {
            var issues = new List<QaIssue>();

            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities == false && e is not View && e is not ViewSheet);

            foreach (var el in elements)
            {
                ElementId levelId;
                try { levelId = el.LevelId; } catch { continue; }
                if (levelId == null) continue; // property not applicable to this element type
                if (levelId != ElementId.InvalidElementId) continue;

                // Only flag categories where a Level genuinely should be set (host-based
                // model elements) -- skip annotation/view-only categories to avoid noise.
                var catId = el.Category.Id;
                bool isModelCategory = el.Category.CategoryType == CategoryType.Model;
                if (!isModelCategory) continue;

                issues.Add(new QaIssue
                {
                    Id = el.Id, RevitCategory = el.Category.Name, ElementName = el.Name,
                    IssueType = QaIssueType.LevelAssociation, ParameterName = "Level", CurrentValue = "(none)", ProposedValue = "",
                    RuleDescription = "Element has no associated Level.", Severity = QaSeverity.Warning
                });
            }
            return issues;
        }
    }

    public static class QaScanner
    {
        public static List<IQaRule> GetAllRules() => new List<IQaRule>
        {
            new NamingConventionRule(), new MissingParameterRule(), new DefaultNameRule(),
            new DuplicateMarkRule(), new LevelAssociationRule(),
        };

        public static List<QaIssue> RunAll(Document doc, QaConfig config, Action<string> onProgress = null)
        {
            var all = new List<QaIssue>();
            foreach (var rule in GetAllRules())
            {
                try { onProgress?.Invoke($"Checking: {rule.Name}..."); all.AddRange(rule.Check(doc, config)); }
                catch (Exception ex) { onProgress?.Invoke($"Rule {rule.Name} failed: {ex.Message}"); }
            }
            return all;
        }
    }
}
