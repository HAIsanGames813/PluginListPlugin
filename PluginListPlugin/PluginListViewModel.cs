using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace PluginList
{
    public class PluginListViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private readonly ObservableCollection<PluginItemViewModel> allPlugins = new ObservableCollection<PluginItemViewModel>();
        public ICollectionView PluginsView { get; }
        public ICommand ExecuteCommand { get; }
        public ICommand BulkToggleCommand { get; }
        public ICommand BulkDeleteCommand { get; }

        private string searchText = string.Empty;
        public string SearchText
        {
            get => searchText;
            set { searchText = value; OnPropertyChanged(nameof(SearchText)); PluginsView.Refresh(); }
        }

        private string sortType = "Name";
        public string SortType
        {
            get => sortType;
            set { sortType = value; OnPropertyChanged(nameof(SortType)); PluginsView.Refresh(); }
        }

        private bool isAscending = true;
        public bool IsAscending
        {
            get => isAscending;
            set { isAscending = value; OnPropertyChanged(nameof(IsAscending)); PluginsView.Refresh(); }
        }

        public PluginListViewModel()
        {
            ExecuteCommand = new RelayCommand(ExecuteTask);
            BulkToggleCommand = new RelayCommand(BulkToggle);
            BulkDeleteCommand = new RelayCommand(BulkDelete);

            PluginsView = CollectionViewSource.GetDefaultView(allPlugins);
            var view = PluginsView as ListCollectionView;
            if (view != null) view.CustomSort = new PluginComparer(this);

            PluginsView.Filter = p =>
            {
                if (p is not PluginItemViewModel item) return false;
                return string.IsNullOrEmpty(SearchText) || item.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
            };

            LoadPlugins();
        }

        private void LoadPlugins()
        {
            allPlugins.Clear();
            string pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user", "plugin");
            string? selfFilePath = Assembly.GetExecutingAssembly().Location;
            string selfFileName = !string.IsNullOrEmpty(selfFilePath) ? Path.GetFileName(selfFilePath) : string.Empty;
            string selfFolderName = !string.IsNullOrEmpty(selfFilePath) ? Path.GetFileName(Path.GetDirectoryName(selfFilePath)) ?? string.Empty : string.Empty;

            if (!Directory.Exists(pluginDir)) return;

            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .ToList();

            foreach (string dir in Directory.GetDirectories(pluginDir))
            {
                string folderName = Path.GetFileName(dir);
                if (folderName.Equals(selfFolderName, StringComparison.OrdinalIgnoreCase)) continue;

                string? internalName = GetInternalPluginName(loadedAssemblies, dir, true);
                bool isDisabled = folderName.StartsWith("_") || Directory.GetFiles(dir, "*.dll.disabled").Any();

                string cleanFolderName = folderName.TrimStart('_');
                string finalName = (string.IsNullOrEmpty(internalName) || internalName == cleanFolderName)
                    ? folderName
                    : $"{folderName} / {internalName}";

                allPlugins.Add(new PluginItemViewModel { DisplayName = finalName, OriginalName = folderName, IsDisabled = isDisabled, IsDirectory = true });
            }

            foreach (string file in Directory.GetFiles(pluginDir, "*.*")
                .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals(selfFileName, StringComparison.OrdinalIgnoreCase)) continue;

                string? internalName = GetInternalPluginName(loadedAssemblies, file, false);
                bool isDisabled = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

                string cleanFileName = isDisabled ? fileName.Substring(0, fileName.Length - 9) : fileName;
                string finalName = (string.IsNullOrEmpty(internalName) || internalName == cleanFileName)
                    ? fileName
                    : $"{fileName} / {internalName}";

                allPlugins.Add(new PluginItemViewModel { DisplayName = finalName, OriginalName = fileName, IsDisabled = isDisabled, IsDirectory = false });
            }

            PluginsView.Refresh();
        }

        private string? GetInternalPluginName(List<Assembly> assemblies, string path, bool isDir)
        {
            try
            {
                var asm = assemblies.FirstOrDefault(a => isDir
                    ? a.Location.StartsWith(path, StringComparison.OrdinalIgnoreCase)
                    : a.Location.Equals(path, StringComparison.OrdinalIgnoreCase));

                if (asm != null)
                {
                    var type = asm.GetTypes().FirstOrDefault(t => t.GetProperty("Name") != null && (t.Name.EndsWith("Plugin") || t.GetInterfaces().Any(i => i.Name.Contains("Plugin"))));
                    if (type != null)
                    {
                        var instance = Activator.CreateInstance(type);
                        if (instance != null)
                        {
                            return type.GetProperty("Name")?.GetValue(instance)?.ToString();
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private void BulkToggle()
        {
            foreach (var item in allPlugins.Where(x => x.IsSelected))
            {
                item.IsTogglePending = !item.IsTogglePending;
            }
        }

        private void BulkDelete()
        {
            foreach (var item in allPlugins.Where(x => x.IsSelected))
            {
                item.IsPendingDelete = !item.IsPendingDelete;
            }
        }

        private void ExecuteTask()
        {
            var targets = allPlugins.Where(p => p.IsPendingDelete || p.IsTogglePending).ToList();
            if (!targets.Any())
            {
                MessageBox.Show("変更する項目が選択されていません。");
                return;
            }

            StringBuilder confirmMsg = new StringBuilder();
            confirmMsg.AppendLine("以下の操作を適用しますか？適用後、YMM4は終了します。");
            confirmMsg.AppendLine();
            foreach (var p in targets)
            {
                if (p.IsPendingDelete) confirmMsg.AppendLine($"・[削除] {p.DisplayName}");
                else if (p.IsTogglePending) confirmMsg.AppendLine($"・[{(p.IsDisabled ? "有効化" : "無効化")}] {p.DisplayName}");
            }

            if (MessageBox.Show(confirmMsg.ToString(), "最終確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            string batchPath = Path.Combine(Path.GetTempPath(), "ymm4_plugin_manager.bat");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off\nchcp 65001 > nul\ntimeout /t 2 /nobreak > nul");

            string pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user", "plugin");
            foreach (var p in targets)
            {
                string currentPath = Path.Combine(pluginDir, p.OriginalName);
                if (p.IsPendingDelete)
                {
                    if (p.IsDirectory) sb.AppendLine($"rd /s /q \"{currentPath}\"");
                    else sb.AppendLine($"del /f /q \"{currentPath}\"");
                }
                else if (p.IsTogglePending)
                {
                    if (p.IsDirectory)
                    {
                        string newName = p.IsDisabled ? p.OriginalName.TrimStart('_') : "_" + p.OriginalName;
                        string newPath = Path.Combine(pluginDir, newName);
                        sb.AppendLine($"move \"{currentPath}\" \"{newPath}\"");
                        if (!p.IsDisabled) sb.AppendLine($"ren \"{newPath}\\*.dll\" *.dll.disabled");
                        else sb.AppendLine($"ren \"{newPath}\\*.disabled\" *.dll");
                    }
                    else
                    {
                        string newName = p.IsDisabled ? p.OriginalName.Replace(".disabled", "") : p.OriginalName + ".disabled";
                        string newPath = Path.Combine(pluginDir, newName);
                        sb.AppendLine($"move \"{currentPath}\" \"{newPath}\"");
                    }
                }
            }
            sb.AppendLine("del \"%~f0\"");
            File.WriteAllText(batchPath, sb.ToString(), new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c \"{batchPath}\"", CreateNoWindow = true, UseShellExecute = false });
            Application.Current.Shutdown();
        }

        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private class PluginComparer : System.Collections.IComparer
        {
            private readonly PluginListViewModel vm;
            public PluginComparer(PluginListViewModel viewModel) => vm = viewModel;
            public int Compare(object? x, object? y)
            {
                if (x is not PluginItemViewModel p1 || y is not PluginItemViewModel p2) return 0;
                int result = 0;
                if (vm.SortType == "Status") result = p1.IsDisabled.CompareTo(p2.IsDisabled);
                if (result == 0) result = string.Compare(p1.DisplayName, p2.DisplayName, StringComparison.OrdinalIgnoreCase);
                return vm.IsAscending ? result : -result;
            }
        }
    }

    public class PluginItemViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string DisplayName { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public bool IsDisabled { get; set; }
        public bool IsDirectory { get; set; }

        private bool isSelected;
        public bool IsSelected { get => isSelected; set { isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }

        private bool isTogglePending;
        public bool IsTogglePending { get => isTogglePending; set { isTogglePending = value; if (value) isPendingDelete = false; OnPropertyChanged(nameof(IsTogglePending)); OnPropertyChanged(nameof(IsPendingDelete)); } }

        private bool isPendingDelete;
        public bool IsPendingDelete { get => isPendingDelete; set { isPendingDelete = value; if (value) isTogglePending = false; OnPropertyChanged(nameof(IsPendingDelete)); OnPropertyChanged(nameof(IsTogglePending)); } }

        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action execute;
        public RelayCommand(Action execute) => this.execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}