# Playnite GOG Collection Importer Plugin

Simple utility to import your GOG Galaxy tags/collections into Playnite as categories.

You can import the collections by using a main menu option or game context menu. Requires the
[GOG OSS library plugin](https://github.com/hawkeye116477/playnite-gog-oss-plugin) and GOG Galaxy 2.0 to be installed.

## How it works

Reads the GOG Galaxy SQLite database at `%PROGRAMDATA%\GOG.com\Galaxy\storage\galaxy-2.0.db` and maps
the tags assigned to your GOG games into Playnite categories.

> **Note:** The import overwrites existing categories on affected games.

## Credits

- [Playnite](https://playnite.link/) by [JosefNemec](https://github.com/JosefNemec)
- [playnite-gog-oss-plugin](https://github.com/hawkeye116477/playnite-gog-oss-plugin) by [hawkeye116477](https://github.com/hawkeye116477)
- Forked from [playnite-steam-collection-importer-plugin](https://github.com/Yalgrin/playnite-steam-collection-importer-plugin) by [Yalgrin](https://github.com/Yalgrin)
