using System.Collections.Generic;
using RevitCleanupManager.Core.Models;

namespace RevitCleanupManager.UI.ViewModels
{
    /// <summary>Bindable wrapper around a QaIssue -- editable checkbox + editable "New Value" cell.</summary>
    public class QaIssueViewModel : ObservableObject
    {
        public QaIssue Model { get; }
        public QaIssueViewModel(QaIssue model) { Model = model; _proposedValue = model.ProposedValue; }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { if (SetField(ref _isSelected, value)) Model.IsSelected = value; } }

        private string _proposedValue;
        public string ProposedValue { get => _proposedValue; set { if (SetField(ref _proposedValue, value)) Model.ProposedValue = value; } }

        public string RevitCategory => Model.RevitCategory;
        public string ElementName => Model.ElementName;
        public string IssueType => Model.IssueType.ToString();
        public string ParameterName => Model.ParameterName;
        public string CurrentValue => Model.CurrentValue;
        public string Severity => Model.Severity.ToString();
        public string RuleDescription => Model.RuleDescription;
    }

    public class CategoryFilterOption
    {
        public string Name { get; set; }
        public override string ToString() => Name;
    }
}
