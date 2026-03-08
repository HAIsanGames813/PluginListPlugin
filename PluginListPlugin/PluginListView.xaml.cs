using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PluginList
{
    public partial class PluginListView : UserControl
    {
        public PluginListView()
        {
            InitializeComponent();
        }

        private void PluginListBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (PluginListBox.ItemsSource == null) return;

            PluginListBox.SelectedItems.Clear();
            foreach (var item in PluginListBox.ItemsSource.Cast<PluginItemViewModel>())
            {
                if (item.IsSelected)
                    PluginListBox.SelectedItems.Add(item);
            }
        }

        /// <summary>
        /// ListBox 上のマウスホイールを内側の縦 ScrollViewer に転送する。
        /// ListBox 自身のスクロールを無効にしているため、このハンドラーがないと
        /// ホイール操作が何も起きない。
        /// </summary>
        private void PluginListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // ListBox の直近の親 ScrollViewer（縦スクロール用）を探して転送する
            var sv = FindParentScrollViewer(PluginListBox);
            if (sv != null)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
                e.Handled = true;
            }
        }

        private static ScrollViewer? FindParentScrollViewer(DependencyObject child)
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is ScrollViewer sv) return sv;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}