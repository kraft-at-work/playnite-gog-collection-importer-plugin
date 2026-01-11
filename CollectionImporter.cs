using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamCollectionImporter.Exceptions;

namespace SteamCollectionImporter
{
    public static class CollectionImporter
    {
        public static ImportedCollections ImportCollections()
        {
            var cloudStorageNamespace = LoadFiles();
            var collections = LoadCollections(cloudStorageNamespace);
            return TransformResults(collections);
        }

        private static ImportedCollections TransformResults(List<CollectionEntry> collectionEntries)
        {
            var collectionNames = new List<string>();
            var gameToCollection = new Dictionary<string, List<string>>();

            foreach (var collectionEntry in collectionEntries)
            {
                collectionNames.Add(collectionEntry.Name);
                foreach (var gameId in collectionEntry.GameIds)
                {
                    if (!gameToCollection.ContainsKey(gameId))
                    {
                        gameToCollection.Add(gameId, new List<string>());
                    }

                    var gameList = gameToCollection[gameId];
                    if (!gameList.Contains(collectionEntry.Name))
                    {
                        gameList.Add(collectionEntry.Name);
                    }
                }
            }

            collectionNames.Sort();

            return new ImportedCollections
            {
                CollectionNames = collectionNames,
                GameToCollection = gameToCollection
            };
        }

        private static List<CloudStorageNamespace> LoadFiles()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "SteamLibrary");
            if (assembly == null)
            {
                throw new SteamLibraryNotFoundException("SteamLibrary not found");
            }

            var steamType = assembly.GetType("SteamLibrary.Steam");
            if (steamType == null)
            {
                throw new SteamLibraryNotFoundException("SteamLibrary.Steam not found");
            }

            var installPathMethod = steamType.GetProperty("InstallationPath")?.GetGetMethod();
            if (installPathMethod == null)
            {
                throw new SteamLibraryNotFoundException("SteamLibrary.Steam.InstallationPath not found");
            }

            var installPathObj = installPathMethod.Invoke(null, null);
            if (!(installPathObj is string installPath))
            {
                throw new SteamLibraryNotFoundException(
                    "SteamLibrary.Steam.InstallationPath returned null or not a string");
            }

            var userdataFolder = Path.Combine(installPath, "userdata");
            var result = new List<CloudStorageNamespace>();
            if (Directory.Exists(userdataFolder))
            {
                result.AddRange(from userFolder in Directory.EnumerateDirectories(userdataFolder)
                    select Path.Combine(userFolder, "config", "cloudstorage", "cloud-storage-namespace-1.json")
                    into targetFile
                    where File.Exists(targetFile)
                    select JsonConvert.DeserializeObject<CloudStorageNamespace>(File.ReadAllText(targetFile)));
            }

            return result;
        }

        private static List<CollectionEntry> LoadCollections(List<CloudStorageNamespace> namespaceList)
        {
            var result = new List<CollectionEntry>();
            foreach (var namespaceObj in namespaceList)
            {
                foreach (var entry in namespaceObj)
                {
                    if (entry == null || entry.Count != 2)
                    {
                        continue;
                    }

                    var nameObj = entry[0];
                    if (!(nameObj is string name) || !name.StartsWith("user-collections"))
                    {
                        continue;
                    }

                    var gamesObj = entry[1];
                    if (!(gamesObj is JObject jObject))
                    {
                        continue;
                    }

                    var isDeletedToken = jObject["is_deleted"];
                    if (isDeletedToken != null && isDeletedToken.Value<bool>())
                    {
                        continue;
                    }

                    var value = jObject["value"];
                    if (value == null)
                    {
                        continue;
                    }

                    var collectionEntry = value.Value<string>();
                    var deserializeObject = JsonConvert.DeserializeObject<CollectionEntry>(collectionEntry);
                    if (deserializeObject == null || deserializeObject.FilterSpec != null ||
                        deserializeObject.GameIds?.Count == 0)
                    {
                        continue;
                    }

                    result.Add(deserializeObject);
                }
            }

            return result;
        }
    }

    internal class CloudStorageNamespace : List<CloudStorageNamespaceEntry>
    {
    }

    // ReSharper disable once ClassNeverInstantiated.Global
    internal class CloudStorageNamespaceEntry : List<object>
    {
    }

    internal class CollectionEntry
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("added")] public List<string> GameIds { get; set; }
        [JsonProperty("filterSpec")] public object FilterSpec { get; set; }

        public override string ToString()
        {
            return $"{nameof(Name)}: {Name}, {nameof(GameIds)}: {string.Join(",", GameIds)}";
        }
    }

    public class ImportedCollections
    {
        public List<string> CollectionNames { get; set; }
        public Dictionary<string, List<string>> GameToCollection { get; set; }
    }
}