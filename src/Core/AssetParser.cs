using System.Reflection;
using System.Text;
using AssetData.Parser.Model;

namespace AssetData.Parser;

/// <summary>
/// Sequential blob data reader. Cursor advances automatically as data is read.
/// Optimized with Span for performance.
/// </summary>
public sealed class BlobReader
{
    private readonly byte[] _data;
    private readonly int _blobStart;
    private int _cursor;

    public BlobReader(byte[] data, int headerSize)
    {
        _data = data;
        _blobStart = headerSize;
        _cursor = headerSize;
    }

    public int Position => _cursor;
    public ReadOnlySpan<byte> Data => _data;

    public string ReadString()
    {
        int start = _cursor;
        while (_cursor < _data.Length && _data[_cursor] != 0)
            _cursor++;

        var result = Encoding.UTF8.GetString(_data.AsSpan(start, _cursor - start));
        if (_cursor < _data.Length) _cursor++;
        return result;
    }

    public int ReserveArray(int elementSize, int count)
    {
        int start = _cursor;
        _cursor += elementSize * count;
        return start;
    }

    public int ReserveStruct(int structSize)
    {
        int start = _cursor;
        _cursor += structSize;
        return start;
    }

    public bool HasData(int requiredBytes = 1) => _cursor + requiredBytes <= _data.Length;
}

/// <summary>
/// Darkspore binary asset parser. Produces an immutable L1 <see cref="AssetValue"/> tree
/// (parser-only model, no INPC). Editor wraps it with its own observable adapter.
/// Based on reverse engineering of Darkspore.exe parser functions.
/// </summary>
public sealed class AssetParser
{
    private readonly Dictionary<string, StructDefinition> _globalStructs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EnumDefinition> _globalEnums = new(StringComparer.OrdinalIgnoreCase);

    private TypeRegistry _registry = null!;

    private byte[] _data = null!;
    private BlobReader _blob = null!;

    /// <summary>Hash-keyed type registry — the game-faithful identity table.</summary>
    public TypeRegistry Registry => _registry;

    /// <summary>Unresolved struct references found while building <see cref="Registry"/> (should be empty).</summary>
    public IReadOnlyList<string> RegistryIssues { get; private set; } = [];

    /// <summary>Enable console logging for debugging.</summary>
    public bool EnableLogging { get; set; }

    /// <summary>Show binary offsets in logs.</summary>
    public bool ShowOffsets { get; set; }

    /// <summary>Get all registered struct definitions (for schema inspection).</summary>
    public IReadOnlyDictionary<string, StructDefinition> Structs => _globalStructs;

    /// <summary>Get all registered enum definitions (for schema inspection).</summary>
    public IReadOnlyDictionary<string, EnumDefinition> Enums => _globalEnums;

    /// <summary>Get file type by extension. Infers from struct with matching name.</summary>
    public FileTypeInfo? GetFileType(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        var structDef = _globalStructs.GetValueOrDefault(ext);
        if (structDef == null) return null;
        return new FileTypeInfo(ext, structDef.Name, structDef.Size);
    }

    public IEnumerable<string> SupportedTypes => _globalStructs.Keys;

    public AssetParser()
    {
        InitializeCatalogs();
    }

    private void InitializeCatalogs()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var catalogTypes = assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(AssetCatalog)) && !t.IsAbstract);

        foreach (var type in catalogTypes)
        {
            var catalog = (AssetCatalog)Activator.CreateInstance(type)!;
            MergeCatalog(catalog);
        }
        ResolveEnumReferences();

        var build = RegistryBuilder.Build(_globalStructs);
        _registry = build.Registry;
        RegistryIssues = build.Issues;
    }

    private void ResolveEnumReferences()
    {
        foreach (var def in _globalStructs.Values)
        {
            var fieldsList = (List<FieldDefinition>)def.Fields;

            for (int i = 0; i < fieldsList.Count; i++)
            {
                var field = fieldsList[i];
                bool isEnum = field.Type == DataType.Enum;
                bool isEnumArray = field.Type == DataType.Array && field.ElementType == "Enum";

                if ((!isEnum && !isEnumArray) || field.EnumType != null)
                    continue;

                string? resolvedEnum = null;

                var specificName = $"{def.Name}.{field.Name}";
                if (_globalEnums.ContainsKey(specificName))
                {
                    resolvedEnum = specificName;
                }
                else if (_globalEnums.ContainsKey(field.Name))
                {
                    resolvedEnum = field.Name;
                }
                else
                {
                    var pascalName = ToPascalCase(field.Name);
                    if (_globalEnums.ContainsKey(pascalName))
                        resolvedEnum = pascalName;
                }

                if (resolvedEnum != null)
                    fieldsList[i] = field with { EnumType = resolvedEnum };
            }
        }
    }

    private static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s) || char.IsUpper(s[0])) return s;
        return char.ToUpper(s[0]) + s.Substring(1);
    }

    private void MergeCatalog(AssetCatalog catalog)
    {
        foreach (var kvp in catalog.Structs)
            _globalStructs.TryAdd(kvp.Key, kvp.Value);

        foreach (var kvp in catalog.Enums)
            _globalEnums.TryAdd(kvp.Key, kvp.Value);
    }

    /// <summary>Parse a binary asset file and return the root L1 value.</summary>
    public AssetValue ParseFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        var fileType = GetFileType(extension)
            ?? throw new ArgumentException($"Unknown file type: {extension}");

        var data = File.ReadAllBytes(filePath);
        return Parse(data, fileType.RootStruct, fileType.HeaderSize);
    }

    /// <summary>Parse binary data and return the root L1 value.</summary>
    public AssetValue Parse(byte[] data, string rootStructName, int headerSize)
    {
        if (!_globalStructs.TryGetValue(rootStructName, out var structDef))
            throw new ArgumentException($"Unknown struct: {rootStructName}");

        _data = data;
        _blob = new BlobReader(data, headerSize);

        var root = new StructValue
        {
            Name = rootStructName.ToLowerInvariant(),
            TypeName = structDef.Name,
            BinaryOffset = 0
        };

        ParseStruct(root, structDef, baseOffset: 0);
        return root;
    }

    /// <summary>Parse binary data from a stream.</summary>
    public AssetValue Parse(Stream stream, string rootStructName, int headerSize)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.ToArray(), rootStructName, headerSize);
    }

    private void ParseStruct(StructValue parent, StructDefinition structDef, int baseOffset)
    {
        foreach (var field in structDef.Fields)
        {
            var value = ParseField(field, baseOffset + field.Offset, structDef.Name);
            if (value != null)
                parent.Add(value);
        }
    }

    private AssetValue? ParseField(FieldDefinition field, int offset, string parentStructName)
    {
        try
        {
            // Game-faithful dispatch (mirrors AssetParser::DeserializeObject 0x009cd2c0):
            // the field's type hash either resolves to a registered struct (recurse) or is a
            // sentinel / value-type handled inline. Inline structs (legacy DataType.Struct)
            // carry their element name as the hash source — D2 fix: no fabricated sentinel,
            // just "FindTypeByHash returned a type ⇒ recurse", the game's first check.
            uint typeHash = field.Type == DataType.Struct
                ? WireHash.Fnv1a(field.ElementType!)
                : (uint)field.Type;

            if (_registry.FindTypeByHash(typeHash) is not null)
                return ParseInlineStructField(field, offset);

            return typeHash switch
            {
                WireHash.Bool => new BoolValue
                {
                    Name = field.Name,
                    Value = ReadUInt32(offset) != 0,
                    BinaryOffset = offset
                },
                WireHash.Int or WireHash.Int32 => new NumberValue
                {
                    Name = field.Name,
                    Value = ReadInt32(offset),
                    OriginalType = NumericType.Int32,
                    BinaryOffset = offset
                },
                WireHash.UInt32 => new NumberValue
                {
                    Name = field.Name,
                    Value = ReadUInt32(offset),
                    OriginalType = NumericType.UInt32,
                    BinaryOffset = offset
                },
                WireHash.HashId => new NumberValue
                {
                    Name = field.Name,
                    Value = ReadUInt32(offset),
                    OriginalType = NumericType.HashId,
                    Format = NumberFormat.Hex,
                    BinaryOffset = offset
                },
                WireHash.ObjId => new NumberValue
                {
                    Name = field.Name,
                    Value = ReadUInt32(offset),
                    OriginalType = NumericType.ObjId,
                    Format = NumberFormat.Hex,
                    BinaryOffset = offset
                },
                WireHash.UInt16 => new NumberValue
                {
                    Name = field.Name,
                    Value = ReadUInt16(offset),
                    OriginalType = NumericType.UInt16,
                    BinaryOffset = offset
                },
                WireHash.UInt8 => new NumberValue
                {
                    Name = field.Name,
                    Value = ReadUInt8(offset),
                    OriginalType = NumericType.UInt8,
                    BinaryOffset = offset
                },
                WireHash.Float => new NumberValue
                {
                    Name = field.Name,
                    Value = ReadFloat(offset),
                    OriginalType = NumericType.Float,
                    Format = NumberFormat.Float,
                    BinaryOffset = offset
                },
                WireHash.Int64 => new NumberValue
                {
                    Name = field.Name,
                    Value = ReadInt64(offset),
                    OriginalType = NumericType.Int64,
                    BinaryOffset = offset
                },
                WireHash.UInt64 => new NumberValue
                {
                    Name = field.Name,
                    Value = ReadUInt64(offset),
                    OriginalType = NumericType.UInt64,
                    BinaryOffset = offset
                },
                WireHash.Enum => ParseEnumField(field, offset),

                WireHash.Vector2 => new VectorValue
                {
                    Name = field.Name,
                    VectorType = VectorType.Vector2,
                    X = ReadFloat(offset),
                    Y = ReadFloat(offset + 4),
                    BinaryOffset = offset
                },
                WireHash.Vector3 => new VectorValue
                {
                    Name = field.Name,
                    VectorType = VectorType.Vector3,
                    X = ReadFloat(offset),
                    Y = ReadFloat(offset + 4),
                    Z = ReadFloat(offset + 8),
                    BinaryOffset = offset
                },
                WireHash.Vector4 => new VectorValue
                {
                    Name = field.Name,
                    VectorType = VectorType.Vector4,
                    X = ReadFloat(offset),
                    Y = ReadFloat(offset + 4),
                    Z = ReadFloat(offset + 8),
                    W = ReadFloat(offset + 12),
                    BinaryOffset = offset
                },
                WireHash.Orientation => new VectorValue
                {
                    Name = field.Name,
                    VectorType = VectorType.Orientation,
                    X = ReadFloat(offset),
                    Y = ReadFloat(offset + 4),
                    Z = ReadFloat(offset + 8),
                    W = ReadFloat(offset + 12),
                    BinaryOffset = offset
                },

                WireHash.Key => ParseKeyField(field, offset),
                WireHash.Char => ParseCharField(field, offset),
                WireHash.CharPtr => ParseCharPtrField(field, offset),
                WireHash.Asset => ParseAssetField(field, offset),
                WireHash.LocalizedAssetString => ParseLocalizedAssetStringField(field, offset),

                WireHash.Array => ParseArrayField(field, offset),
                WireHash.Nullable => ParseNullableField(field, offset),

                _ => null
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Error parsing '{field.Name}' in '{parentStructName}' at 0x{offset:X}: {ex.Message}", ex);
        }
    }

    private EnumValue ParseEnumField(FieldDefinition field, int offset)
    {
        var rawValue = ReadUInt32(offset);
        string? resolvedName = null;

        if (field.EnumType != null && _globalEnums.TryGetValue(field.EnumType, out var enumDef))
            resolvedName = enumDef.GetName(rawValue);

        return new EnumValue
        {
            Name = field.Name,
            EnumType = field.EnumType ?? "",
            RawValue = rawValue,
            ResolvedName = resolvedName ?? "",
            BinaryOffset = offset
        };
    }

    private AssetValue? ParseKeyField(FieldDefinition field, int offset)
    {
        uint indicator = ReadUInt32(offset);
        if (indicator == 0) return null;

        return new StringValue
        {
            Name = field.Name,
            Value = _blob.ReadString(),
            NodeKind = AssetValueKind.Asset,
            BinaryOffset = offset
        };
    }

    private AssetValue? ParseCharField(FieldDefinition field, int offset)
    {
        if (field.BufferSize > 0)
        {
            return new StringValue
            {
                Name = field.Name,
                Value = ReadInlineString(offset, field.BufferSize),
                NodeKind = AssetValueKind.String,
                BinaryOffset = offset
            };
        }

        uint indicator = ReadUInt32(offset);
        if (indicator == 0) return null;

        return new StringValue
        {
            Name = field.Name,
            Value = _blob.ReadString(),
            NodeKind = AssetValueKind.String,
            BinaryOffset = offset
        };
    }

    private AssetValue? ParseCharPtrField(FieldDefinition field, int offset)
    {
        uint indicator = ReadUInt32(offset);
        if (indicator == 0) return null;

        return new StringValue
        {
            Name = field.Name,
            Value = _blob.ReadString(),
            NodeKind = AssetValueKind.String,
            BinaryOffset = offset
        };
    }

    private AssetValue? ParseAssetField(FieldDefinition field, int offset)
    {
        uint indicator = ReadUInt32(offset);
        if (indicator == 0) return null;

        return new StringValue
        {
            Name = field.Name,
            Value = _blob.ReadString(),
            NodeKind = AssetValueKind.Asset,
            BinaryOffset = offset
        };
    }

    private AssetValue? ParseLocalizedAssetStringField(FieldDefinition field, int offset)
    {
        uint indicator1 = ReadUInt32(offset);
        uint indicator2 = ReadUInt32(offset + 4);

        if (indicator1 == 0 && indicator2 == 0) return null;

        string primaryValue = indicator1 != 0 ? _blob.ReadString() : "";
        string secondaryValue = indicator2 != 0 ? _blob.ReadString() : "";

        return new LocalizedStringValue
        {
            Name = field.Name,
            PrimaryValue = primaryValue,
            SecondaryValue = secondaryValue,
            BinaryOffset = offset
        };
    }

    private ArrayValue ParseArrayField(FieldDefinition field, int offset)
    {
        uint hasValue = ReadUInt32(offset);
        int count = ReadInt32(offset + field.CountOffset);

        var array = new ArrayValue
        {
            Name = field.Name,
            ElementType = field.ElementType ?? "unknown",
            BinaryOffset = offset
        };

        if (hasValue == 0 || count <= 0)
            return array;

        if (count > 1_000_000)
            throw new InvalidOperationException($"Array '{field.Name}' has invalid count: {count}");

        uint elementHash = WireHash.ResolveElementHash(field.ElementType);

        if (_registry.FindTypeByHash(elementHash) is TypeDescriptor elementDesc)
        {
            var elementStructDef = _globalStructs[elementDesc.Name];
            int arrayStart = _blob.ReserveArray(elementStructDef.Size, count);

            for (int i = 0; i < count; i++)
            {
                int elemOffset = arrayStart + (i * elementStructDef.Size);
                var entry = new StructValue
                {
                    Name = $"[{i}]",
                    TypeName = elementDesc.Name,
                    BinaryOffset = elemOffset
                };
                ParseStruct(entry, elementStructDef, elemOffset);
                array.Add(entry);
            }
            return array;
        }

        bool isLocalized = elementHash == WireHash.LocalizedAssetString;
        bool isDynamicString =
            elementHash == WireHash.Key      ||
            elementHash == WireHash.Char     ||
            elementHash == WireHash.CharPtr  ||
            elementHash == WireHash.Asset    ||
            isLocalized;

        if (isDynamicString)
        {
            int indicatorSize = isLocalized ? 8 : 4;
            int indicatorStart = _blob.ReserveArray(indicatorSize, count);

            for (int i = 0; i < count; i++)
            {
                int indicatorOffset = indicatorStart + (i * indicatorSize);
                AssetValue entry;

                if (isLocalized)
                {
                    uint ind1 = ReadUInt32(indicatorOffset);
                    uint ind2 = ReadUInt32(indicatorOffset + 4);
                    string primary   = ind1 != 0 ? _blob.ReadString() : "";
                    string secondary = ind2 != 0 ? _blob.ReadString() : "";
                    entry = new LocalizedStringValue
                    {
                        Name = $"[{i}]",
                        PrimaryValue = primary,
                        SecondaryValue = secondary,
                        BinaryOffset = indicatorOffset
                    };
                }
                else
                {
                    uint indicator = ReadUInt32(indicatorOffset);
                    string value = indicator != 0 ? _blob.ReadString() : "";
                    entry = new StringValue
                    {
                        Name = $"[{i}]",
                        Value = value,
                        NodeKind = elementHash == WireHash.Asset ? AssetValueKind.Asset : AssetValueKind.String,
                        BinaryOffset = indicatorOffset
                    };
                }
                array.Add(entry);
            }
            return array;
        }

        int elementSize = GetWireSize(elementHash);
        if (elementSize == 0)
            throw new InvalidOperationException($"Unknown element type: {field.ElementType}");

        int arrayStart2 = _blob.ReserveArray(elementSize, count);
        for (int i = 0; i < count; i++)
        {
            int elemOffset = arrayStart2 + (i * elementSize);
            var entry = IsVectorHash(elementHash)
                ? CreateVectorValue($"[{i}]", elementHash, elemOffset)
                : CreatePrimitiveValue($"[{i}]", elementHash, elemOffset, field.EnumType);
            array.Add(entry);
        }

        return array;
    }

    private AssetValue ParseNullableField(FieldDefinition field, int offset)
    {
        uint hasValue = ReadUInt32(offset);
        if (hasValue == 0)
        {
            return new NullValue
            {
                Name = field.Name,
                BinaryOffset = offset
            };
        }

        uint elementHash = WireHash.Fnv1a(field.ElementType!);
        if (_registry.FindTypeByHash(elementHash) is not TypeDescriptor desc)
            throw new InvalidOperationException($"Unknown struct for nullable: {field.ElementType}");

        var structDef = _globalStructs[desc.Name];
        int structStart = _blob.ReserveStruct(structDef.Size);

        var node = new StructValue
        {
            Name = field.Name,
            TypeName = desc.Name,
            BinaryOffset = structStart
        };

        ParseStruct(node, structDef, structStart);
        return node;
    }

    private StructValue ParseInlineStructField(FieldDefinition field, int offset)
    {
        uint elementHash = WireHash.Fnv1a(field.ElementType!);
        if (_registry.FindTypeByHash(elementHash) is not TypeDescriptor desc)
            throw new InvalidOperationException($"Unknown struct for inline: {field.ElementType}");

        var structDef = _globalStructs[desc.Name];
        var node = new StructValue
        {
            Name = field.Name,
            TypeName = desc.Name,
            BinaryOffset = offset
        };

        ParseStruct(node, structDef, offset);
        return node;
    }

    private static bool IsVectorHash(uint hash) =>
        hash == WireHash.Vector2 || hash == WireHash.Vector3 ||
        hash == WireHash.Vector4 || hash == WireHash.Orientation;

    private static int GetWireSize(uint hash) => hash switch
    {
        WireHash.Bool   or WireHash.Int   or WireHash.Int32  or
        WireHash.UInt32 or WireHash.HashId or WireHash.ObjId or
        WireHash.Float  or WireHash.Enum                          => 4,
        WireHash.UInt16                                           => 2,
        WireHash.UInt8                                            => 1,
        WireHash.Int64  or WireHash.UInt64                        => 8,
        WireHash.Vector2                                          => 8,
        WireHash.Vector3                                          => 12,
        WireHash.Vector4 or WireHash.Orientation                  => 16,
        _                                                         => 0
    };

    private VectorValue CreateVectorValue(string name, uint hash, int offset) => hash switch
    {
        WireHash.Vector2 => new VectorValue
        {
            Name = name,
            VectorType = VectorType.Vector2,
            X = ReadFloat(offset),
            Y = ReadFloat(offset + 4),
            BinaryOffset = offset
        },
        WireHash.Vector3 => new VectorValue
        {
            Name = name,
            VectorType = VectorType.Vector3,
            X = ReadFloat(offset),
            Y = ReadFloat(offset + 4),
            Z = ReadFloat(offset + 8),
            BinaryOffset = offset
        },
        WireHash.Vector4 => new VectorValue
        {
            Name = name,
            VectorType = VectorType.Vector4,
            X = ReadFloat(offset),
            Y = ReadFloat(offset + 4),
            Z = ReadFloat(offset + 8),
            W = ReadFloat(offset + 12),
            BinaryOffset = offset
        },
        WireHash.Orientation => new VectorValue
        {
            Name = name,
            VectorType = VectorType.Orientation,
            X = ReadFloat(offset),
            Y = ReadFloat(offset + 4),
            Z = ReadFloat(offset + 8),
            W = ReadFloat(offset + 12),
            BinaryOffset = offset
        },
        _ => throw new InvalidOperationException($"Not a vector hash: 0x{hash:X8}")
    };

    private AssetValue CreatePrimitiveValue(string name, uint hash, int offset, string? enumType) => hash switch
    {
        WireHash.Bool => new BoolValue
        {
            Name = name,
            Value = ReadUInt32(offset) != 0,
            BinaryOffset = offset
        },
        WireHash.Int or WireHash.Int32 => new NumberValue
        {
            Name = name,
            Value = ReadInt32(offset),
            OriginalType = NumericType.Int32,
            BinaryOffset = offset
        },
        WireHash.UInt32 => new NumberValue
        {
            Name = name,
            Value = ReadUInt32(offset),
            OriginalType = NumericType.UInt32,
            BinaryOffset = offset
        },
        WireHash.HashId => new NumberValue
        {
            Name = name,
            Value = ReadUInt32(offset),
            OriginalType = NumericType.HashId,
            Format = NumberFormat.Hex,
            BinaryOffset = offset
        },
        WireHash.ObjId => new NumberValue
        {
            Name = name,
            Value = ReadUInt32(offset),
            OriginalType = NumericType.ObjId,
            Format = NumberFormat.Hex,
            BinaryOffset = offset
        },
        WireHash.UInt16 => new NumberValue
        {
            Name = name,
            Value = ReadUInt16(offset),
            OriginalType = NumericType.UInt16,
            BinaryOffset = offset
        },
        WireHash.UInt8 => new NumberValue
        {
            Name = name,
            Value = ReadUInt8(offset),
            OriginalType = NumericType.UInt8,
            BinaryOffset = offset
        },
        WireHash.Float => new NumberValue
        {
            Name = name,
            Value = ReadFloat(offset),
            OriginalType = NumericType.Float,
            Format = NumberFormat.Float,
            BinaryOffset = offset
        },
        WireHash.Enum => CreateEnumValue(name, enumType, offset),
        WireHash.Int64 => new NumberValue
        {
            Name = name,
            Value = ReadInt64(offset),
            OriginalType = NumericType.Int64,
            BinaryOffset = offset
        },
        WireHash.UInt64 => new NumberValue
        {
            Name = name,
            Value = ReadUInt64(offset),
            OriginalType = NumericType.UInt64,
            BinaryOffset = offset
        },
        _ => throw new InvalidOperationException($"Not a primitive hash: 0x{hash:X8}")
    };

    private EnumValue CreateEnumValue(string name, string? enumType, int offset)
    {
        var rawValue = ReadUInt32(offset);
        string? resolvedName = null;

        if (enumType != null && _globalEnums.TryGetValue(enumType, out var enumDef))
            resolvedName = enumDef.GetName(rawValue);

        return new EnumValue
        {
            Name = name,
            EnumType = enumType ?? "",
            RawValue = rawValue,
            ResolvedName = resolvedName ?? "",
            BinaryOffset = offset
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Binary readers using Span for performance
    // ═══════════════════════════════════════════════════════════════════════

    private byte ReadUInt8(int offset) => _data[offset];
    private ushort ReadUInt16(int offset) => BitConverter.ToUInt16(_data.AsSpan(offset, 2));
    private int ReadInt32(int offset) => BitConverter.ToInt32(_data.AsSpan(offset, 4));
    private uint ReadUInt32(int offset) => BitConverter.ToUInt32(_data.AsSpan(offset, 4));
    private long ReadInt64(int offset) => BitConverter.ToInt64(_data.AsSpan(offset, 8));
    private ulong ReadUInt64(int offset) => BitConverter.ToUInt64(_data.AsSpan(offset, 8));
    private float ReadFloat(int offset) => BitConverter.ToSingle(_data.AsSpan(offset, 4));

    private string ReadInlineString(int offset, int bufferSize)
    {
        int end = offset;
        int maxEnd = Math.Min(offset + bufferSize, _data.Length);
        while (end < maxEnd && _data[end] != 0)
            end++;
        return Encoding.UTF8.GetString(_data.AsSpan(offset, end - offset));
    }
}
