using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WolvenKit.App.Models.AssetRelations;
using WolvenKit.Common.Model.Database;
using WolvenKit.Core.Interfaces;
using WolvenKit.RED4.Archive.IO;
using WolvenKit.RED4.Types;

namespace WolvenKit.App.Services;

/// <summary>Read-only, one-hop queries. The UI expands paths lazily instead of scanning the game.</summary>
public sealed class AssetRelationService
{
    public const int BranchLimit = 500;
    private readonly IAppArchiveManager _archives;
    private readonly IPluginService _plugins;

    public AssetRelationService(IAppArchiveManager archives, IPluginService plugins)
    {
        _archives = archives;
        _plugins = plugins;
    }

    public async Task<IReadOnlyList<AssetRelationNode>> LoadAsync(AssetRelationNode node, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return node.Direction == AssetRelationDirection.Uses
            ? ReadImports(node, token)
            : await ReadIncomingAsync(node, token);
    }

    private IReadOnlyList<AssetRelationNode> ReadImports(AssetRelationNode node, CancellationToken token)
    {
        if (node.File is not { } file)
        {
            return new[] { AssetRelationNode.Message("Asset unavailable; imports could not be inspected.") };
        }
        // Extract() is not cancellable. Bound memory and check cancellation immediately afterwards.
        if (file.Size > 128 * 1024 * 1024)
        {
            return new[] { AssetRelationNode.Message("Not scanned: asset exceeds the 128 MiB inspection limit.") };
        }
        using var stream = new MemoryStream();
        file.Extract(stream);
        token.ThrowIfCancellationRequested();
        stream.Position = 0;
        using var reader = new CR2WReader(stream);
        var status = reader.ReadFileInfo(out var info);
        if (status != EFileReadErrorCodes.NoError || info is null)
        {
            return new[] { AssetRelationNode.Message($"Imports unavailable: {status}. This view supports CR2W resources.") };
        }
        var paths = info.GetImports().Where(x => x != ResourcePath.Empty).Distinct().ToList();
        var result = new List<AssetRelationNode>();
        foreach (var path in paths.Take(BranchLimit))
        {
            token.ThrowIfCancellationRequested();
            var target = _archives.GetGameFile(path);
            result.Add(AssetRelationNode.Asset((ulong)path,
                path.GetResolvedText() ?? target?.Name ?? $"Hash {(ulong)path}", target, node.Ancestors));
        }
        result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label));
        if (paths.Count > BranchLimit)
        {
            result.Add(AssetRelationNode.Message($"Showing {BranchLimit} of {paths.Count} imports; remaining results omitted."));
        }
        if (paths.Count == 0)
        {
            result.Add(AssetRelationNode.Message("No imports in this file. Dynamic/string-based links are not covered."));
        }
        return result;
    }

    private async Task<IReadOnlyList<AssetRelationNode>> ReadIncomingAsync(AssetRelationNode node, CancellationToken token)
    {
        if (!_plugins.IsInstalled(EPlugin.wolvenkit_resources))
        {
            return new[] { AssetRelationNode.Message("Install WolvenKit Resources from Plugins to query incoming references.") };
        }
        await using var db = new RedDBContext();
        if (!File.Exists(db.DbPath) || db.Files is null)
        {
            return new[] { AssetRelationNode.Message("Resources index unavailable. Install or repair WolvenKit Resources.") };
        }
        var matches = await db.Files.AsNoTracking()
            .Where(x => x.Uses != null && x.Uses.Any(y => y.Hash == node.Hash))
            .OrderBy(x => x.Id)
            .Select(x => new { x.Hash, Archive = x.Archive!.Name, Source = x.Archive!.Source })
            .Take(BranchLimit + 1).ToListAsync(token);
        var result = new List<AssetRelationNode>();
        // Resolve the exact indexed archive, never silently substitute a mod with the same hash.
        var archives = _archives.Archives.Items.ToList();
        foreach (var match in matches.Take(BranchLimit))
        {
            token.ThrowIfCancellationRequested();
            IGameFile? file = null;
            foreach (var archive in archives.Where(x => x.Source.ToString() == match.Source &&
                (x.Name == match.Archive || x.ArchiveRelativePath == match.Archive)))
            {
                if (archive.Files.TryGetValue(match.Hash, out file))
                {
                    break;
                }
            }
            var path = ((ResourcePath)match.Hash).GetResolvedText() ?? file?.Name ?? $"Hash {match.Hash}";
            result.Add(AssetRelationNode.Asset(match.Hash, path, file, node.Ancestors,
                $"Indexed: {match.Source}: {match.Archive}"));
        }
        result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label));
        if (matches.Count > BranchLimit)
        {
            result.Add(AssetRelationNode.Message($"Results limited to {BranchLimit} indexed references."));
        }
        if (matches.Count == 0)
        {
            result.Add(AssetRelationNode.Message("No indexed references found. Index coverage may be incomplete or outdated."));
        }
        return result;
    }
}
