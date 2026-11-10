using System.Collections.ObjectModel;
using System.ComponentModel;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>Stable sidebar rows: editing a title must never reset the ListView's source.</summary>
public sealed class AgentSessionRow(AgentConversation conversation) : INotifyPropertyChanged
{
    public string Id { get; } = conversation.Id;
    public string Title { get; private set; } = conversation.Title;
    public override string ToString() => Title;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Update(AgentConversation conversation)
    {
        if (Title != conversation.Title) { Title = conversation.Title; PropertyChanged?.Invoke(this, new(nameof(Title))); }
    }
}

internal sealed class AgentSessionList : ObservableCollection<AgentSessionRow>
{
    internal void Merge(IEnumerable<AgentConversation> conversations)
    {
        var desired = conversations.DistinctBy(c => c.Id, StringComparer.Ordinal).ToArray();
        var ids = desired.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        for (var i = Count - 1; i >= 0; i--) if (!ids.Contains(this[i].Id)) RemoveAt(i);
        var current = this.ToDictionary(r => r.Id, StringComparer.Ordinal);
        for (var i = 0; i < desired.Length; i++)
        {
            var conversation = desired[i];
            if (current.TryGetValue(conversation.Id, out var row))
            {
                if (!ReferenceEquals(this[i], row)) Move(IndexOf(row), i);
                row.Update(conversation);
            }
            else Insert(i, new(conversation));
        }
    }
}
