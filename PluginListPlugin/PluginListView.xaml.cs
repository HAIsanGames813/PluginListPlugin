using System.Windows;
using System.Windows.Controls;

namespace PluginList
{
    public partial class PluginListView : UserControl
    {
        public PluginListView()
        {
            InitializeComponent();
        }

        private void Sort_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is PluginListViewModel vm && sender is RadioButton rb)
            {
                vm.SortType = rb.CommandParameter?.ToString() ?? "Name";
            }
        }
    }
}