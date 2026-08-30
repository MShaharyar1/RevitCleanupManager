using System.Collections.ObjectModel;
using System.Linq;
using RevitCleanupManager.Core.Models;

namespace RevitCleanupManager.UI.ViewModels
{
    public class CleanupItemViewModel : ObservableObject
    {
        public CleanupItem Model { get; }
        public CleanupItemViewModel(CleanupItem model)
        {
            Model = model;
            _isSelected = model.IsSafeToAutoSelect;
            model.IsSelected = _isSelected;
        }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { if (SetField(ref _isSelected, value)) Model.IsSelected = value; } }
        public string Name => Model.Name;
        public string TypeOrFamilyName => Model.TypeOrFamilyName;
        public string Details => Model.Details;
        public bool IsSafeToAutoSelect => Model.IsSafeToAutoSelect;
    }

    public class CategoryTabViewModel : ObservableObject
    {
        public CleanupCategory Category { get; }
        public string DisplayName { get; }
        public ObservableCollection<CleanupItemViewModel> Items { get; } = new ObservableCollection<CleanupItemViewModel>();
        public CategoryTabViewModel(CleanupCategory category, string displayName) { Category = category; DisplayName = displayName; }
        public string TabHeader => $"{DisplayName} ({Items.Count})";
        public int SelectedCount => Items.Count(i => i.IsSelected);

        public void Load(System.Collections.Generic.List<CleanupItem> items)
        {
            Items.Clear();
            foreach (var item in items) Items.Add(new CleanupItemViewModel(item));
            OnPropertyChanged(nameof(TabHeader));
        }
        public void SelectAllSafe() { foreach (var i in Items) i.IsSelected = i.IsSafeToAutoSelect; }
        public void SelectAll() { foreach (var i in Items) i.IsSelected = true; }
        public void SelectNone() { foreach (var i in Items) i.IsSelected = false; }
    }
}
