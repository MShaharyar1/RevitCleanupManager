using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RevitCleanupManager.Core.Config
{
    public class NamingRule
    {
        /// <summary>One of: Level, Grid, Sheet, View, FamilyType. Matched by QaRules code.</summary>
        public string TargetElementType { get; set; }
        public string RegexPattern { get; set; }
        public string Description { get; set; }
    }

    public class RequiredParameterRule
    {
        /// <summary>Revit category display name (e.g. "Doors"), or "All" for every model category.</summary>
        public string CategoryName { get; set; }
        public string ParameterName { get; set; }
    }

    /// <summary>
    /// Everything the QA/QC scan checks against is data-driven from this config, so firms
    /// can tune naming standards and required parameters per project/template without
    /// recompiling the plugin. Loaded from QaConfig.json next to the DLL if present,
    /// otherwise falls back to Default() below.
    /// </summary>
    public class QaConfig
    {
        public List<NamingRule> NamingRules { get; set; } = new List<NamingRule>();
        public List<RequiredParameterRule> RequiredParameters { get; set; } = new List<RequiredParameterRule>();

        /// <summary>Regex patterns that indicate an un-renamed default name (families, types, views, sheets).</summary>
        public List<string> DefaultNamePatterns { get; set; } = new List<string>();

        /// <summary>Category display names to check for duplicate "Mark" values (0 = check all categories that have a Mark parameter).</summary>
        public List<string> DuplicateMarkCategories { get; set; } = new List<string>();

        public static QaConfig Default() => new QaConfig
        {
            NamingRules = new List<NamingRule>
            {
                new NamingRule { TargetElementType = "Level", RegexPattern = @"^(L\d{1,2}|Level \d{1,2}|Roof|B\d)\s*-?\s*.*$", Description = "Levels should start with a floor code (e.g. 'L01 - Ground Floor')." },
                new NamingRule { TargetElementType = "Sheet", RegexPattern = @"^[A-Z]{1,3}-?\d{2,4}$", Description = "Sheet numbers should follow DISCIPLINE-### (e.g. 'A-101')." },
                new NamingRule { TargetElementType = "View", RegexPattern = @"^(?!.*(Copy of|\{3D\}|Unnamed)).+$", Description = "View names should not contain placeholder text like 'Copy of' or '{3D}'." },
                new NamingRule { TargetElementType = "Grid", RegexPattern = @"^[A-Z0-9.]{1,4}$", Description = "Grid names should be short alphanumeric labels (e.g. 'A', '1', 'A.1')." },
            },
            RequiredParameters = new List<RequiredParameterRule>
            {
                new RequiredParameterRule { CategoryName = "Doors", ParameterName = "Mark" },
                new RequiredParameterRule { CategoryName = "Windows", ParameterName = "Mark" },
                new RequiredParameterRule { CategoryName = "Rooms", ParameterName = "Name" },
                new RequiredParameterRule { CategoryName = "Rooms", ParameterName = "Number" },
                new RequiredParameterRule { CategoryName = "Sheets", ParameterName = "Sheet Name" },
            },
            DefaultNamePatterns = new List<string>
            {
                @"^Family\d*$", @"^Type\s?\d*$", @"^Unnamed$", @"Copy of", @"^Symbol\s?\d*$"
            },
            DuplicateMarkCategories = new List<string> { "Doors", "Windows" }
        };

        public static QaConfig LoadOrDefault(string jsonPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(jsonPath) && File.Exists(jsonPath))
                {
                    var json = File.ReadAllText(jsonPath);
                    var loaded = JsonSerializer.Deserialize<QaConfig>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch
            {
                // Fall through to defaults if the file is missing or malformed -- a bad
                // config file should never block the scan from running.
            }
            return Default();
        }
    }
}
