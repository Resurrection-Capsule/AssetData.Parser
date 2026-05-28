namespace AssetData.Parser;

/// <summary>
/// Phase-1 adapter: builds a hash-keyed <see cref="TypeRegistry"/> from the legacy
/// string-keyed <see cref="StructDefinition"/> catalog, WITHOUT changing the fluent DSL yet.
/// The model fixes happen here at conversion time:
///   • inline struct (legacy <c>DataType.Struct</c>) → a real <c>TypeHash = Fnv1a(structName)</c>,
///     so it resolves through <c>FindTypeByHash</c> like everything else (kills the fabricated D2 sentinel);
///   • array/nullable element types (legacy <c>ElementType</c> string) → <c>ElementHash</c> (D5).
/// Field rows and the parser's current behavior are untouched — this only produces the new model
/// alongside the old one so the deserializer rewrite (Phase 1b) can switch over.
/// </summary>
public static class RegistryBuilder
{
    public sealed record Result(TypeRegistry Registry, IReadOnlyList<string> Issues);

    public static Result Build(IReadOnlyDictionary<string, StructDefinition> structs)
    {
        var registry = new TypeRegistry();
        foreach (var def in structs.Values)
        {
            var fields = new List<FieldDescriptor>(def.Fields.Count);
            foreach (var f in def.Fields)
                fields.Add(ConvertField(f));
            registry.Register(new TypeDescriptor(def.Name, def.Size, fields));
        }

        // Coverage pass: every struct-shaped reference must resolve to a registered type.
        var issues = new List<string>();
        foreach (var def in structs.Values)
        {
            foreach (var f in def.Fields)
            {
                string? unresolved = f.Type switch
                {
                    // inline struct: TypeHash must resolve
                    DataType.Struct when !registry.Contains(WireHash.Fnv1a(f.ElementType!))
                        => f.ElementType,
                    // nullable: ElementType must resolve
                    DataType.Nullable when !registry.Contains(WireHash.Fnv1a(f.ElementType!))
                        => f.ElementType,
                    // array-of-struct (element type is not a known DataType name)
                    DataType.Array when f.ElementType != null
                                        && !Enum.TryParse<DataType>(f.ElementType, true, out _)
                                        && !registry.Contains(WireHash.Fnv1a(f.ElementType))
                        => f.ElementType,
                    _ => null
                };

                if (unresolved != null)
                    issues.Add($"{def.Name}.{f.Name} → unresolved struct '{unresolved}'");
            }
        }

        return new Result(registry, issues);
    }

    private static FieldDescriptor ConvertField(FieldDefinition f)
    {
        uint typeHash = f.Type == DataType.Struct
            ? WireHash.Fnv1a(f.ElementType!)        // inline struct resolves by hash (D2 fix)
            : (uint)f.Type;                         // DataType values ARE the wire hashes

        uint elementHash = f.Type switch
        {
            DataType.Array    => WireHash.ResolveElementHash(f.ElementType),
            DataType.Nullable => WireHash.Fnv1a(f.ElementType!),
            _                 => 0u
        };

        return new FieldDescriptor(
            f.Name, typeHash, f.Offset, elementHash, f.CountOffset, f.BufferSize, f.EnumType);
    }
}
