using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitCleanupManager.Core.Models
{
    public enum QaSeverity { Info, Warning, Error }

    public enum QaIssueType
    {
        MissingParameter,
        NamingConvention,
        LevelAssociation,
        DefaultFamilyOrTypeName,
        DuplicateValue
    }

    /// <summary>
    /// One flagged QA/QC issue. ParameterName is the literal string "Name" for
    /// rename-the-element issues (views, sheets, levels, families, types), or a real
    /// parameter name for parameter-value issues -- both are fixed the same way by
    /// ParameterUpdateExecutor, which is what lets "rename" and "fix a parameter" share
    /// one Excel round-trip and one Apply button.
    /// </summary>
    public class QaIssue
    {
        public ElementId Id { get; set; }
        public string RevitCategory { get; set; }
        public string ElementName { get; set; }
        public QaIssueType IssueType { get; set; }
        public string ParameterName { get; set; }
        public string CurrentValue { get; set; }
        public string RuleDescription { get; set; }
        public QaSeverity Severity { get; set; } = QaSeverity.Warning;

        /// <summary>User-editable in the grid (and in the exported Excel "New Value" column).</summary>
        public string ProposedValue { get; set; }
        public bool IsSelected { get; set; }
    }

    /// <summary>Result of applying one parameter/name update back to the model.</summary>
    public class ParameterUpdateResult
    {
        public ElementId Id { get; set; }
        public string ElementName { get; set; }
        public string ParameterName { get; set; }
        public string NewValue { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    /// <summary>One row of a general (non-QA) bulk parameter export/import grid.</summary>
    public class ParameterGridRow
    {
        public ElementId Id { get; set; }
        public string RevitCategory { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string ElementName { get; set; }
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }
}
