using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using WolvenKit.App.Models.AssetRelations;
using WolvenKit.App.Services;
using WolvenKit.Core.Interfaces;

namespace WolvenKit.UnitTests.App.AssetRelations;

[TestClass]
public class AssetRelationTests
{
    private static IGameFile File(ulong hash, string archivePath = "base.archive")
    {
        var archive = new Mock<IGameArchive>();
        archive.SetupGet(x => x.ArchiveAbsolutePath).Returns(archivePath);
        archive.SetupGet(x => x.Name).Returns(archivePath);
        var file = new Mock<IGameFile>();
        file.SetupGet(x => x.Key).Returns(hash);
        file.Setup(x => x.GetArchive()).Returns(archive.Object);
        return file.Object;
    }

    [TestMethod]
    public void CycleTerminatesButAnotherArchiveWithTheSameHashIsExpandable()
    {
        var rootFile = File(1);
        var root = AssetRelationNode.Asset(1, "character.ent", rootFile);
        var cycle = AssetRelationNode.Asset(1, "character.ent", rootFile, root.Ancestors);
        Assert.AreEqual(0, cycle.Children.Count);
        var otherVersion = AssetRelationNode.Asset(1, "character.ent", File(1, "mod.archive"), root.Ancestors);
        Assert.AreEqual(2, otherVersion.Children.Count);
    }

    [TestMethod]
    public void SharedDependencyRetainsBothParentPathsWithoutMutatingAncestors()
    {
        var root = AssetRelationNode.Asset(1, "character.ent", File(1));
        var first = AssetRelationNode.Asset(2, "first.app", File(2), root.Ancestors);
        var second = AssetRelationNode.Asset(3, "second.app", File(3), root.Ancestors);
        var shared = File(4);
        var a = AssetRelationNode.Asset(4, "shared.mesh", shared, first.Ancestors);
        var b = AssetRelationNode.Asset(4, "shared.mesh", shared, second.Ancestors);
        Assert.AreEqual(2, a.Children.Count);
        Assert.AreEqual(2, b.Children.Count);
        Assert.AreEqual(1, root.Ancestors.Count);
        Assert.AreEqual(2, first.Ancestors.Count);
        Assert.IsFalse(a.Ancestors.SetEquals(b.Ancestors));
    }

    [TestMethod]
    public void UnavailableAssetRetainsItsHashAndReverseLookup()
    {
        var node = AssetRelationNode.Asset(123, "unknown.mesh", null);
        Assert.AreEqual(123UL, node.Hash);
        Assert.IsNull(node.File);
        Assert.AreEqual(1, node.Children.Count);
        Assert.AreEqual(AssetRelationDirection.UsedBy, node.Children.Single().Direction);
    }

    [TestMethod]
    public async Task MissingResourcesPluginDoesNotReportNoReferences()
    {
        var root = AssetRelationNode.Asset(1, "character.ent", File(1));
        var service = new AssetRelationService(Mock.Of<IAppArchiveManager>(), Mock.Of<IPluginService>());
        var result = await service.LoadAsync(root.Children.Single(x => x.Direction == AssetRelationDirection.UsedBy),
            CancellationToken.None);
        StringAssert.Contains(result.Single().Label, "Install WolvenKit Resources");
    }

    [TestMethod]
    public async Task CancellationPreventsExtraction()
    {
        var file = Mock.Get(File(1));
        var root = AssetRelationNode.Asset(1, "character.ent", file.Object);
        var service = new AssetRelationService(Mock.Of<IAppArchiveManager>(), Mock.Of<IPluginService>());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await service.LoadAsync(root.Children.First(), cancelled.Token);
            Assert.Fail("Expected cancellation");
        }
        catch (OperationCanceledException)
        {
            file.Verify(x => x.Extract(It.IsAny<Stream>()), Times.Never);
        }
    }
}
