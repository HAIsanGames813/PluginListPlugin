using System;
using YukkuriMovieMaker.Plugin;

namespace PluginList
{
    public class PluginList : IToolPlugin
    {
        public string Name => "プラグイン管理リスト";
        public Type ViewModelType => typeof(PluginListViewModel);
        public Type ViewType => typeof(PluginListView);
    }
}