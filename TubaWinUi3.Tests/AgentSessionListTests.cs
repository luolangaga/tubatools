using System.Collections.Specialized;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

public sealed class AgentSessionListTests
{
    [Fact]
    public void EditingDraftTitle_PreservesEveryRow_WithoutCollectionEvents()
    {
        var rows = new AgentSessionList();
        rows.Merge([new("draft", "草稿"), new("saved", "已有会话")]);
        var draft = rows[0]; var saved = rows[1];
        var changes = new List<string?>(); var listChanges = 0;
        draft.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        rows.CollectionChanged += (_, _) => listChanges++;
        for (var i = 1; i <= 100; i++) rows.Merge([new("draft", "草稿 " + i), new("saved", "已有会话")]);
        Assert.Same(draft, rows[0]); Assert.Same(saved, rows[1]);
        Assert.Equal(0, listChanges); Assert.Equal(100, changes.Count);
        Assert.All(changes, name => Assert.Equal(nameof(AgentSessionRow.Title), name));
        changes.Clear(); rows.Merge([new("draft", "草稿 100"), new("saved", "已有会话")]);
        Assert.Empty(changes);
    }

    [Fact]
    public void HistoryChanges_InsertMoveAndRemove_WithoutResettingSurvivingRows()
    {
        var rows = new AgentSessionList(); rows.Merge([new("one", "One"), new("two", "Two"), new("three", "Three")]);
        var first = rows[0]; var second = rows[1]; var third = rows[2];
        var actions = new List<NotifyCollectionChangedAction>(); rows.CollectionChanged += (_, e) => actions.Add(e.Action);
        rows.Merge([new("new", "New"), new("three", "Renamed"), new("two", "Two")]);
        Assert.Equal(new[] { "new", "three", "two" }, rows.Select(r => r.Id));
        Assert.Same(third, rows[1]); Assert.Same(second, rows[2]); Assert.DoesNotContain(first, rows);
        Assert.Equal("Renamed", third.Title);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
        Assert.Contains(NotifyCollectionChangedAction.Add, actions); Assert.Contains(NotifyCollectionChangedAction.Move, actions); Assert.Contains(NotifyCollectionChangedAction.Remove, actions);
        rows.Merge([]); Assert.Empty(rows); Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void DuplicateHistoryEntries_DoNotDuplicateContainers()
    {
        var rows = new AgentSessionList(); rows.Merge([new("one", "First"), new("one", "Duplicate"), new("two", "Two")]);
        Assert.Equal(new[] { "one", "two" }, rows.Select(r => r.Id)); Assert.Equal("First", rows[0].Title);
    }
}
