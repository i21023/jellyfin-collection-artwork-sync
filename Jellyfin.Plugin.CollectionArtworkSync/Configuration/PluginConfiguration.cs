using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.CollectionArtworkSync.Configuration;

public enum OnlyWriteChangedArtworks{
    Hash,
    TimeStamp,
    AlwaysWrite
}

public class PluginConfiguration : BasePluginConfiguration{
    public PluginConfiguration(){
        ExternalArtworkDirectoryPath = "";
        OnlyWriteChangedArtworks = OnlyWriteChangedArtworks.Hash;
    }

    public string ExternalArtworkDirectoryPath {get; set;}

    public OnlyWriteChangedArtworks OnlyWriteChangedArtworks {get; set;}
}