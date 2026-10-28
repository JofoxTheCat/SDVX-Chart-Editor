using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>An undoable edit of a chart. Execute and Undo must be exact inverses.</summary>
public interface IEditCommand
{
    string Description { get; }
    void Execute(Chart chart);
    void Undo(Chart chart);
}

/// <summary>Several commands applied and undone as one step.</summary>
public sealed class CompositeCommand : IEditCommand
{
    private readonly List<IEditCommand> commands;

    public CompositeCommand(string description, IEnumerable<IEditCommand> commands)
    {
        Description = description;
        this.commands = commands.ToList();
    }

    public string Description { get; }
    public IReadOnlyList<IEditCommand> Commands => commands;

    public void Execute(Chart chart)
    {
        foreach (var c in commands) c.Execute(chart);
    }

    public void Undo(Chart chart)
    {
        for (int i = commands.Count - 1; i >= 0; i--) commands[i].Undo(chart);
    }
}

/// <summary>
/// Undo/redo history for one chart. Commands run through <see cref="Execute"/>; a transaction
/// (<see cref="BeginTransaction"/> / <see cref="Commit"/>) groups several into one undo step, e.g. a drag.
/// </summary>
public sealed class CommandStack
{
    private readonly List<IEditCommand> undo = new();
    private readonly List<IEditCommand> redo = new();
    private List<IEditCommand>? transaction;
    private string transactionName = "";
    private int savedAt;

    public CommandStack(Chart chart) => Chart = chart;

    public Chart Chart { get; }

    /// <summary>Maximum undo depth; the oldest steps are dropped. 0 = unlimited.</summary>
    public int Limit { get; set; } = 500;

    /// <summary>Raised after every execute / undo / redo / commit.</summary>
    public event Action? Changed;

    public bool CanUndo => undo.Count > 0 && transaction == null;
    public bool CanRedo => redo.Count > 0 && transaction == null;
    public string? UndoDescription => undo.Count > 0 ? undo[^1].Description : null;
    public string? RedoDescription => redo.Count > 0 ? redo[^1].Description : null;
    public IEnumerable<string> History => undo.Select(c => c.Description);

    /// <summary>True when the chart differs from the state at the last <see cref="MarkSaved"/>.</summary>
    public bool IsDirty => savedAt != undo.Count;

    public void MarkSaved() => savedAt = undo.Count;

    public void Execute(IEditCommand command)
    {
        command.Execute(Chart);
        if (transaction != null)
        {
            transaction.Add(command);
            return;
        }
        Push(command);
    }

    public void BeginTransaction(string description)
    {
        if (transaction != null) throw new InvalidOperationException("a transaction is already open");
        transaction = new List<IEditCommand>();
        transactionName = description;
    }

    public void Commit()
    {
        var t = transaction ?? throw new InvalidOperationException("no open transaction");
        transaction = null;
        if (t.Count > 0) Push(t.Count == 1 ? t[0] : new CompositeCommand(transactionName, t));
    }

    /// <summary>Undoes everything done since <see cref="BeginTransaction"/> and closes it.</summary>
    public void Rollback()
    {
        var t = transaction ?? throw new InvalidOperationException("no open transaction");
        transaction = null;
        for (int i = t.Count - 1; i >= 0; i--) t[i].Undo(Chart);
        Changed?.Invoke();
    }

    public bool Undo()
    {
        if (!CanUndo) return false;
        var c = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        c.Undo(Chart);
        redo.Add(c);
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        var c = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        c.Execute(Chart);
        undo.Add(c);
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
        savedAt = 0;
        Changed?.Invoke();
    }

    private void Push(IEditCommand command)
    {
        undo.Add(command);
        if (savedAt > undo.Count - 1) savedAt = -1; // the saved state can no longer be reached by redo
        redo.Clear();
        if (Limit > 0 && undo.Count > Limit)
        {
            undo.RemoveAt(0);
            savedAt--;
        }
        Changed?.Invoke();
    }
}
