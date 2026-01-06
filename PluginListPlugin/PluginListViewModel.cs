using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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

        private readonly ObservableCollection<PluginItemViewModel> allPlugins;
        public ICollectionView PluginsView { get; }
        public ICommand ExecuteCommand { get; }
        public ICommand BulkEnableCommand { get; }
        public ICommand BulkDisableCommand { get; }
        public ICommand BulkDeleteCommand { get; }
        public ICommand OpenPluginFolderCommand { get; }
        public ICommand ToggleDisplayModeCommand { get; }

        private string searchText = string.Empty;
        public string SearchText
        {
            get => searchText;
            set { searchText = value; OnPropertyChanged(nameof(SearchText)); PluginsView.Refresh(); }
        }

        private bool isRetrievedMode = true;
        public bool IsRetrievedMode
        {
            get => isRetrievedMode;
            set
            {
                isRetrievedMode = value;
                OnPropertyChanged(nameof(IsRetrievedMode));
                foreach (var item in allPlugins) item.IsRetrievedMode = value;
            }
        }

        private string combinedSortType = "NameAsc";
        public string CombinedSortType
        {
            get => combinedSortType;
            set
            {
                combinedSortType = value;
                switch (value)
                {
                    case "NameAsc": SortType = "Name"; IsAscending = true; break;
                    case "NameDesc": SortType = "Name"; IsAscending = false; break;
                    case "StatusAsc": SortType = "Status"; IsAscending = true; break;
                    case "StatusDesc": SortType = "Status"; IsAscending = false; break;
                }
                OnPropertyChanged(nameof(CombinedSortType));
                PluginsView.Refresh();
            }
        }

        public string SortType { get; private set; } = "Name";
        public bool IsAscending { get; private set; } = true;

        public PluginListViewModel()
        {
            ExecuteCommand = new RelayCommand(ExecuteTask);
            BulkEnableCommand = new RelayCommand(BulkEnable);
            BulkDisableCommand = new RelayCommand(BulkDisable);
            BulkDeleteCommand = new RelayCommand(BulkDelete);
            OpenPluginFolderCommand = new RelayCommand(OpenPluginFolder);
            ToggleDisplayModeCommand = new RelayCommand(() => IsRetrievedMode = !IsRetrievedMode);

            allPlugins = new ObservableCollection<PluginItemViewModel>();
            LoadPlugins();

            PluginsView = CollectionViewSource.GetDefaultView(allPlugins);
            var view = PluginsView as ListCollectionView;
            if (view != null) view.CustomSort = new PluginComparer(this);

            PluginsView.Filter = p =>
            {
                if (p is not PluginItemViewModel item) return false;
                return string.IsNullOrEmpty(SearchText) || item.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
            };
        }

        private void LoadPlugins()
        {
            allPlugins.Clear();
            string pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user", "plugin");
            if (!Directory.Exists(pluginDir)) return;

            string? selfFilePath = Assembly.GetExecutingAssembly().Location;
            string selfFileName = !string.IsNullOrEmpty(selfFilePath) ? Path.GetFileName(selfFilePath) : string.Empty;

            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .ToList();

            foreach (string dir in Directory.GetDirectories(pluginDir))
            {
                string folderName = Path.GetFileName(dir);
                if (folderName.StartsWith("PluginList", StringComparison.OrdinalIgnoreCase)) continue;

                string? internalName = GetSafePluginName(loadedAssemblies, dir, true);
                bool isDisabled = folderName.StartsWith("_") || Directory.GetFiles(dir, "*.dll.disabled").Any();

                allPlugins.Add(new PluginItemViewModel { InternalName = internalName ?? string.Empty, OriginalName = folderName, IsDisabled = isDisabled, IsDirectory = true, IsRetrievedMode = this.IsRetrievedMode });
            }

            foreach (string file in Directory.GetFiles(pluginDir, "*.*")
                .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals(selfFileName, StringComparison.OrdinalIgnoreCase)) continue;

                string? internalName = GetSafePluginName(loadedAssemblies, file, false);
                bool isDisabled = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

                allPlugins.Add(new PluginItemViewModel { InternalName = internalName ?? string.Empty, OriginalName = fileName, IsDisabled = isDisabled, IsDirectory = false, IsRetrievedMode = this.IsRetrievedMode });
            }
        }

        private string? GetSafePluginName(List<Assembly> assemblies, string path, bool isDir)
        {
            try
            {
                var targetAsm = assemblies.FirstOrDefault(a =>
                {
                    try
                    {
                        string loc = a.Location;
                        return isDir ? loc.StartsWith(path, StringComparison.OrdinalIgnoreCase) : loc.Equals(path, StringComparison.OrdinalIgnoreCase);
                    }
                    catch { return false; }
                });

                if (targetAsm != null)
                {
                    Type[] types;
                    try { types = targetAsm.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

                    foreach (var type in types)
                    {
                        if (type.GetInterfaces().Any(i => i.Name.Contains("Plugin")))
                        {
                            var nameProp = type.GetProperty("Name");
                            if (nameProp != null && nameProp.CanRead && nameProp.GetGetMethod()?.IsStatic == true)
                            {
                                try
                                {
                                    return nameProp.GetValue(null)?.ToString();
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                string? targetFile = null;
                if (!isDir)
                {
                    targetFile = path;
                }
                else
                {
                    string folderName = Path.GetFileName(path);
                    string potentialDll = Path.Combine(path, folderName + ".dll");
                    if (File.Exists(potentialDll)) targetFile = potentialDll;
                    else
                    {
                        var dlls = Directory.GetFiles(path, "*.dll");
                        if (dlls.Length > 0) targetFile = dlls[0];
                    }
                }

                if (!string.IsNullOrEmpty(targetFile) && File.Exists(targetFile))
                {
                    var info = FileVersionInfo.GetVersionInfo(targetFile);
                    if (!string.IsNullOrWhiteSpace(info.ProductName)) return info.ProductName;
                    if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription;
                }
            }
            catch { }

            return null;
        }

        private void BulkEnable()
        {
            foreach (var item in allPlugins.Where(x => x.IsSelected))
            {
                if (item.IsDisabled) item.IsTogglePending = true;
                else item.IsTogglePending = false;
            }
        }

        private void BulkDisable()
        {
            foreach (var item in allPlugins.Where(x => x.IsSelected))
            {
                if (!item.IsDisabled) item.IsTogglePending = true;
                else item.IsTogglePending = false;
            }
        }

        private void BulkDelete()
        {
            foreach (var item in allPlugins.Where(x => x.IsSelected))
            {
                item.IsPendingDelete = !item.IsPendingDelete;
            }
        }

        private void OpenPluginFolder()
        {
            string pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user", "plugin");
            if (Directory.Exists(pluginDir)) Process.Start("explorer.exe", pluginDir);
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
            int del = targets.Count(p => p.IsPendingDelete);
            int en = targets.Count(p => p.IsTogglePending && p.IsDisabled);
            int dis = targets.Count(p => p.IsTogglePending && !p.IsDisabled);
            if (del > 0) confirmMsg.AppendLine($"・削除: {del}件");
            if (en > 0) confirmMsg.AppendLine($"・有効化: {en}件");
            if (dis > 0) confirmMsg.AppendLine($"・無効化: {dis}件");

            if (MessageBox.Show(confirmMsg.ToString(), "最終確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            string batchPath = Path.Combine(Path.GetTempPath(), "ymm4_plugin_manager.bat");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 > nul");
            sb.AppendLine(":WAIT_LOOP");
            sb.AppendLine("tasklist /FI \"IMAGENAME eq YukkuriMovieMaker.exe\" 2>NUL | find /I /N \"YukkuriMovieMaker.exe\">NUL");
            sb.AppendLine("if \"%ERRORLEVEL%\"==\"0\" ( timeout /t 1 /nobreak > nul & goto WAIT_LOOP )");
            sb.AppendLine("timeout /t 1 /nobreak > nul");

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
                        sb.AppendLine($"pushd \"{newPath}\"");
                        if (!p.IsDisabled) sb.AppendLine("ren *.dll *.dll.disabled 2>nul");
                        else sb.AppendLine("ren *.dll.disabled *.dll 2>nul");
                        sb.AppendLine("popd");
                    }
                    else
                    {
                        string newFileName = p.IsDisabled ? p.OriginalName.Replace(".dll.disabled", ".dll") : p.OriginalName + ".disabled";
                        string newFilePath = Path.Combine(pluginDir, newFileName);
                        sb.AppendLine($"move /y \"{currentPath}\" \"{newFilePath}\"");
                    }
                }
            }
            sb.AppendLine("del \"%~f0\"");
            File.WriteAllText(batchPath, sb.ToString(), new UTF8Encoding(false));

            Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c \"{batchPath}\"", CreateNoWindow = true, UseShellExecute = false });
            Application.Current?.Shutdown();
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
        public string InternalName { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public bool IsDisabled { get; set; }
        public bool IsDirectory { get; set; }

        private bool isRetrievedMode;
        public bool IsRetrievedMode
        {
            get => isRetrievedMode;
            set { isRetrievedMode = value; OnPropertyChanged(nameof(DisplayName)); }
        }

        public string DisplayName
        {
            get
            {
                if (IsRetrievedMode && !string.IsNullOrEmpty(InternalName))
                {
                    return InternalName;
                }
                return OriginalName;
            }
        }

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