using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using GogCollectionImporter.Exceptions;

namespace GogCollectionImporter
{
    // ReSharper disable once UnusedType.Global
    public class GogCollectionImporter : GenericPlugin
    {
        private static readonly Guid GogPluginId = Guid.Parse("03689811-3f33-4dfb-a121-2ee168fb9a5c");

        private static readonly ILogger Logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("8d5c7a3b-2e4f-4d6a-9b1c-7e3f5a2d8c4e");

        private IPlayniteAPI Api { get; }

        public GogCollectionImporter(IPlayniteAPI api) : base(api)
        {
            Api = api;
            Properties = new GenericPluginProperties
            {
                HasSettings = false
            };
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            var sectionName = ResourceProvider.GetString("LOC_KraftAtWork_GogCollectionImporter_Menu_SectionName");
            yield return new MainMenuItem
            {
                MenuSection = $"@{sectionName}",
                Description =
                    ResourceProvider.GetString("LOC_KraftAtWork_GogCollectionImporter_Menu_ImportCollectionsForAllGames"),
                Action = a => ImportGogCategories()
            };
            yield return new MainMenuItem
            {
                MenuSection = $"@{sectionName}",
                Description =
                    ResourceProvider.GetString(
                        "LOC_KraftAtWork_GogCollectionImporter_Menu_ImportCollectionsForFilteredGames"),
                Action = a => ImportGogCategories(GetFilteredGameIds())
            };
            yield return new MainMenuItem
            {
                MenuSection = $"@{sectionName}",
                Description =
                    ResourceProvider.GetString(
                        "LOC_KraftAtWork_GogCollectionImporter_Menu_ImportCollectionsForSelectedGames"),
                Action = a => ImportGogCategories(GetSelectedGameIds())
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                MenuSection = ResourceProvider.GetString("LOC_KraftAtWork_GogCollectionImporter_Menu_SectionName"),
                Description = ResourceProvider.GetString("LOC_KraftAtWork_GogCollectionImporter_Menu_ImportCollections"),
                Action = a => ImportGogCategories(args.Games.Select(g => g.Id).ToList())
            };
        }

        private List<Guid> GetFilteredGameIds()
        {
            return PlayniteApi.MainView.UIDispatcher.Invoke(() =>
            {
                return PlayniteApi.MainView.FilteredGames.ConvertAll(g => g.Id);
            });
        }

        private List<Guid> GetSelectedGameIds()
        {
            return PlayniteApi.MainView.UIDispatcher.Invoke(() =>
            {
                return PlayniteApi.MainView.SelectedGames.ToList().ConvertAll(g => g.Id);
            });
        }

        private void ImportGogCategories(List<Guid> gameIds = null)
        {
            var caption = ResourceProvider.GetString("LOC_KraftAtWork_GogCollectionImporter_Menu_SectionName");
            try
            {
                var messageBoxResult = Api.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOC_KraftAtWork_GogCollectionImporter_Confirmation"),
                    caption,
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (messageBoxResult != MessageBoxResult.OK)
                {
                    return;
                }

                var importedCollections = ImportCategories();

                var addedCategories = 0;
                var changedGames = 0;

                var db = Api.Database;
                using (db.BufferedUpdate())
                {
                    var categoryNameToId = PrepareCategories(importedCollections, ref addedCategories);
                    ModifyGames(gameIds, importedCollections, categoryNameToId, ref changedGames);
                }

                Api.Dialogs.ShowMessage(FormatSuccessMessage(addedCategories, changedGames), caption,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (GogGalaxyNotFoundException e)
            {
                Logger.Error(e, "Failed to import collections");
                Api.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("LOC_KraftAtWork_GogCollectionImporter_NoGogGalaxy"), caption);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to import collections");
                Api.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("LOC_KraftAtWork_GogCollectionImporter_Error"), caption);
            }
        }

        private static ImportedCollections ImportCategories()
        {
            var importedCollections = CollectionImporter.ImportCollections();
            Logger.Info($"Imported {importedCollections.CollectionNames.Count} collections");
            return importedCollections;
        }

        private Dictionary<string, Guid> PrepareCategories(ImportedCollections importedCollections,
            ref int addedCategories)
        {
            var db = Api.Database;
            var categoryNameToId = db.Categories.ToDictionary(category => category.Name, category => category.Id);

            foreach (var importedCollectionName in importedCollections.CollectionNames.Where(
                         importedCollectionName => !categoryNameToId.ContainsKey(importedCollectionName)))
            {
                Logger.Info($"Adding new category: {importedCollectionName}");
                db.Categories.Add(new Category(importedCollectionName));

                var category = db.Categories.FirstOrDefault(c => c.Name == importedCollectionName);
                if (category != null)
                {
                    categoryNameToId.Add(category.Name, category.Id);
                    addedCategories++;
                }
                else
                {
                    throw new Exception("Failed to add new category");
                }
            }

            return categoryNameToId;
        }

        private void ModifyGames(List<Guid> gameIds, ImportedCollections importedCollections,
            Dictionary<string, Guid> categoryNameToId, ref int changedGames)
        {
            var db = Api.Database;
            foreach (var game in db.Games)
            {
                if (gameIds != null && !gameIds.Contains(game.Id))
                {
                    continue;
                }

                importedCollections.GameToCollection.TryGetValue(game.GameId, out var collectionNames);
                if (collectionNames == null)
                {
                    collectionNames = new List<string>();
                }

                var categoryIds = new HashSet<Guid>();
                foreach (var collectionName in collectionNames)
                {
                    if (!categoryNameToId.TryGetValue(collectionName, out var categoryId))
                    {
                        throw new Exception($"Failed to find category for {collectionName}!");
                    }

                    categoryIds.Add(categoryId);
                }

                var gameCategoryIds = game.CategoryIds ?? new List<Guid>();
                if (categoryIds.Count == gameCategoryIds.Count && categoryIds.SetEquals(gameCategoryIds))
                {
                    continue;
                }

                Logger.Info($"Changing categories for {game.Name} (game id: {game.GameId})");
                game.CategoryIds = categoryIds.ToList();
                db.Games.Update(game);

                changedGames++;
            }
        }

        private static string FormatSuccessMessage(int addedCategories, int changedGames)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append(ResourceProvider.GetString("LOC_KraftAtWork_GogCollectionImporter_Done"));
            if (addedCategories <= 0 && changedGames <= 0)
            {
                stringBuilder.Append(" ");
                stringBuilder.Append(ResourceProvider.GetString("LOC_KraftAtWork_GogCollectionImporter_NoChanges"));
            }
            else
            {
                if (addedCategories > 0)
                {
                    stringBuilder.Append(" ");
                    stringBuilder.Append(string.Format(
                        ResourceProvider.GetString(
                            "LOC_KraftAtWork_GogCollectionImporter_AddedNumberOfCollections"),
                        addedCategories));
                }

                if (changedGames > 0)
                {
                    stringBuilder.Append(" ");
                    stringBuilder.Append(string.Format(
                        ResourceProvider.GetString("LOC_KraftAtWork_GogCollectionImporter_ChangedNumberOfGames"),
                        changedGames));
                }
            }

            return stringBuilder.ToString();
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
        }
    }
}
