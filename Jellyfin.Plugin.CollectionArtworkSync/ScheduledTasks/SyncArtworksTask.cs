using System.Security.Cryptography;
using ICU4N.Text;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.CollectionArtworkSync.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.CollectionArtworkSync.ScheduledTasks;

public class CollectionArtworkSyncManager : IScheduledTask, IDisposable
{

    private readonly ILogger<CollectionArtworkSyncManager> _logger;
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;

    public CollectionArtworkSyncManager(
        ILogger<CollectionArtworkSyncManager> logger,
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
        _logger = logger;
        _libraryManager = libraryManager;
        _providerManager = providerManager;
    }

    public string Name => "Sync movie collection artworks";

    public string Key => "SyncMovieCollectionArtworksTask";

    public string Description => "Sync external collection artworks to jellyfin";

    public string Category => "Maintenance";

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        Dictionary<string, List<string>> mappings = new(){
            {"poster", new List<string>{"poster", "folder", "cover", "default", "movie"}},
            {"backdrop", new List<string>{"backdrop", "fanart", "background", "art"}},
            {"logo", new List<string>{"logo", "clearlogo"}},
            {"disc", new List<string>{"disc", "discart"}}
        };

        _logger.LogInformation("Starting synchronization task");

        string jellyfinCollectionsPath="data/collections";

        string externalArtworkDirectoryPath = Plugin.Instance!.Configuration.ExternalArtworkDirectoryPath;
        OnlyWriteChangedArtworks onlyWriteChangedArtworks = Plugin.Instance.Configuration.OnlyWriteChangedArtworks;

        _logger.LogInformation($"only write changed artworks: {onlyWriteChangedArtworks}");
        if (!Directory.Exists(externalArtworkDirectoryPath)){
            throw new Exception($"{externalArtworkDirectoryPath} is not a valid directory");
        }

        string[] moviesetFolderPaths = await GetAllSubDirectoriesOfDirectory(jellyfinCollectionsPath);

        HashSet<string> updatedCollections = new();

        foreach (string moviesetFolderPath in moviesetFolderPaths){
            _logger.LogInformation($"Executing '{moviesetFolderPath}'");
            string moviesetName = Path.GetFileName(moviesetFolderPath);
            string externalPath = $"{externalArtworkDirectoryPath}/{moviesetName}";
            externalPath = externalPath.Replace(" [boxset]", "");
            if (!Directory.Exists(externalPath)){
                _logger.LogWarning($"No external movie set artwork folder found for movieset {moviesetName} in folder {externalPath}");
                continue;
            }
            string[] artworkFilePaths = await GetAllSubFilesOfDirectory(externalPath);
            _logger.LogInformation($"Found {artworkFilePaths.Count()} artwork files for {moviesetName}");

            foreach (string artworkFilePath in artworkFilePaths){
                _logger.LogInformation($"Processing artwork file {artworkFilePath}");
                string artworkFileName = Path.GetFileName(artworkFilePath);
                string artworkFileNameWithoutExtension = Path.GetFileNameWithoutExtension(artworkFileName);
                var jellyfinArtworkFiles = GetMatchingFilesInDirectory(moviesetFolderPath, [.. mappings.GetValueOrDefault(artworkFileNameWithoutExtension, new List<string>{artworkFileNameWithoutExtension})]);
                _logger.LogInformation($"Found {jellyfinArtworkFiles.Count()} matching files in jellyfin collections folder {moviesetFolderPath} for {artworkFileNameWithoutExtension}");
                string? jellyfinArtworkFilePath = null;
                // at least on artwork file was found for the artwork type in the jellyfin collection folder
                if (jellyfinArtworkFiles.Count() == 1){
                    _logger.LogInformation($"Found {jellyfinArtworkFiles[0]} in jellyfin collections folder");
                    jellyfinArtworkFilePath = Path.Combine(moviesetFolderPath, jellyfinArtworkFiles[0]);
                    _logger.LogInformation($"external file {artworkFilePath} has size {new FileInfo(artworkFilePath).Length} and jellyfin file {jellyfinArtworkFilePath} has size {new FileInfo(jellyfinArtworkFilePath).Length}");
                    if (new FileInfo(artworkFilePath).Length != new FileInfo(jellyfinArtworkFilePath).Length){
                        jellyfinArtworkFilePath = Path.Combine(moviesetFolderPath, artworkFileName);
                        CopyArtworkFile(artworkFilePath, jellyfinArtworkFilePath);
                        updatedCollections.Add(moviesetName);
                        continue;
                    }

                    if (onlyWriteChangedArtworks == OnlyWriteChangedArtworks.Hash){
                        string externalFileHash = ComputeFileHash(artworkFilePath);
                        string jellyfinFileHash = ComputeFileHash(jellyfinArtworkFilePath);
                        _logger.LogInformation($"External file hash: {externalFileHash}, Jellyfin file hash: {jellyfinFileHash}");
                        if (externalFileHash == jellyfinFileHash){
                            _logger.LogInformation($"Most recent state of {artworkFileName} is already present in jellyfin collections folder");
                            RenameFileIfNecessary(artworkFilePath, jellyfinArtworkFilePath);
                            continue;
                        }
                    }
                    else if(onlyWriteChangedArtworks == OnlyWriteChangedArtworks.TimeStamp)
                    {                    
                        DateTime externalFileLastModified=File.GetLastWriteTimeUtc(artworkFilePath);
                        DateTime jellyfinFileLastModified=File.GetLastWriteTimeUtc(jellyfinArtworkFilePath);
                        if (externalFileLastModified <= jellyfinFileLastModified){
                            _logger.LogInformation($"Most recent state of {artworkFileName} is already present in jellyfin collections folder");
                            RenameFileIfNecessary(artworkFilePath, jellyfinArtworkFilePath);
                            continue; 
                        }
                    }
                }
                else if (jellyfinArtworkFiles.Count() > 1){
                    _logger.LogWarning($"Multiple files found for {artworkFileName} in jellyfin collections folder. Deleting all files to avoid conflicts");
                    jellyfinArtworkFiles.Select(f => Path.Combine(moviesetFolderPath, f)).ToList().ForEach(File.Delete);
                }
                if (jellyfinArtworkFilePath == null){
                    jellyfinArtworkFilePath = Path.Combine(moviesetFolderPath, artworkFileName);
                }
                CopyArtworkFile(artworkFilePath, jellyfinArtworkFilePath);
                updatedCollections.Add(moviesetName);
            }
        }
      var boxsets = _libraryManager.GetItemList(new
            InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.BoxSet],
                Recursive = true
            }); 

        _logger.LogInformation($"Found {boxsets.Count()} collection folders:", boxsets.Select(c => c.Name));
        foreach (var boxset in boxsets)
        {
            if (updatedCollections.Contains(boxset.Name)) continue;
            _logger.LogInformation($"Updating Metadata for {boxset.Name}");
            _providerManager.QueueRefresh(boxset.Id, new MetadataRefreshOptions(new DirectoryService(_fileSystem)){
                ImageRefreshMode = MetadataRefreshMode.FullRefresh 
            }, RefreshPriority.High);
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerInterval, IntervalTicks = TimeSpan.FromHours(24).Ticks }];
    }

    public async Task<string[]> GetAllSubDirectoriesOfDirectory(string path){
        return await Task.Run(() => 
        {
            if (Directory.Exists(path))
            {
                string[] dirs = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
                return dirs;
            }
            else
            {
                return Array.Empty<string>();
            }
        });
    }
    
    public async Task<string[]> GetAllSubFilesOfDirectory(string path){
        return await Task.Run(() => 
        {
            if (Directory.Exists(path))
            {
                string[] files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
                return files;
            }
            else
            {
                return Array.Empty<string>();
            }
        });
    }
    static string ComputeFileHash(string filePath)
    {
        using (FileStream fs = File.OpenRead(filePath))
        using (MD5 md5 = MD5.Create())
        {
            byte[] hashBytes = md5.ComputeHash(fs);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }

    static List<string> GetMatchingFilesInDirectory(string directoryPath, string[] possibleArtworkFileNames)
    {
        List<string> matchingFiles = new();
        foreach (string possibleArtworkFileName in possibleArtworkFileNames){
            var pattern = possibleArtworkFileName + ".*";
            matchingFiles.AddRange(Directory.GetFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly).Select(f => Path.GetFileName(f)));
        }
        return matchingFiles;
    }

    private void CopyArtworkFile(string artworkFilePath, string jellyfinArtworkFilePath)
    {
        _logger.LogInformation($"Copying Artwork file {artworkFilePath} to {jellyfinArtworkFilePath}");
        File.Copy(artworkFilePath, jellyfinArtworkFilePath!, overwrite: true);
    }

    private void RenameFileIfNecessary(string externalFilePath, string jellyfinFilePath)
    {
        string externalFileName = Path.GetFileName(externalFilePath);
        string jellyfinFileName = Path.GetFileName(jellyfinFilePath);
        if (externalFileName == jellyfinFileName)
        {
            return;
        }

        string jellyfinDirPath = Path.GetDirectoryName(jellyfinFilePath)!;
        string newJellyfinFilePath = Path.Combine(jellyfinDirPath, externalFileName);
        _logger.LogInformation($"Renaming file {jellyfinFilePath} to {newJellyfinFilePath}");
        File.Move(jellyfinFilePath, newJellyfinFilePath);
    }
}