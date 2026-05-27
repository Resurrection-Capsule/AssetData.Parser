# AssetData.Parser — Architecture Redesign (game-faithful)

Reimagines the parser around **how `Darkspore.exe` actually works**, reverse-engineered via Ghidra. Goal: the C# parser should mirror the client's own asset runtime model — one type registry keyed by FNV hash, one generic recursive deserializer dispatching purely on type hash — instead of the current string-keyed + per-field `DataType` enum + fabricated sentinels.

> **Ground truth:** `Darkspore.exe` asset namespaces (337 functions). Cross-validated decompilation lives in the ReCap repo: `docs/architecture/assetdata-system/GHIDRA_GROUND_TRUTH.md` + `FORMAT_COVERAGE.md`. All `0x…` addresses below cite `Darkspore.exe`.
>
> **Status:** blueprint. No code changed yet. Migration is phased and preserves the `AssetNode` output contract (Editor / ReCap server / Wiki consumers untouched).

---

## 1. How the game actually works (recap)

The client ships **one** generic parser (`AssetParser::DeserializeObject` `0x009cd2c0`) backed by **one** registry (`AssetTypeRegistry`). Every format is a `(AssetType::Foo + AssetData::Foo)` stub pair that calls `AssetTypeRegistry::Register` (`0x009f4b40`) — **143 registered formats**. There is no per-format loader.

The parser dispatches each field on a single decision:

```
t = AssetTypeRegistry::FindTypeByHash(field.typeHash)   // 0x009f4370
if t != 0:  recurse DeserializeObject(t)                 // registered struct (incl. "inline struct")
else:       the hash is a SENTINEL or a VALUE-TYPE → handle inline
```

Sentinels (FNV-1a of canonical type names; collide with no struct):

| Hash | Name | Wire shape |
|---|---|---|
| `0x9C617503` | `asset` | indicator → C-string in blob; resolve to live object |
| `0x71AB5182` | `Nullable` | indicator → sub-struct in blob; recurse element type (`field+0x28`) |
| `0x46842E82` | `Key` | indicator → C-string (deferred ref) |
| `0x1D1FF116` | `cLocalizedAssetString` | two indicators → up to two C-strings |
| `0x19E2690D` | `char*` | indicator → C-string |
| `0xF6C8069D` | `char` | inline `char[bufferSize]` if `bufferSize>0`, else dynamic CharPtr |
| `0x555CCDF4` | `array` | `[hasValue:4][count@countOffset:4]` → `count×stride` in blob; element dispatch |
| `0x096339A2` | `Enum` | u32 raw, resolved via enum table on descriptor |

Value-types (raw bytes copied): `bool`, `uint8/16/32`, `int/int32`, `int64/uint64`, `HashId`, `tObjID`, `float`, `cSPVector2/3/4`, `orientation`.

`AssetType::CheckSpecialType` (`FUN_009c8660`) returns true only for the three heap-owning sentinels (`Asset`, `Nullable`, `cLocalizedAssetString`) — used by the array branch + destructor.

**Array element dispatch** (`0x555CCDF4`): `FindTypeByHash(elementHash)` → registered struct ⇒ recurse each; `0x19E2690D` (CharPtr) ⇒ walk strings; otherwise raw bytes already in stream.

---

## 2. Current C# architecture (as-is)

```
AssetCatalog (abstract)            one subclass per format-group; Build() registers via fluent DSL
  └─ Struct(name, size, Field…)    → StructDefinition { Name, Size, Fields[] }  (string-keyed)
AssetParser
  ├─ InitializeCatalogs()          reflection: find AssetCatalog subclasses, instantiate
  ├─ MergeCatalog()                reflection: read private `_structs`/`_enums` dicts, merge
  ├─ _globalStructs : Dict<string> string-keyed type table
  └─ ParseField(field, …)          switch on field.Type (DataType enum baked per field)
AssetNode tree out                 StructNode / ArrayNode / NumberNode / … (consumed by Editor + ReCap)
```

It works and produces correct trees for ~94 formats. But the **model** diverges from the game in ways that cause real bugs and make new formats fragile.

---

## 3. Divergences (what's wrong vs the game)

| # | Current C# | Game (Ghidra) | Severity |
|---|---|---|---|
| D1 | Types resolved by **string name** (`_globalStructs[name]`) | `FindTypeByHash(fnv)` — hash identity | model drift |
| D2 | **`DataType.Struct = 0x00000008`** — a fabricated sentinel for inline structs | No such sentinel. Inline struct = `FindTypeByHash(typeHash)` returns a registered type ⇒ recurse (the *first* check in `DeserializeObject`) | **wrong model** |
| D3 | **`AssetPropertyVector 0xE8A2A5D7`** parsed with an invented layout (`NameHash/TypeHash/ValueOffset/Flags` + 172-byte `VariantData` hexdump) | Sentinel is **dead** — `DeserializeObject` has no case for it. On-disk uses `cAssetPropertyList { mpAssetProperties: array<cAssetProperty,0xBC> }`; `cAssetProperty` = `key u32@0`, `name char[80]@4`, `type u32@0x54`, `value char[80]@0x58` | **wrong — produces garbage** |
| D4 | `DataType` baked per field at authoring time | `field.typeHash` stored in the 0x60 descriptor, resolved at runtime | model drift |
| D5 | `FieldDefinition.ElementType` is a **string**, parsed back via `Enum.TryParse` | `field+0x28` = elementHash (u32) | model drift |
| D6 | `MergeCatalog` reads private `_structs` via **reflection** | `Register()` pushes onto `g_AssetTypeRegistryHead` | hack / fragile |
| D7 | Array branch handles `Asset` + `cLocalizedAssetString` string-arrays | Game array branch only special-cases `CharPtr 0x19E2690D`; others fall to raw | harmless superset (on-disk never emits them) — keep but document |

D2/D3 are the only **correctness** bugs; the rest are model fidelity that make the codebase honest and new-format-proof.

---

## 4. Target architecture (game-faithful)

```
WireHash (constants)        sentinels + value-types = the FNV hashes (today's DataType values)
TypeRegistry                Dict<uint, TypeDescriptor>  keyed by FNV-1a(typeName)   ← AssetTypeRegistry
  ├─ Register(descriptor)   adds by hash                                            ← Register 0x009f4b40
  └─ FindTypeByHash(uint)   → TypeDescriptor?  (null ⇒ sentinel/value-type)         ← FindTypeByHash 0x009f4370
TypeDescriptor              { Name, TypeHash, Size, Fields[], FlattenedSize?, Fingerprint? }   ← 0xA78 record
FieldDescriptor             { Name, NameHash, TypeHash, Offset, ElementHash, CountOffset, BufferSize, EnumTable }  ← 0x60 desc
Deserializer.Deserialize    ONE recursive method dispatching on FindTypeByHash      ← DeserializeObject 0x009cd2c0
AssetNode tree out          UNCHANGED — same node classes, same shape
```

### 4.1 The single dispatch (mirrors `DeserializeObject`)

```csharp
void Deserialize(AssetNode parent, TypeDescriptor type, int baseOffset)
{
    foreach (var f in type.Fields)
    {
        int off = baseOffset + f.Offset;
        var sub = _registry.FindTypeByHash(f.TypeHash);
        if (sub is not null) {                       // registered struct (replaces DataType.Struct)
            var node = new StructNode { Name = f.Name, TypeName = sub.Name, BinaryOffset = off };
            Deserialize(node, sub, off);
            parent.AddChild(node);
            continue;
        }
        parent.AddChild(f.TypeHash switch {          // sentinel / value-type
            WireHash.Asset                 => ReadAssetRef(f, off),
            WireHash.Nullable              => ReadNullable(f, off),     // recurse FindTypeByHash(f.ElementHash)
            WireHash.Key                   => ReadKey(f, off),
            WireHash.LocalizedAssetString  => ReadLocalized(f, off),
            WireHash.CharPtr               => ReadCharPtr(f, off),
            WireHash.Char                  => ReadChar(f, off),         // inline if BufferSize>0
            WireHash.Array                 => ReadArray(f, off),        // element dispatch via FindTypeByHash
            WireHash.Enum                  => ReadEnum(f, off),
            _                              => ReadValueType(f.TypeHash, f, off)  // bool/int/float/vector…
        });
    }
}
```

Note: **no `DataType.Struct`, no `AssetPropertyVector` branch.** Inline struct and "property vector" both resolve through `FindTypeByHash` like everything else.

### 4.2 Authoring stays a fluent DSL (1 stub ≈ 1 game format)

The per-format `Build()` DSL is *good* — it's the C# equivalent of an `AssetData::Foo` stub. Keep it; only change what it produces (a `TypeDescriptor` registered by hash) and how the registry collects it (explicit, not reflection):

```csharp
public sealed class NounType : AssetTypeStub        // ← was NounCatalog : AssetCatalog
{
    protected override void Build(TypeRegistry r) => r.Register("Noun", size: 480,
        EnumField("nounType", "NounType", 0),
        Key("prefab", 16),
        Struct("bbox", "cSPBoundingBox", 56),        // inline → resolved by hash, no DataType.Struct
        Nullable("gfxStates", "cGameObjectGfxStates", 132),
        Array("eliteAssetIds", WireHash.UInt64, 176),
        Asset("npcClassData", 160),
        // …
        Array("properties", "cAssetProperty", 460)); // property list = array<cAssetProperty>, NOT AssetPropertyVector
}
```

`Struct(...)`/`Nullable(...)`/`Array(...)` helpers compute the element/type hash at registration (`FNV1a(typeName)`), so the field carries a hash exactly like the game descriptor.

### 4.3 `cAssetProperty` (replaces the fake `AssetPropertyVector`)

```csharp
r.Register("cAssetProperty", size: 0xBC,
    Field("key",   WireHash.UInt32, 0x00),
    CharBuffer("name",  0x04, bufferSize: 80),   // Char sentinel, inline
    Field("type",  WireHash.UInt32, 0x54),       // variant discriminator
    CharBuffer("value", 0x58, bufferSize: 80));  // interpreted per `type`
// property-bearing fields use: Array("props", "cAssetProperty", offset)
```

Delete `DataType.AssetPropertyVector`, `ParseAssetPropertyVectorField`, `ParseAssetPropertyItem`, and `DataType.Struct`.

---

## 5. Delete / change / keep

**Delete**
- `DataType.Struct = 0x00000008` (D2) and `ParseInlineStructField` special path → handled by hash recursion.
- `DataType.AssetPropertyVector` + `ParseAssetPropertyVectorField` + `ParseAssetPropertyItem` (D3).
- `MergeCatalog` reflection over private `_structs`/`_enums` (D6).
- Legacy `DataType.UInt = 0x54CC76D5` alias if unused.

**Change**
- `_globalStructs : Dict<string,…>` → `TypeRegistry : Dict<uint, TypeDescriptor>` keyed by `FNV1a(name)`. Keep a thin `name→hash` map only for the `GetFileType(extension)` entry point.
- `FieldDefinition.ElementType : string` → `ElementHash : uint` (+ keep name for debug/XML).
- `ParseField` switch on `DataType` → `Deserialize` dispatch on `FindTypeByHash` then sentinel hash.
- `AssetCatalog` → `AssetTypeStub` whose `Build(TypeRegistry)` registers explicitly.

**Keep (unchanged contract)**
- All `AssetNode` classes + tree shape. **Editor, ReCap server, Wiki keep working with zero changes.**
- `DbpfReader` (DBPF/DBBF + RefPack) — already faithful.
- `Catalog.cs` / `CatalogEntry.cs` — catalog file schema confirmed correct.
- The 143 fluent format definitions — only the base class + helper return types change; field rows stay.
- `AssetService` / `AssetSerializer` public API.

**Optional fidelity (nice-to-have, not required)**
- `FlattenedSize` per type (mirror `IndexType` `0x009f4…`) — sum of nested instance sizes.
- `Fingerprint` per type (mirror `BuildTypeMetadata` `0x009f43e0`) — recursive FNV structural hash → free disk-cache invalidation key.

---

## 6. Migration plan (phased, each phase compiles + green)

| Phase | Goal | Touchpoints | Risk |
|---|---|---|---|
| 0 | Add `WireHash` constants + `TypeRegistry` + `TypeDescriptor` + `FieldDescriptor`. Build an **adapter** that fills the registry from existing `StructDefinition`s (no DSL change yet). | new `Core/TypeModel/*.cs` | none (additive) |
| 1 | Rewrite the deserializer to dispatch via `FindTypeByHash` + sentinel hash. Keep DSL + `DataType` as the hash source. Run on real `.package`, diff `AssetNode` trees against current output. | `AssetParser.cs` | medium — covered by tree-diff test |
| 2 | **Correctness fix:** register `cAssetProperty`/`cAssetPropertyList` as structs; convert property fields to `Array("…","cAssetProperty")`; delete `AssetPropertyVector` + `DataType.Struct`; inline structs become hash recursion. | `TypeSystem.cs`, format defs using them | medium |
| 3 | Replace reflection `MergeCatalog` with explicit `stub.Build(registry)`; rename `AssetCatalog`→`AssetTypeStub`. | `AssetParser.cs`, all 143 defs (mechanical: base class + helper signatures) | low (mechanical, compiler-checked) |
| 4 | Optional: `FlattenedSize` + `Fingerprint`; persist a MemoryPack cache keyed by fingerprint. | `Core/TypeModel/*.cs` | none |

Phases 0–2 deliver the correctness wins. Phase 3 is cosmetic-but-honest. Phase 4 is performance.

### Regression guard
Before Phase 1, add a golden test: parse every entry in a real `AssetData_Binary.package`, serialize to XML, snapshot. Each phase must keep the snapshot byte-identical (except the intended `cAssetProperty` fix in Phase 2, which gets its own snapshot update).

---

## 7. Compatibility & non-goals

- **Output contract frozen.** `AssetNode` shape is the public boundary; no consumer changes.
- **Not modeling client runtime identity.** `AssetObject`/`AssetCache`/the 5-region `AssetCatalog`/status machine are *runtime* concerns (live-object caching, ref counts). A parser library returns trees; it does not need them. (The ReCap server adds its own caching layer — see `ASSET_SYSTEM.md`.)
- **Not a loader-per-format.** Stays one generic deserializer, exactly like the client.

---

## 8. Open questions

1. **Does `cAssetProperty.value` (char[80]@0x58) ever overflow to a blob?** Game `Char` sentinel falls back to dynamic CharPtr when `bufferSize==0`; here bufferSize=80, so inline. Confirm no asset exceeds 80 bytes in that field (decompile a real `.noun` carrying properties).
2. **`name` field offset for `cAssetProperty`** — verify `0x04` start vs alignment padding against a real on-disk record (the 0xA8..0xBC tail is ~20 unexplained bytes).
3. **Which formats emit `array<cAssetProperty>`?** Confirmed — exactly two defs currently declare `AssetPropertyVector`: `PacketTypes/ability.cs` and `Structures/cAICondition.cs`. These are the Phase-2 fix targets. (`IStruct`/inline-struct is used in 11 defs — all become hash recursion in Phase 2, no row changes needed.)
4. **Keep the `DataType` enum name** (as `WireHash` value source) or split sentinels vs value-types into two enums for clarity?
5. `Structures/DirectorBucket..cs` has a double-dot filename — typo to fix during Phase 3 sweep.

---

## 9. Appendix — module audit (per-file, Ghidra-grounded)

Read of the actual modules, with concrete fixes mapped to the client's behavior.

### `Core/AssetNode.cs` — **biggest architectural smell**
The parser's output type is an **editor view-model in disguise**: `INotifyPropertyChanged`, `ObservableCollection<AssetNode> Children`, `IsEditable`, `DisplayValue` (a UI string), `BinaryOffset` (debug) — on **every node**. The client's parsed object is a plain struct with field values/pointers; it has no observability, no display strings, no edit flags. For a ~150 MB package this is hundreds of MB of pointless MVVM bookkeeping (matches the ReCap `ASSET_SYSTEM.md` problem #9).

This is the **layering violation** the debate identified, now with evidence: **L1 (parser) currently ships the L2-Editor model.**

- **Fix (the big one):** L1 emits a lean immutable tree (POCO + `IReadOnlyList`, no INPC, no `DisplayValue`/`IsEditable`). L2-Editor maps it to an observable `EditorNode` (INPC + undo + display). L2-Server reads the lean tree / projects to immutable DTOs.
- **Effort:** high — the Editor consumes `AssetNode` directly today (confirmed: `PackageBrowserViewModel`, `XmlToNodes`, `MainViewModel`). Needs an editor-node adapter. Schedule as its own phase **after** the registry refactor; gate behind the golden-tree test.
- **Why it matters for fidelity:** the client cleanly parses then *optionally* wraps for the editor (`Editor mode? → Preload *.Library.Xml`). Same split.

### `Core/DbpfReader.cs` — faithful, but O(n) lookups
- DBPF/DBBF header, shared type/group, RefPack decompress: **faithful**, keep.
- **O(n) linear scans** — `GetAsset(ResourceKey)` (line ~183), `Resolve()` (~240/248), `LoadInternalNames` (~138) all `foreach (_entries)`. The client uses **hash buckets** (`AssetCache::FindCachedAsset 0x009cac50`, `g_GlobalManager+0x28`). **Fix:** build `Dictionary<ResourceKey, DbpfEntry>` + `Dictionary<uint /*instanceId*/, DbpfEntry>` once in the ctor (matches ReCap `ASSET_SYSTEM.md` quick-win #1). Same fix the server needs.
- **Name resolution** — three `NameRegistry` (type/file/project from `reg_*.txt` + embedded `sporemaster/names`) is the C# stand-in for the client's `catalog_*.bin` (`assetNameWType` / `sourceFileNameWType`). **Optional fidelity:** load `catalog_*.bin` via the existing `Catalog`/`CatalogEntry` parser to seed names directly from the package, as `ProcessCatalogItem 0x009cd6e0` does — fewer external `.txt` dependencies.
- **No decompressed-entry cache** — `ReadEntry` re-reads + re-decompresses each call. The client caches live objects (`AssetCache`). This is an **L2 concern** (editor cache / server warm-up), not L1. Leave L1 stateless; document it.

### `Core/TypeSystem.cs` + `Core/AssetParser.cs`
Covered in §3–§5: hash registry, kill `DataType.Struct` (D2) + `AssetPropertyVector` (D3), drop reflection `MergeCatalog` (D6), `ElementType:string` → `ElementHash:uint` (D5).

### `Core/AssetService.cs` (`AssetSerializer`)
`AssetSerializer.ToXml` is an **export/editor** concern, not core parsing — the client has no XML emitter on the binary path (XML is editor-mode only). **Fix:** move XML serialization to a `Serialization`/Editor module so L1 Core is parse-only. Low priority.

### `Editor/Services/SchemaProvider.cs`
Already `[Obsolete]` + "can be safely deleted". **Delete in Phase 3.**

### `Editor` (consumers)
- `PackageBrowserViewModel` + `KeyAssetSuggestionsService` need a package-wide **name/key/type index** but currently lean on `DbpfReader.ListAssets` (O(n)). This is the **L2-Editor index** from the debate (the client's `AssetCatalog` maps #2 key / #4 name / DBList type, in MVVM idiom). Build it once the DbpfReader exposes the keyed dictionaries above.
- `UndoRedoService` is already the editor's identity/mutation layer — the home for the observable node model after L1 is leaned out.

### Format defs (143 stubs)
Mechanical changes only (base class + helper return types) in Phase 3. Two correctness targets in Phase 2: `PacketTypes/ability.cs`, `Structures/cAICondition.cs` (drop `AssetPropertyVector` → `array<cAssetProperty>`). Fix `DirectorBucket..cs` filename.

### Audit summary — priority

| Fix | Module | Severity | Phase |
|---|---|---|---|
| `cAssetProperty` (drop fake `AssetPropertyVector`) | TypeSystem + 2 defs | **correctness** | 2 |
| Drop fabricated `DataType.Struct` | TypeSystem/Parser | **correctness** | 2 |
| Hash-keyed `TypeRegistry` + unified dispatch | Parser/TypeSystem | model fidelity | 1 |
| DbpfReader O(n) → keyed dictionaries | DbpfReader | perf | 1 (or quick-win now) |
| Lean L1 node model (move MVVM to Editor) | AssetNode + Editor | architecture | post-registry phase |
| Drop reflection `MergeCatalog` | Parser | cleanup | 3 |
| Delete dead `SchemaProvider` | Editor | cleanup | 3 |
| Move XML out of Core | AssetService | cleanup | 3 |
| `catalog_*.bin` name seeding | DbpfReader | optional fidelity | 4 |
