using System.Windows;
using Autodesk.Revit.DB;
using RevitCleanupManager.UI.ViewModels;

namespace RevitCleanupManager.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow(Document doc)
        {
            InitializeComponent();
            DataContext = new ShellViewModel(doc);
        }
    }
}
