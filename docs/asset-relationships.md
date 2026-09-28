# Asset relationships (initial implementation)

In the Asset Browser, select a character's `.ent` file (or any other resource),
right-click and choose **Explore asset relationships**. Expand the root, then:

- **Uses — live file imports** follows outgoing CR2W imports. Expand each child
  to follow further relationships, for example entity → appearance → mesh → material.
  These are file-level dependencies, not a claim that every imported asset is active
  in the currently selected appearance.
- **Used by — Resources index** queries the existing WolvenKit Resources database.
  Each reference preserves its indexed archive identity. The database relates depot
  hashes, not a particular override; results can be incomplete or outdated.
- Select an asset to see its full path, hash and archive. **Open selected asset**
  opens that exact resolved file without adding it to the project.

The tree retains every parent chain, including shared dependencies. Cycles stop
when an archive/hash pair repeats on the same path. Unavailable targets remain
visible and still permit reverse lookup by hash. Queries happen lazily, off the UI
thread, with one query at a time per window. Closing the window cancels queued
work and suppresses late results. Extraction itself cannot be interrupted.

## Scope and limits

This is a read-only relationship browser, not an editor or a complete character
catalog. Start from an asset; character-name/TweakDB search and a free-form node
canvas are future extensions. No game data or project files are modified.

Outgoing targets follow the existing `GetGameFile` resolution (project, mods,
base game); the selected root stays the exact file chosen in Asset Browser.
This does not independently validate the game's runtime mod load order.
Incoming results resolve only the matching indexed source archive.

Only CR2W import-table links are read. This excludes dynamic/string-based links,
TweakDB associations, embedded relationships not represented by imports,
per-appearance activation and unsaved editor changes. An empty import table does
not prove that an asset has no other relationships. The Resources plugin is only
required for incoming queries. Missing index, unavailable file, read error and
empty result are displayed separately.

Each branch shows at most 500 results and explicitly reports truncation. Files
larger than 128 MiB are not extracted for inspection. Reopen the window to refresh
already loaded branches after changing files or the Resources index.

## Validation

Unit tests cover path-local cycles, same-hash archive variants, shared dependency
paths, unavailable targets, missing Resources plugin and cancellation before I/O.
The implementation environment had no .NET SDK or Windows/WPF runtime: these
tests and the application build have not been executed there.

On a Windows development machine with the SDK from `global.json`:

```powershell
dotnet build WolvenKit/WolvenKit.csproj -c Debug -p:Platform=x64
dotnet test Tests/WolvenKit.UnitTests/WolvenKit.UnitTests.csproj -c Debug -p:Platform=x64 --filter FullyQualifiedName~AssetRelationTests
```

Manual acceptance:

1. Open a character `.ent`; follow an `.app` → `.mesh` chain and verify archive
   and depot path against the existing editor. Shared children keep both parents.
2. Expand Used by on a shared resource. Compare with Find files using this;
   validate duplicate hashes across base/mod archives are not silently substituted.
3. Repeat without Resources installed: outgoing imports still work and incoming
   queries explain the missing plugin. A corrupt index shows a retryable error.
4. Inspect a missing target and non-CR2W resource; neither appears as a successful
   empty scan. Close the window during a query and confirm the UI stays responsive.
5. Open a related asset and confirm no project file is added or modified.
