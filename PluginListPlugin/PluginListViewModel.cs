using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
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
        public ICommand BulkEnableCommand { get; }
        public ICommand BulkDisableCommand { get; }
        public ICommand BulkDeleteCommand { get; }
        public ICommand OpenPluginFolderCommand { get; }

        // --- 検索 ---
        private string searchText = string.Empty;
        public string SearchText
        {
            get => searchText;
            set { searchText = value; OnPropertyChanged(nameof(SearchText)); PluginsView.Refresh(); }
        }

        // --- ソート種別（ラジオボタン用） ---
        // "Name" / "PublishedAt" / "DownloadedAt" / "Author" / "Type" / "Status" / "Version"
        private string sortType = "Name";
        public string SortType
        {
            get => sortType;
            set { sortType = value; OnPropertyChanged(nameof(SortType)); PluginsView.Refresh(); }
        }

        // --- 昇順/降順（ラジオボタン用） ---
        private bool isAscending = true;
        public bool IsAscending
        {
            get => isAscending;
            set { isAscending = value; OnPropertyChanged(nameof(IsAscending)); PluginsView.Refresh(); }
        }

        public PluginListViewModel()
        {
            ExecuteCommand = new RelayCommand(ExecuteTask);
            BulkEnableCommand = new RelayCommand(BulkEnable);
            BulkDisableCommand = new RelayCommand(BulkDisable);
            BulkDeleteCommand = new RelayCommand(BulkDelete);
            OpenPluginFolderCommand = new RelayCommand(OpenPluginFolder);

            LoadPlugins();
            PluginsView = CollectionViewSource.GetDefaultView(allPlugins);
            var view = PluginsView as ListCollectionView;
            if (view != null) view.CustomSort = new PluginComparer(this);

            PluginsView.Filter = p =>
            {
                if (p is not PluginItemViewModel item) return false;
                if (string.IsNullOrEmpty(SearchText)) return true;
                return item.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || item.InfoAuthor.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || item.InfoType.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
            };
        }

        // ---- info.json -------------------------------------------------------

        private record PluginInfoEntry(
            string Name, string PortalName, string Author, string Version,
            string Type, string PublishedAt, string DownloadedAt);

        private Dictionary<string, PluginInfoEntry> LoadInfoJson(string pluginDir)
        {
            var result = new Dictionary<string, PluginInfoEntry>(StringComparer.OrdinalIgnoreCase);
            string infoPath = Path.Combine(pluginDir, "info.json");
            if (!File.Exists(infoPath)) return result;
            try
            {
                string json = File.ReadAllText(infoPath, Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("plugins", out var plugins)) return result;
                foreach (var elem in plugins.EnumerateArray())
                {
                    string Get(string key) => elem.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
                    string name = Get("name");
                    if (!string.IsNullOrEmpty(name))
                        result[name] = new PluginInfoEntry(name, Get("portalName"), Get("author"),
                            Get("version"), Get("type"), Get("publishedAt"), Get("downloadedAt"));
                }
            }
            catch { }
            return result;
        }

        private static string FormatDate(string? iso)
        {
            if (string.IsNullOrEmpty(iso)) return string.Empty;
            return DateTimeOffset.TryParse(iso, out var dto)
                ? dto.ToLocalTime().ToString("yyyy/MM/dd HH:mm")
                : iso;
        }

        // ---- プラグイン読み込み ----------------------------------------------

        private void LoadPlugins()
        {
            allPlugins.Clear();
            string pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user", "plugin");
            if (!Directory.Exists(pluginDir)) return;

            string? selfFilePath = Assembly.GetExecutingAssembly().Location;
            string selfFileName = !string.IsNullOrEmpty(selfFilePath) ? Path.GetFileName(selfFilePath) : string.Empty;

            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)).ToList();

            var infoMap = LoadInfoJson(pluginDir);

            foreach (string dir in Directory.GetDirectories(pluginDir))
            {
                string folderName = Path.GetFileName(dir);
                if (folderName.StartsWith("PluginList", StringComparison.OrdinalIgnoreCase)) continue;

                string? internalName = GetSafePluginName(loadedAssemblies, dir, true);
                bool isDisabled = folderName.StartsWith("_") ||
                                  Directory.GetFiles(dir, "*.dll.disabled", SearchOption.AllDirectories).Any();

                string lookupKey = folderName.TrimStart('_');
                infoMap.TryGetValue(lookupKey, out var info);

                allPlugins.Add(new PluginItemViewModel
                {
                    InternalName = internalName ?? string.Empty,
                    OriginalName = folderName,
                    IsDisabled = isDisabled,
                    IsDirectory = true,
                    FolderPath = dir,
                    HasInfoJson = info != null,
                    InfoPortalName = info?.PortalName ?? string.Empty,
                    InfoType = info?.Type ?? string.Empty,
                    InfoVersion = info?.Version ?? string.Empty,
                    InfoAuthor = info?.Author ?? string.Empty,
                    InfoPublishedAt = FormatDate(info?.PublishedAt),
                    InfoDownloadedAt = FormatDate(info?.DownloadedAt),
                    PublishedAtRaw = info?.PublishedAt ?? string.Empty,
                    DownloadedAtRaw = info?.DownloadedAt ?? string.Empty,
                });
            }

            foreach (string file in Directory.GetFiles(pluginDir, "*.*")
                .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals(selfFileName, StringComparison.OrdinalIgnoreCase)) continue;

                string? internalName = GetSafePluginName(loadedAssemblies, file, false);
                bool isDisabled = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

                allPlugins.Add(new PluginItemViewModel
                {
                    InternalName = internalName ?? string.Empty,
                    OriginalName = fileName,
                    IsDisabled = isDisabled,
                    IsDirectory = false,
                    FolderPath = pluginDir,
                    HasInfoJson = false,
                });
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
                        return isDir
                            ? loc.StartsWith(path, StringComparison.OrdinalIgnoreCase)
                            : loc.Equals(path, StringComparison.OrdinalIgnoreCase);
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
                            if (nameProp != null && nameProp.CanRead)
                            {
                                try
                                {
                                    if (nameProp.GetGetMethod()?.IsStatic == true) return nameProp.GetValue(null)?.ToString();
                                    var instance = Activator.CreateInstance(type);
                                    return nameProp.GetValue(instance)?.ToString();
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
                string? targetFile = isDir
                    ? Directory.GetFiles(path, "*.dll", SearchOption.TopDirectoryOnly).FirstOrDefault()
                    : path;
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

        // ---- 一括操作 --------------------------------------------------------

        private void BulkEnable()
        {
            foreach (var item in allPlugins.Where(x => x.IsSelected))
                item.IsTogglePending = item.IsDisabled;
        }

        private void BulkDisable()
        {
            foreach (var item in allPlugins.Where(x => x.IsSelected))
                item.IsTogglePending = !item.IsDisabled;
        }

        private void BulkDelete()
        {
            foreach (var item in allPlugins.Where(x => x.IsSelected))
                item.IsPendingDelete = !item.IsPendingDelete;
        }

        private void OpenPluginFolder()
        {
            string pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user", "plugin");
            if (Directory.Exists(pluginDir)) Process.Start("explorer.exe", pluginDir);
        }

        private void ExecuteTask()
        {
            var targets = allPlugins.Where(p => p.IsPendingDelete || p.IsTogglePending).ToList();
            if (!targets.Any()) return;

            if (MessageBox.Show("変更を適用しますか？適用後、YMM4は終了します。", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

            string batchPath = Path.Combine(Path.GetTempPath(), "ymm4_plugin_manager.bat");
            int currentPid = Process.GetCurrentProcess().Id;

            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal enabledelayedexpansion");
            sb.AppendLine("chcp 65001 > nul");
            sb.AppendLine(":WAIT_LOOP");
            sb.AppendLine($"tasklist /FI \"PID eq {currentPid}\" 2>NUL | find \"{currentPid}\" >NUL");
            sb.AppendLine("if \"%ERRORLEVEL%\"==\"0\" ( timeout /t 1 /nobreak > nul & goto WAIT_LOOP )");
            sb.AppendLine("timeout /t 2 /nobreak > nul");

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
                        sb.AppendLine($"move /y \"{currentPath}\" \"{newPath}\"");
                        if (!p.IsDisabled)
                            sb.AppendLine($"powershell -Command \"Get-ChildItem -Path '{newPath}' -Filter '*.dll' -Recurse | ForEach-Object {{ Rename-Item -Path $_.FullName -NewName ($_.Name + '.disabled') -ErrorAction SilentlyContinue }}\"");
                        else
                            sb.AppendLine($"powershell -Command \"Get-ChildItem -Path '{newPath}' -Filter '*.dll.disabled' -Recurse | ForEach-Object {{ Rename-Item -Path $_.FullName -NewName ($_.Name -replace '.disabled', '') -ErrorAction SilentlyContinue }}\"");
                    }
                    else
                    {
                        string newFileName = p.IsDisabled
                            ? p.OriginalName.Replace(".dll.disabled", ".dll")
                            : p.OriginalName + ".disabled";
                        sb.AppendLine($"move /y \"{currentPath}\" \"{Path.Combine(pluginDir, newFileName)}\"");
                    }
                }
            }
            sb.AppendLine("(goto) 2>nul & del \"%~f0\"");
            File.WriteAllText(batchPath, sb.ToString(), new UTF8Encoding(false));

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchPath}\"",
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = true
            });

            Application.Current?.Shutdown();
        }

        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ---- ソーター --------------------------------------------------------

        private class PluginComparer : System.Collections.IComparer
        {
            private readonly PluginListViewModel vm;
            public PluginComparer(PluginListViewModel viewModel) => vm = viewModel;

            public int Compare(object? x, object? y)
            {
                if (x is not PluginItemViewModel p1 || y is not PluginItemViewModel p2) return 0;

                int result = vm.SortType switch
                {
                    "Status" => p1.IsDisabled.CompareTo(p2.IsDisabled),
                    "Author" => string.Compare(p1.InfoAuthor, p2.InfoAuthor, StringComparison.OrdinalIgnoreCase),
                    "Type" => string.Compare(p1.InfoType, p2.InfoType, StringComparison.OrdinalIgnoreCase),
                    "Version" => string.Compare(p1.InfoVersion, p2.InfoVersion, StringComparison.OrdinalIgnoreCase),
                    "PublishedAt" => string.Compare(p1.PublishedAtRaw, p2.PublishedAtRaw, StringComparison.OrdinalIgnoreCase),
                    "DownloadedAt" => string.Compare(p1.DownloadedAtRaw, p2.DownloadedAtRaw, StringComparison.OrdinalIgnoreCase),
                    _ => string.Compare(p1.DisplayName, p2.DisplayName, StringComparison.OrdinalIgnoreCase),
                };

                if (result == 0 && vm.SortType != "Name")
                    result = string.Compare(p1.DisplayName, p2.DisplayName, StringComparison.OrdinalIgnoreCase);

                return vm.IsAscending ? result : -result;
            }
        }
    }

    // =========================================================================

    public class PluginItemViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string InternalName { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public bool IsDisabled { get; set; }
        public bool IsDirectory { get; set; }
        public string FolderPath { get; set; } = string.Empty;

        // info.json 由来
        public bool HasInfoJson { get; set; }
        public string InfoPortalName { get; set; } = string.Empty;
        public string InfoType { get; set; } = string.Empty;
        public string InfoVersion { get; set; } = string.Empty;
        public string InfoAuthor { get; set; } = string.Empty;
        public string InfoPublishedAt { get; set; } = string.Empty;
        public string InfoDownloadedAt { get; set; } = string.Empty;
        // ソート用（ISO 8601 のまま保持）
        public string PublishedAtRaw { get; set; } = string.Empty;
        public string DownloadedAtRaw { get; set; } = string.Empty;

        /// <summary>有効・無効の表示テキスト</summary>
        public string StatusText => IsDisabled ? "無効" : "有効";

        /// <summary>フォルダを開くコマンド（個別）</summary>
        public ICommand OpenFolderCommand { get; }

        public PluginItemViewModel()
        {
            OpenFolderCommand = new RelayCommand(() =>
            {
                if (!string.IsNullOrEmpty(FolderPath) && Directory.Exists(FolderPath))
                    Process.Start("explorer.exe", FolderPath);
            });
        }

        private bool isRetrievedMode;
        public bool IsRetrievedMode
        {
            get => isRetrievedMode;
            set { isRetrievedMode = value; OnPropertyChanged(nameof(DisplayName)); }
        }

        /// <summary>
        /// 表示名: info.json の portalName → InternalName → OriginalName の順で優先
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (HasInfoJson && !string.IsNullOrEmpty(InfoPortalName)) return InfoPortalName;
                if (!string.IsNullOrEmpty(InternalName)) return InternalName;
                return OriginalName;
            }
        }

        private bool isSelected;
        public bool IsSelected
        {
            get => isSelected;
            set { isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        private bool isTogglePending;
        public bool IsTogglePending
        {
            get => isTogglePending;
            set
            {
                isTogglePending = value;
                if (value) isPendingDelete = false;
                OnPropertyChanged(nameof(IsTogglePending));
                OnPropertyChanged(nameof(IsPendingDelete));
            }
        }

        private bool isPendingDelete;
        public bool IsPendingDelete
        {
            get => isPendingDelete;
            set
            {
                isPendingDelete = value;
                if (value) isTogglePending = false;
                OnPropertyChanged(nameof(IsPendingDelete));
                OnPropertyChanged(nameof(IsTogglePending));
            }
        }

        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // =========================================================================

    public class RelayCommand : ICommand
    {
        private readonly Action execute;
        public RelayCommand(Action execute) => this.execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}