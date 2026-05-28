using AssetData.Parser.Model;

namespace AssetData.Parser.Editor.Models;

/// <summary>
/// Builds the editor-facing observable tree (<see cref="EditorNode"/>) from the parser's
/// immutable L1 <see cref="AssetValue"/> tree. One-pass recursive copy; no L1 ↔ L2 binding
/// after construction — the editor owns its own state from here on.
/// </summary>
public static class EditorTreeBuilder
{
    public static EditorNode Build(AssetValue value)
    {
        var node = Convert(value);
        if (node is { } container)
            PopulateChildren(value, container);
        return node;
    }

    private static EditorNode Convert(AssetValue value) => value switch
    {
        StructValue s => new StructNode
        {
            Name = s.Name,
            TypeName = s.TypeName,
            BinaryOffset = s.BinaryOffset
        },
        ArrayValue a => new ArrayNode
        {
            Name = a.Name,
            ElementType = a.ElementType,
            BinaryOffset = a.BinaryOffset
        },
        StringValue str => new StringNode
        {
            Name = str.Name,
            Value = str.Value,
            NodeKind = MapKind(str.NodeKind),
            BinaryOffset = str.BinaryOffset
        },
        LocalizedStringValue l => new LocalizedStringNode
        {
            Name = l.Name,
            PrimaryValue = l.PrimaryValue,
            SecondaryValue = l.SecondaryValue,
            BinaryOffset = l.BinaryOffset
        },
        NumberValue n => new NumberNode
        {
            Name = n.Name,
            Value = n.Value,
            OriginalType = n.OriginalType,
            Format = n.Format,
            BinaryOffset = n.BinaryOffset
        },
        BoolValue b => new BooleanNode
        {
            Name = b.Name,
            Value = b.Value,
            BinaryOffset = b.BinaryOffset
        },
        EnumValue e => new EnumNode
        {
            Name = e.Name,
            EnumType = e.EnumType,
            RawValue = e.RawValue,
            ResolvedName = e.ResolvedName,
            BinaryOffset = e.BinaryOffset
        },
        VectorValue v => new VectorNode
        {
            Name = v.Name,
            VectorType = v.VectorType,
            X = v.X,
            Y = v.Y,
            Z = v.Z,
            W = v.W,
            BinaryOffset = v.BinaryOffset
        },
        NullValue n => new NullNode
        {
            Name = n.Name,
            BinaryOffset = n.BinaryOffset
        },
        _ => throw new InvalidOperationException($"Unknown L1 value type: {value.GetType().Name}")
    };

    private static void PopulateChildren(AssetValue source, EditorNode target)
    {
        foreach (var child in source.Children)
        {
            var childNode = Convert(child);
            target.AddChild(childNode);
            if (child.Children.Count > 0)
                PopulateChildren(child, childNode);
        }
    }

    private static EditorNodeKind MapKind(AssetValueKind kind) => kind switch
    {
        AssetValueKind.Asset  => EditorNodeKind.Asset,
        AssetValueKind.String => EditorNodeKind.String,
        _ => EditorNodeKind.String
    };
}

/// <summary>
/// Reverse converter: edits made in the L2 <see cref="EditorNode"/> tree are projected back into a
/// fresh L1 <see cref="AssetValue"/> tree for serialization (XML export). Symmetric to
/// <see cref="EditorTreeBuilder.Build"/>; the L1 tree it produces is throw-away.
/// </summary>
public static class EditorToValue
{
    public static AssetValue Convert(EditorNode node) => node switch
    {
        StructNode s => BuildStruct(s),
        ArrayNode a  => BuildArray(a),
        StringNode str => new StringValue
        {
            Name = str.Name,
            Value = str.Value,
            NodeKind = str.NodeKind == EditorNodeKind.Asset ? AssetValueKind.Asset : AssetValueKind.String,
            BinaryOffset = str.BinaryOffset
        },
        LocalizedStringNode l => new LocalizedStringValue
        {
            Name = l.Name,
            PrimaryValue = l.PrimaryValue,
            SecondaryValue = l.SecondaryValue,
            BinaryOffset = l.BinaryOffset
        },
        NumberNode n => new NumberValue
        {
            Name = n.Name,
            Value = n.Value,
            OriginalType = n.OriginalType,
            Format = n.Format,
            BinaryOffset = n.BinaryOffset
        },
        BooleanNode b => new BoolValue
        {
            Name = b.Name,
            Value = b.Value,
            BinaryOffset = b.BinaryOffset
        },
        EnumNode e => new EnumValue
        {
            Name = e.Name,
            EnumType = e.EnumType,
            RawValue = e.RawValue,
            ResolvedName = e.ResolvedName,
            BinaryOffset = e.BinaryOffset
        },
        VectorNode v => new VectorValue
        {
            Name = v.Name,
            VectorType = v.VectorType,
            X = v.X, Y = v.Y, Z = v.Z, W = v.W,
            BinaryOffset = v.BinaryOffset
        },
        NullNode n => new NullValue
        {
            Name = n.Name,
            BinaryOffset = n.BinaryOffset
        },
        _ => throw new InvalidOperationException($"Unknown editor node: {node.GetType().Name}")
    };

    private static StructValue BuildStruct(StructNode s)
    {
        var v = new StructValue { Name = s.Name, TypeName = s.TypeName, BinaryOffset = s.BinaryOffset };
        foreach (var child in s.Children) v.Add(Convert(child));
        return v;
    }

    private static ArrayValue BuildArray(ArrayNode a)
    {
        var v = new ArrayValue { Name = a.Name, ElementType = a.ElementType, BinaryOffset = a.BinaryOffset };
        foreach (var child in a.Children) v.Add(Convert(child));
        return v;
    }
}
