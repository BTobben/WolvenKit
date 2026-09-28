using System.Collections.Generic;
using System.Collections.ObjectModel;
using WolvenKit.Core.Interfaces;

namespace WolvenKit.App.Models.AssetRelations;

public enum AssetRelationDirection { Uses, UsedBy }

/// <summary>A path occurrence, not a globally deduplicated asset: shared dependencies retain each parent.</summary>
public sealed class AssetRelationNode
{
    public string Label { get; init; } = "";
    public string Details { get; init; } = "";
    public IGameFile? File { get; init; }
    public ulong Hash { get; init; }
    public AssetRelationDirection? Direction { get; init; }
    public HashSet<string> Ancestors { get; init; } = new();
    public ObservableCollection<AssetRelationNode> Children { get; } = new();
    public bool Loaded { get; set; }
    public bool Loading { get; set; }

    public static string Identity(IGameFile file) => $"{file.GetArchive().ArchiveAbsolutePath}|{file.Key}";

    public static AssetRelationNode Message(string text) => new() { Label = text, Loaded = true };

    public static AssetRelationNode Asset(ulong hash, string path, IGameFile? file,
        HashSet<string>? ancestors = null, string? indexedArchive = null)
    {
        var visited = ancestors is null ? new HashSet<string>() : new HashSet<string>(ancestors);
        var cycle = file is not null && !visited.Add(Identity(file));
        var archive = file is null ? indexedArchive ?? "Not found in loaded archives" :
            $"{file.GetArchive().Source}: {file.GetArchive().Name}";
        var node = new AssetRelationNode
        {
            Hash = hash, File = file, Ancestors = visited,
            Label = path + (cycle ? " [already on this path]" : file is null ? " [unavailable]" : ""),
            Details = $"{path}\nHash: {hash}\nArchive: {archive}",
            Loaded = true
        };
        if (cycle)
        {
            return node;
        }
        if (file is not null)
        {
            node.Children.Add(Group(node, AssetRelationDirection.Uses, "Uses — live file imports"));
        }
        node.Children.Add(Group(node, AssetRelationDirection.UsedBy, "Used by — Resources index"));
        return node;
    }

    private static AssetRelationNode Group(AssetRelationNode asset, AssetRelationDirection direction, string label)
    {
        var node = new AssetRelationNode
        {
            Label = label, Details = asset.Details, Hash = asset.Hash,
            File = asset.File, Direction = direction, Ancestors = asset.Ancestors
        };
        node.Children.Add(Message("Expand to load relations…"));
        return node;
    }
}
