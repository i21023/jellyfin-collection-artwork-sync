using System.ComponentModel;
using System.Globalization;
using Jellyfin.Plugin.CollectionArtworkSync.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.CollectionArtworkSync;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "Collection Artwork Sync";

    public override string Description => "Automatically sync external movie collection artworks to Jellyfin";

    public override Guid Id => new("33ecce10-f862-49b0-aa8d-b45baf5a978b");

    public static Plugin? Instance {get; private set;}

    public PluginConfiguration PluginConfiguration => Configuration;

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return 
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }

}