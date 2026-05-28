using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace AssetData.Parser;

/// <summary>
/// Resource key identifying an asset in a DBPF archive.
/// </summary>
public readonly record struct ResourceKey(uint TypeId, uint GroupId, uint InstanceId)
{
    public override string ToString() => $"0x{TypeId:X8}!0x{GroupId:X8}!0x{InstanceId:X8}";
}

/// <summary>
/// DBPF/DBBF archive reader (Spore/Darkspore package format).
/// </summary>
public sealed class DbpfReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private readonly bool _isDBBF;
    private readonly List<DbpfEntry> _entries = new();
    // Hash-bucket indices (mirror the client's AssetCache lookup; replace O(n) scans).
    private readonly Dictionary<ResourceKey, DbpfEntry> _byKey = new();
    private readonly Dictionary<uint, List<DbpfEntry>> _byInstance = new();
    private readonly NameRegistry _typeRegistry = new();
    private readonly NameRegistry _fileRegistry = new();
    private readonly NameRegistry _projectRegistry = new();
    
    public IReadOnlyList<DbpfEntry> Entries => _entries;
    
    /// <summary>Optional logger. Set before opening a package for boot-time diagnostics.</summary>
    public Action<string>? Logger { get; set; }

    public DbpfReader(string path)
    {
        _stream = File.OpenRead(path);
        _reader = new BinaryReader(_stream);

        LoadEmbeddedRegistries();
        
        // Read header
        uint magic = _reader.ReadUInt32();
        _isDBBF = magic == 0x46424244; // "DBBF"
        bool isDBPF = magic == 0x46504244; // "DBPF"
        
        if (!isDBPF && !_isDBBF)
            throw new InvalidDataException($"Invalid DBPF magic: 0x{magic:X8}");
        
        int majorVersion = _reader.ReadInt32();
        int minorVersion = _reader.ReadInt32();
        
        // Read index location
        int count;
        long indexOffset;
        
        if (_isDBBF)
        {
            _stream.Seek(0x24, SeekOrigin.Begin);
            count = _reader.ReadInt32();
            _stream.Seek(0x30, SeekOrigin.Begin);
            indexOffset = _reader.ReadInt64();
        }
        else
        {
            _stream.Seek(0x24, SeekOrigin.Begin);
            count = _reader.ReadInt32();
            _stream.Seek(0x40, SeekOrigin.Begin);
            indexOffset = _reader.ReadUInt32();
        }
        
        // Read index entries
        _stream.Seek(indexOffset, SeekOrigin.Begin);
        int flags = _reader.ReadInt32();
        
        int? sharedTypeId = (flags & 1) != 0 ? _reader.ReadInt32() : null;
        int? sharedGroupId = (flags & 2) != 0 ? _reader.ReadInt32() : null;
        if ((flags & 4) != 0) _reader.ReadInt32(); // unknown field
        
        for (int i = 0; i < count; i++)
        {
            var entry = ReadEntry(sharedTypeId, sharedGroupId);
            _entries.Add(entry);

            // Build lookup indices in entry order (first-wins on duplicate keys, matching
            // the previous linear-scan behavior which returned the first match).
            _byKey.TryAdd(entry.Key, entry);
            if (!_byInstance.TryGetValue(entry.Key.InstanceId, out var bucket))
                _byInstance[entry.Key.InstanceId] = bucket = new List<DbpfEntry>();
            bucket.Add(entry);
        }

        // Try to load internal names
        LoadInternalNames();
    }
    
    private DbpfEntry ReadEntry(int? sharedTypeId, int? sharedGroupId)
    {
        uint typeId = sharedTypeId.HasValue ? (uint)sharedTypeId.Value : _reader.ReadUInt32();
        uint groupId = sharedGroupId.HasValue ? (uint)sharedGroupId.Value : _reader.ReadUInt32();
        uint instanceId = _reader.ReadUInt32();
        
        ulong offset = _isDBBF ? _reader.ReadUInt64() : _reader.ReadUInt32();
        int compressedSize = _reader.ReadInt32() & 0x7FFFFFFF;
        int memSize = _reader.ReadInt32();
        short compressionFlag = _reader.ReadInt16();
        bool isCompressed = compressionFlag == -1 || compressionFlag != 0;
        _reader.ReadByte(); // unknown
        _reader.ReadByte(); // padding
        
        return new DbpfEntry(
            new ResourceKey(typeId, groupId, instanceId),
            offset, compressedSize, memSize, isCompressed
        );
    }
    
    private void LoadEmbeddedRegistries()
    {
        var asm = Assembly.GetExecutingAssembly();
        var map = new (string Resource, NameRegistry Registry)[]
        {
            ("AssetData.Parser.Registries.reg_type.txt", _typeRegistry),
            ("AssetData.Parser.Registries.reg_file.txt", _fileRegistry),
        };

        foreach (var (resName, registry) in map)
        {
            using var stream = asm.GetManifestResourceStream(resName);
            if (stream == null)
            {
                Logger?.Invoke($"[DbpfReader] WARN: embedded registry '{resName}' nao encontrado.");
                continue;
            }
            using var reader = new StreamReader(stream, Encoding.UTF8);
            registry.LoadFromString(reader.ReadToEnd());
            Logger?.Invoke($"[DbpfReader] Registry '{resName}' carregado.");
        }
    }

    private void LoadInternalNames()
    {
        uint sporemasterGroup = FnvHash("sporemaster");
        uint namesInstance = FnvHash("names");
        
        foreach (var entry in _entries)
        {
            if (entry.Key.GroupId == sporemasterGroup && entry.Key.InstanceId == namesInstance)
            {
                var data = ReadEntry(entry);
                if (data != null)
                {
                    var text = Encoding.UTF8.GetString(data);
                    _projectRegistry.LoadFromString(text);
                }
                break;
            }
        }
    }
    
    /// <summary>
    /// Load external registry files for name resolution.
    /// </summary>
    public void LoadRegistries(string registryDir)
    {
        var typeRegPath = Path.Combine(registryDir, "reg_type.txt");
        var fileRegPath = Path.Combine(registryDir, "reg_file.txt");
        
        if (File.Exists(typeRegPath))
            _typeRegistry.LoadFromFile(typeRegPath);
        if (File.Exists(fileRegPath))
            _fileRegistry.LoadFromFile(fileRegPath);
    }
    
    /// <summary>
    /// Get asset by virtual name (e.g., "ZelemBoss.phase").
    /// </summary>
    public byte[]? GetAsset(string virtualName)
    {
        var key = Resolve(virtualName);
        if (key.InstanceId == 0 && key.TypeId == 0)
            return null;
        return GetAsset(key);
    }
    
    /// <summary>
    /// Get asset by resource key.
    /// </summary>
    public byte[]? GetAsset(ResourceKey key)
        => _byKey.TryGetValue(key, out var entry) ? ReadEntry(entry) : null;
    
    /// <summary>
    /// Read and decompress entry data.
    /// </summary>
    public byte[]? ReadEntry(DbpfEntry entry)
    {
        try
        {
            _stream.Seek((long)entry.Offset, SeekOrigin.Begin);
            var compressed = _reader.ReadBytes(entry.CompressedSize);
            
            if (entry.IsCompressed)
            {
                var decompressed = new byte[entry.MemSize];
                RefPackDecompress(compressed, decompressed);
                return decompressed;
            }
            
            return compressed;
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Resolve virtual name to resource key.
    /// </summary>
    public ResourceKey Resolve(string virtualName)
    {
        var parts = virtualName.Split('.', 2);
        string fname = parts[0];
        string? ftype = parts.Length > 1 ? parts[1] : null;
        
        // Resolve file name hash
        uint fHash = _fileRegistry.GetHash(fname) 
                  ?? _projectRegistry.GetHash(fname) 
                  ?? ParseOrHash(fname);
        
        // Resolve type hash
        uint? tHash = null;
        if (ftype != null)
        {
            tHash = _typeRegistry.GetHash(ftype) ?? ParseOrHash(ftype);
        }
        
        if (!_byInstance.TryGetValue(fHash, out var bucket))
            return default;

        // Prefer an exact type match (entry order preserved), else fall back to the
        // first entry for this instance — same result as the old two-pass linear scan.
        if (tHash.HasValue)
        {
            foreach (var entry in bucket)
                if (entry.Key.TypeId == tHash.Value)
                    return entry.Key;
        }

        return bucket[0].Key;
    }
    
    /// <summary>
    /// List all assets with resolved names.
    /// </summary>
    public IEnumerable<string> ListAssets()
    {
        foreach (var entry in _entries)
        {
            var fname = _fileRegistry.GetName(entry.Key.InstanceId)
                     ?? _projectRegistry.GetName(entry.Key.InstanceId)
                     ?? $"0x{entry.Key.InstanceId:X8}";
            
            var ftype = _typeRegistry.GetName(entry.Key.TypeId)
                     ?? $"0x{entry.Key.TypeId:X8}";
            
            yield return $"{fname}.{ftype}";
        }
    }
    
    /// <summary>
    /// List assets filtered by type extension.
    /// </summary>
    public IEnumerable<(string Name, DbpfEntry Entry)> ListAssetsByType(string typeExtension)
    {
        uint typeHash = _typeRegistry.GetHash(typeExtension) ?? FnvHash(typeExtension);
        
        foreach (var entry in _entries)
        {
            if (entry.Key.TypeId == typeHash)
            {
                var fname = _fileRegistry.GetName(entry.Key.InstanceId)
                         ?? _projectRegistry.GetName(entry.Key.InstanceId)
                         ?? $"0x{entry.Key.InstanceId:X8}";
                
                yield return ($"{fname}.{typeExtension}", entry);
            }
        }
    }
    
    public void Dispose()
    {
        _reader.Dispose();
        _stream.Dispose();
    }
    
    // FNV hash (case-insensitive). Delegates to the single shared implementation; kept as a
    // public entry point for NameRegistry and external callers.
    public static uint FnvHash(string s) => WireHash.Fnv1a(s);
    
    private static uint ParseOrHash(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToUInt32(s, 16);
        if (s.StartsWith("#"))
            return Convert.ToUInt32(s[1..], 16);
        if (s.StartsWith("$"))
            return FnvHash(s[1..]);
        if (uint.TryParse(s, out uint val))
            return val;
        return FnvHash(s);
    }
    
    // RefPack decompression
    private static void RefPackDecompress(byte[] input, byte[] output)
    {
        if (input.Length < 5) return;
        
        int pin = 0;
        byte cType = input[pin++];
        pin++; // skip
        
        if (cType != 0x10 && cType != 0x50) return;
        
        int decompSize = (input[pin] << 16) | (input[pin + 1] << 8) | input[pin + 2];
        pin += 3;
        
        if (output.Length < decompSize) return;
        
        int size = 0;
        while (size < decompSize && pin < input.Length)
        {
            int ctrl = input[pin++];
            int numPlain = 0, numCopy = 0, copyOff = 0;
            
            if (ctrl >= 252)
            {
                numPlain = ctrl & 0x03;
            }
            else if (ctrl >= 224)
            {
                numPlain = ((ctrl & 0x1F) << 2) + 4;
            }
            else if (ctrl >= 192)
            {
                if (pin + 3 > input.Length) break;
                byte b1 = input[pin++], b2 = input[pin++], b3 = input[pin++];
                numPlain = ctrl & 0x03;
                numCopy = ((ctrl & 0x0C) << 6) + b3 + 5;
                copyOff = ((ctrl & 0x10) << 12) + (b1 << 8) + b2 + 1;
            }
            else if (ctrl >= 128)
            {
                if (pin + 2 > input.Length) break;
                byte b2 = input[pin++], b3 = input[pin++];
                numPlain = (b2 >> 6) & 0x03;
                numCopy = (ctrl & 0x3F) + 4;
                copyOff = ((b2 & 0x3F) << 8) + b3 + 1;
            }
            else
            {
                if (pin + 1 > input.Length) break;
                byte b1 = input[pin++];
                numPlain = ctrl & 0x03;
                numCopy = ((ctrl >> 2) & 0x07) + 3;
                copyOff = ((ctrl & 0x60) << 3) + b1 + 1;
            }
            
            // Copy plain bytes
            if (numPlain > 0)
            {
                if (pin + numPlain > input.Length || size + numPlain > output.Length) break;
                Array.Copy(input, pin, output, size, numPlain);
                pin += numPlain;
                size += numPlain;
            }
            
            // Copy from back-reference
            if (numCopy > 0)
            {
                if (copyOff <= 0 || copyOff > size || size + numCopy > output.Length) break;
                
                for (int i = 0; i < numCopy; i++)
                {
                    output[size] = output[size - copyOff];
                    size++;
                }
            }
        }
    }
}

/// <summary>
/// DBPF entry metadata.
/// </summary>
public readonly record struct DbpfEntry(
    ResourceKey Key,
    ulong Offset,
    int CompressedSize,
    int MemSize,
    bool IsCompressed
);

/// <summary>
/// Name-to-hash registry for resolving virtual names.
/// </summary>
internal sealed class NameRegistry
{
    private readonly Dictionary<string, uint> _hashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, string> _names = new();
    
    public void Add(string name, uint hash)
    {
        _hashes[name] = hash;
        _names[hash] = name;
    }
    
    public uint? GetHash(string name) => _hashes.TryGetValue(name, out var h) ? h : null;
    public string? GetName(uint hash) => _names.TryGetValue(hash, out var n) ? n : null;
    
    public void LoadFromFile(string path)
    {
        if (!File.Exists(path)) return;
        LoadFromString(File.ReadAllText(path));
    }
    
    public void LoadFromString(string content)
    {
        foreach (var line in content.Split('\n', '\r'))
        {
            var trimmed = line.Trim();

            var commentIdx = trimmed.IndexOf("//");
            if (commentIdx >= 0) trimmed = trimmed[..commentIdx].Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed[0] == '#') continue;

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            string name;
            uint hash;

            if (parts.Length >= 2)
            {
                name = parts[0];
                hash = ParseHash(parts[1]);
            }
            else
            {
                var raw = parts[0];
                var stripped = (raw.StartsWith("$") || raw.StartsWith("%")) ? raw[1..] : raw;
                name = stripped;
                hash = DbpfReader.FnvHash(stripped);
            }

            Add(name, hash);
        }
    }

    private static uint ParseHash(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToUInt32(s, 16);
        if (s.StartsWith("#"))
            return Convert.ToUInt32(s[1..], 16);
        if (s.StartsWith("$"))
            return DbpfReader.FnvHash(s[1..]);
        if (uint.TryParse(s, out uint val))
            return val;
        return 0;
    }
}
