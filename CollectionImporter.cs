using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using GogCollectionImporter.Exceptions;

namespace GogCollectionImporter
{
    public static class CollectionImporter
    {
        private static string GogGalaxyDbPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "GOG.com", "Galaxy", "storage", "galaxy-2.0.db");

        public static ImportedCollections ImportCollections()
        {
            var dbPath = GogGalaxyDbPath;
            if (!File.Exists(dbPath))
            {
                throw new GogGalaxyNotFoundException($"GOG Galaxy database not found at: {dbPath}");
            }

            return LoadFromDatabase(dbPath);
        }

        private static ImportedCollections LoadFromDatabase(string dbPath)
        {
            var gameToCollection = new Dictionary<string, List<string>>();
            var collectionNames = new HashSet<string>();

            var connectionString = $"Data Source={dbPath};Read Only=True;Version=3;";
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT REPLACE(releaseKey, 'gog_', '') AS gogProductId, tag " +
                        "FROM UserReleaseTags " +
                        "WHERE releaseKey LIKE 'gog_%' " +
                        "  AND tag IS NOT NULL " +
                        "  AND tag <> ''";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var gogProductId = reader.GetString(0);
                            var tag = reader.GetString(1);

                            collectionNames.Add(tag);

                            if (!gameToCollection.ContainsKey(gogProductId))
                            {
                                gameToCollection[gogProductId] = new List<string>();
                            }

                            if (!gameToCollection[gogProductId].Contains(tag))
                            {
                                gameToCollection[gogProductId].Add(tag);
                            }
                        }
                    }
                }
            }

            var sortedNames = new List<string>(collectionNames);
            sortedNames.Sort();

            return new ImportedCollections
            {
                CollectionNames = sortedNames,
                GameToCollection = gameToCollection
            };
        }
    }

    public class ImportedCollections
    {
        public List<string> CollectionNames { get; set; }
        public Dictionary<string, List<string>> GameToCollection { get; set; }
    }
}
