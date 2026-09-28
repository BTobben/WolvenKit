using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using WolvenKit.App.Models.AssetRelations;
using WolvenKit.App.Services;
using WolvenKit.Core.Interfaces;

namespace WolvenKit.Views.Tools;

public partial class AssetRelationsWindow : MetroWindow
{
    private readonly AssetRelationService _service;
    private readonly Action<IGameFile> _open;
    private readonly CancellationTokenSource _closed = new();
    private readonly SemaphoreSlim _queryLock = new(1, 1);

    public AssetRelationsWindow(IGameFile root, AssetRelationService service, Action<IGameFile> open)
    {
        InitializeComponent();
        _service = service;
        _open = open;
        RelationTree.Items.Add(AssetRelationNode.Asset(root.Key, root.Name, root));
        Closed += (_, _) => _closed.Cancel();
    }

    private async void OnExpanded(object sender, RoutedEventArgs e)
    {
        // Expanded bubbles through ancestors: only query the actual expanded group.
        if (!ReferenceEquals(sender, e.OriginalSource) || sender is not TreeViewItem { DataContext: AssetRelationNode node } ||
            node.Direction is null || node.Loaded || node.Loading)
        {
            return;
        }
        node.Loading = true;
        node.Children.Clear();
        node.Children.Add(AssetRelationNode.Message("Loading…"));
        var token = _closed.Token;
        try
        {
            await _queryLock.WaitAsync(token);
            try
            {
                var children = await Task.Run(() => _service.LoadAsync(node, token), token);
                token.ThrowIfCancellationRequested();
                node.Children.Clear();
                foreach (var child in children)
                {
                    node.Children.Add(child);
                }
                node.Loaded = true;
            }
            finally
            {
                _queryLock.Release();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Window closed; do not publish stale results.
        }
        catch (Exception ex)
        {
            node.Children.Clear();
            node.Children.Add(AssetRelationNode.Message("Unable to load relations. Collapse and expand to retry."));
            StatusText.Text = ex.Message;
        }
        finally
        {
            node.Loading = false;
        }
    }

    private void OnSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is AssetRelationNode node)
        {
            DetailsBox.Text = node.Details;
            OpenButton.IsEnabled = node.Direction is null && node.File is not null;
        }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (RelationTree.SelectedItem is AssetRelationNode { Direction: null, File: { } file })
        {
            _open(file);
        }
    }
}
