using System.Windows.Controls;

namespace PluginList
{
    public partial class PluginListView : UserControl
    {
        public PluginListView()
        {
            InitializeComponent();
        }

        private void PluginListBox_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (PluginListBox.ItemsSource == null) return;

            PluginListBox.SelectedItems.Clear();
            foreach (var item in PluginListBox.ItemsSource.Cast<PluginItemViewModel>())
            {
                if (item.IsSelected)
                {
                    PluginListBox.SelectedItems.Add(item);
                }
            }
        }
    }
}