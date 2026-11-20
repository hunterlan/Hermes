using Hermes.Core.Models;

namespace Hermes.App.Services;

/// <summary>How workspace items are named at the start of a storage message.</summary>
public static class StorageSubjects
{
    /// <summary>How a collection is named at the start of a storage message: <c>Collection "Pets"</c>.</summary>
    public static string Collection(CollectionModel collection) => $"Collection \"{collection.Name}\"";

    /// <summary>How a folder is named in a storage message about its collection's file.</summary>
    public static string Folder(FolderModel folder, CollectionModel collection) =>
        $"Folder \"{folder.Name}\" in {InCollection(collection)}";

    /// <summary>How a request is named in a storage message about its collection's file.</summary>
    public static string Request(HttpRequestModel request, CollectionModel collection) =>
        $"Request \"{request.Name}\" in {InCollection(collection)}";

    /// <summary>How an environment is named at the start of a storage message: <c>Environment "Dev"</c>.</summary>
    public static string Environment(EnvironmentModel environment) => $"Environment \"{environment.Name}\"";

    private static string InCollection(CollectionModel collection) => $"collection \"{collection.Name}\"";
}
