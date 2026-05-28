using AssetData.Parser.Editor.Models;

namespace AssetData.Parser.Editor.Services;

/// <summary>Represents an undoable/redoable action.</summary>
public interface IUndoableAction
{
    void Undo();
    void Redo();
    string Description { get; }
}

/// <summary>Property change action for undo/redo.</summary>
public sealed class PropertyChangeAction<T> : IUndoableAction
{
    private readonly EditorNode _node;
    private readonly string _propertyName;
    private readonly T _oldValue;
    private readonly T _newValue;
    private readonly Action<T> _setter;

    public string Description => $"Change {_propertyName} on {_node.Name}";

    public PropertyChangeAction(EditorNode node, string propertyName, T oldValue, T newValue, Action<T> setter)
    {
        _node = node;
        _propertyName = propertyName;
        _oldValue = oldValue;
        _newValue = newValue;
        _setter = setter;
    }

    public void Undo() => _setter(_oldValue);
    public void Redo() => _setter(_newValue);
}

/// <summary>Modern undo/redo service with action batching support.</summary>
public sealed class UndoRedoService
{
    private readonly Stack<IUndoableAction> _undoStack = new();
    private readonly Stack<IUndoableAction> _redoStack = new();
    private bool _isApplying;

    public int MaxHistory { get; set; } = 1000;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public event Action? StateChanged;
    public bool IsApplying => _isApplying;

    public void Push(IUndoableAction action)
    {
        if (_isApplying) return;

        _undoStack.Push(action);
        _redoStack.Clear();

        if (_undoStack.Count > MaxHistory)
        {
            var temp = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = 0; i < MaxHistory; i++)
                _undoStack.Push(temp[temp.Length - 1 - i]);
        }

        StateChanged?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;

        _isApplying = true;
        try
        {
            var action = _undoStack.Pop();
            action.Undo();
            _redoStack.Push(action);
        }
        finally
        {
            _isApplying = false;
            StateChanged?.Invoke();
        }
    }

    public void Redo()
    {
        if (!CanRedo) return;

        _isApplying = true;
        try
        {
            var action = _redoStack.Pop();
            action.Redo();
            _undoStack.Push(action);
        }
        finally
        {
            _isApplying = false;
            StateChanged?.Invoke();
        }
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke();
    }

    public void TrackStringChange(StringNode node, string oldValue, string newValue)
    {
        if (oldValue == newValue) return;
        Push(new PropertyChangeAction<string>(node, "Value", oldValue, newValue, v => node.Value = v));
    }

    public void TrackNumberChange(NumberNode node, double oldValue, double newValue)
    {
        if (Math.Abs(oldValue - newValue) < double.Epsilon) return;
        Push(new PropertyChangeAction<double>(node, "Value", oldValue, newValue, v => node.Value = v));
    }

    public void TrackBooleanChange(BooleanNode node, bool oldValue, bool newValue)
    {
        if (oldValue == newValue) return;
        Push(new PropertyChangeAction<bool>(node, "Value", oldValue, newValue, v => node.Value = v));
    }

    public void TrackNameChange(EditorNode node, string oldName, string newName)
    {
        if (oldName == newName) return;
        Push(new PropertyChangeAction<string>(node, "Name", oldName, newName, v => node.Name = v));
    }
}
