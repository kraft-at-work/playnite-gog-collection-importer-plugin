using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SteamCollectionImporter.Exceptions;

namespace SteamCollectionImporter
{
    // ReSharper disable once UnusedType.Global
    public class SteamCollectionImporter : GenericPlugin
    {
        private static readonly Guid SteamPluginId = Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab");

        private static readonly ILogger Logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("1e6cc38b-3610-4f52-9630-c7950f3424f3");

        private IPlayniteAPI Api { get; }

        public SteamCollectionImporter(IPlayniteAPI api) : base(api)
        {
            Api = api;
            Properties = new GenericPluginProperties
            {
                HasSettings = false
            };
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            var sectionName = ResourceProvider.GetString("LOC_Yalgrin_SteamCollectionImporter_Menu_SectionName");
            yield return new MainMenuItem
            {
                MenuSection = $"@{sectionName}",
                Description =
                    ResourceProvider.GetString("LOC_Yalgrin_SteamCollectionImporter_Menu_ImportCollectionsForAllGames"),
                Action = a => ImportSteamCategories()
            };
            yield return new MainMenuItem
            {
                MenuSection = $"@{sectionName}",
                Description =
                    ResourceProvider.GetString(
                        "LOC_Yalgrin_SteamCollectionImporter_Menu_ImportCollectionsForFilteredGames"),
                Action = a => ImportSteamCategories(GetFilteredGameIds())
            };
            yield return new MainMenuItem
            {
                MenuSection = $"@{sectionName}",
                Description =
                    ResourceProvider.GetString(
                        "LOC_Yalgrin_SteamCollectionImporter_Menu_ImportCollectionsForSelectedGames"),
                Action = a => ImportSteamCategories(GetSelectedGameIds())
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            if (args.Games.All(g => g.PluginId != SteamPluginId))
            {
                yield break;
            }

            yield return new GameMenuItem
            {
                MenuSection = ResourceProvider.GetString("LOC_Yalgrin_SteamCollectionImporter_Menu_SectionName"),
                Description = ResourceProvider.GetString("LOC_Yalgrin_SteamCollectionImporter_Menu_ImportCollections"),
                Action = a => ImportSteamCategories(args.Games.Select(g => g.Id).ToList())
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

        private void ImportSteamCategories(List<Guid> gameIds = null)
        {
            var caption = ResourceProvider.GetString("LOC_Yalgrin_SteamCollectionImporter_Menu_SectionName");
            try
            {
                var messageBoxResult = Api.Dialogs.ShowMessage(
                    ResourceProvider.GetString("LOC_Yalgrin_SteamCollectionImporter_Confirmation"),
                    caption,
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (messageBoxResult != MessageBoxResult.OK)
                {
                    return;
                }

                var importedCategories = ImportCategories();

                var addedCategories = 0;
                var changedGames = 0;

                var db = Api.Database;
                using (db.BufferedUpdate())
                {
                    var categoryNameToId = PrepareCategories(importedCategories, ref addedCategories);
                    ModifyGames(gameIds, importedCategories, categoryNameToId, ref changedGames);
                }

                Api.Dialogs.ShowMessage(FormatSuccessMessage(addedCategories, changedGames), caption,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (SteamLibraryNotFoundException e)
            {
                Logger.Error(e, "Failed to import collections");
                Api.Dialogs.ShowErrorMessage(
                    ResourceProvider.GetString("LOC_Yalgrin_SteamCollectionImporter_NoSteamLibrary"), caption);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to import collections");
                Api.Dialogs.ShowErrorMessage(ResourceProvider.GetString("LOC_Yalgrin_SteamCollectionImporter_Error"),
                    caption);
            }
        }

        private static ImportedCollections ImportCategories()
        {
            var importCollections = CollectionImporter.ImportCollections();
            Logger.Info($"Imported {importCollections.CollectionNames.Count} collections");
            return importCollections;
        }

        private Dictionary<string, Guid> PrepareCategories(ImportedCollections importCollections,
            ref int addedCategories)
        {
            var db = Api.Database;
            var categoryNameToId = db.Categories.ToDictionary(category => category.Name, category => category.Id);

            foreach (var importedCollectionName in importCollections.CollectionNames.Where(importedCollectionName =>
                         !categoryNameToId.ContainsKey(importedCollectionName)))
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

        private void ModifyGames(List<Guid> gameIds, ImportedCollections importedCategories,
            Dictionary<string, Guid> categoryNameToId, ref int changedGames)
        {
            var db = Api.Database;
            foreach (var game in db.Games)
            {
                if (game.PluginId != SteamPluginId || (gameIds != null && !gameIds.Contains(game.Id)))
                {
                    continue;
                }

                importedCategories.GameToCollection.TryGetValue(game.GameId, out var collectionNames);
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

                Logger.Info($"Changing categories for {game.Name} (steam app id: {game.GameId})");
                game.CategoryIds = categoryIds.ToList();
                db.Games.Update(game);

                changedGames++;
            }
        }

        private static string FormatSuccessMessage(int addedCategories, int changedGames)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append(ResourceProvider.GetString("LOC_Yalgrin_SteamCollectionImporter_Done"));
            if (addedCategories <= 0 && changedGames <= 0)
            {
                stringBuilder.Append(" ");
                stringBuilder.Append(ResourceProvider.GetString("LOC_Yalgrin_SteamCollectionImporter_NoChanges"));
            }
            else
            {
                if (addedCategories > 0)
                {
                    stringBuilder.Append(" ");
                    stringBuilder.Append(string.Format(
                        ResourceProvider.GetString("LOC_Yalgrin_SteamCollectionImporter_AddedNumberOfCollections"),
                        addedCategories));
                }

                if (changedGames > 0)
                {
                    stringBuilder.Append(" ");
                    stringBuilder.Append(string.Format(
                        ResourceProvider.GetString("LOC_Yalgrin_SteamCollectionImporter_ChangedNumberOfGames"),
                        changedGames));
                }
            }

            var messageBoxText = stringBuilder.ToString();
            return messageBoxText;
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            //not used
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            //not used
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            //not used
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            //not used
        }

        public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
        {
            //not used
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            //not used
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            //not used
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            //not used
        }
    }
}