using System.Text;
using AssetData.Parser;

namespace ReCap.Wiki;

/// <summary>
/// Wiki generator for AssetData.Parser asset catalog documentation.
/// </summary>
public static class Program
{
    private static readonly Dictionary<string, HashSet<string>> _usagesMap = new();
    private const string FolderStructures = "Structures";
    private const string FolderEnums = "Enums";
    private const string FolderCatalog = "Catalog";

    public static int Main(string[] args)
    {
        string outputDir = args.Length > 0 ? args[0] : "./wiki-output";
        
        try
        {
            GenerateWiki(outputDir);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static void GenerateWiki(string outputDir)
    {
        Console.WriteLine($"[WikiGen] Generating Wiki in: {outputDir}");

        var catalogDir = Path.Combine(outputDir, FolderCatalog);
        var structsDir = Path.Combine(catalogDir, FolderStructures);
        var enumsDir = Path.Combine(catalogDir, FolderEnums);

        if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        Directory.CreateDirectory(structsDir);
        Directory.CreateDirectory(enumsDir);

        var assembly = typeof(AssetCatalog).Assembly;
        var catalogTypes = assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(AssetCatalog)) && !t.IsAbstract)
            .OrderBy(t => t.Name);

        var loadedCatalogs = new List<AssetCatalog>();
        foreach (var type in catalogTypes)
        {
            try 
            { 
                loadedCatalogs.Add((AssetCatalog)Activator.CreateInstance(type)!); 
            }
            catch 
            { 
                Console.WriteLine($"[WikiGen] Warning: Failed to instantiate {type.Name}"); 
            }
        }

        ResolveEnumReferences(loadedCatalogs);
        BuildDependencyGraph(loadedCatalogs);

        var allStructs = new List<string>();
        var allEnums = new List<string>();

        foreach (var catalog in loadedCatalogs)
        {
            foreach (var structName in catalog.StructNames)
            {
                var def = catalog.GetStruct(structName);
                if (def == null) continue;
                var markdown = GenerateMarkdownForStruct(def);
                File.WriteAllText(Path.Combine(structsDir, $"{def.Name}.md"), markdown);
                allStructs.Add(def.Name);
            }

            foreach (var enumName in catalog.EnumNames)
            {
                var def = catalog.GetEnum(enumName);
                if (def == null) continue;
                var markdown = GenerateMarkdownForEnum(def);
                File.WriteAllText(Path.Combine(enumsDir, $"{def.Name}.md"), markdown);
                allEnums.Add(def.Name);
            }
        }

        GenerateSidebar(outputDir, allStructs, allEnums);
        Console.WriteLine($"[WikiGen] Done! {allStructs.Count} Structures, {allEnums.Count} Enums.");
    }

    private static void ResolveEnumReferences(List<AssetCatalog> catalogs)
    {
        var globalEnumNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in catalogs)
        {
            foreach (var e in cat.EnumNames) globalEnumNames.Add(e);
        }

        Console.WriteLine($"[WikiGen] Resolving references for {globalEnumNames.Count} Enums...");

        foreach (var cat in catalogs)
        {
            foreach (var structName in cat.StructNames)
            {
                var def = cat.GetStruct(structName);
                if (def == null) continue;

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
                    if (globalEnumNames.Contains(specificName))
                    {
                        resolvedEnum = specificName;
                    }
                    else
                    {
                        if (globalEnumNames.Contains(field.Name))
                        {
                            resolvedEnum = field.Name;
                        }
                        else
                        {
                            var pascalName = ToPascalCase(field.Name);
                            if (globalEnumNames.Contains(pascalName))
                            {
                                resolvedEnum = pascalName;
                            }
                        }
                    }

                    if (resolvedEnum != null)
                    {
                        fieldsList[i] = field with { EnumType = resolvedEnum };
                        Console.WriteLine($"   Linked: {def.Name}.{field.Name} -> {resolvedEnum}");
                    }
                }
            }
        }
    }

    private static string ToPascalCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (char.IsUpper(s[0])) return s;
        return char.ToUpper(s[0]) + s.Substring(1);
    }

    private static void GenerateSidebar(string outputDir, List<string> structs, List<string> enums)
    {
        var sb = new StringBuilder();
        
        // Header with icon
        sb.AppendLine("<h3>");
        sb.AppendLine("  <img");
        sb.AppendLine("    src=\"https://raw.githubusercontent.com/JeanxPereira/AssetData.Parser/refs/heads/main/.github/icon.png\"");
        sb.AppendLine("    width=\"32\"");
        sb.AppendLine("    align=\"center\"");
        sb.AppendLine("  />");
        sb.AppendLine("  Darkspore AssetData");
        sb.AppendLine("</h3>");
        sb.AppendLine();
        
        sb.AppendLine("## Base");
        sb.AppendLine("* **[[Home]]**");
        sb.AppendLine("* [[Getting Started]]");
        sb.AppendLine();

        sb.AppendLine("## Guides");
        sb.AppendLine("* [[Asset System]]");
        sb.AppendLine("* [[Binary Format]]");
        sb.AppendLine("* [[Parser Architecture]]");
        sb.AppendLine("* [[Adding a Format]]");
        sb.AppendLine("* [[Catalog Reference]]");
        sb.AppendLine();

        sb.AppendLine("## Catalog Assets");
        
        // Structures with details/summary
        sb.AppendLine("  <details>");
        sb.AppendLine("  <summary>Structures</summary>");
        sb.AppendLine();
        foreach (var s in structs.OrderBy(x => x))
        {
            sb.AppendLine($"  * [[{s}]]");
        }
        sb.AppendLine("  </details>");
        
        // Enums with details/summary
        sb.AppendLine("  <details>");
        sb.AppendLine("  <summary>Enums</summary>");
        sb.AppendLine();
        foreach (var e in enums.OrderBy(x => x))
        {
            sb.AppendLine($"  * [[{e}]]");
        }
        sb.AppendLine("  </details>");
        sb.AppendLine();

        File.WriteAllText(Path.Combine(outputDir, "_Sidebar.md"), sb.ToString());
    }

    private static void BuildDependencyGraph(List<AssetCatalog> catalogs)
    {
        _usagesMap.Clear();
        foreach (var catalog in catalogs)
        {
            foreach (var structName in catalog.StructNames)
            {
                var definition = catalog.GetStruct(structName);
                if (definition == null) continue;

                foreach (var field in definition.Fields)
                {
                    string? dep = null;
                    if (field.Type == DataType.Struct) dep = field.ElementType;
                    else if (field.Type == DataType.Nullable) dep = field.ElementType;
                    else if (field.Type == DataType.Enum) dep = field.EnumType;
                    else if (field.Type == DataType.Array)
                    {
                        if (field.ElementType == "Enum") dep = field.EnumType;
                        else if (field.ElementType != null && !Enum.TryParse<DataType>(field.ElementType, true, out _))
                            dep = field.ElementType;
                    }

                    if (dep != null)
                    {
                        if (!_usagesMap.ContainsKey(dep)) _usagesMap[dep] = new HashSet<string>();
                        _usagesMap[dep].Add(structName);
                    }
                }
            }
        }
    }

    private static string GenerateMarkdownForStruct(StructDefinition def)
    {
        var sb = new StringBuilder();
        // sb.AppendLine($"# {def.Name}");
        sb.AppendLine($"**Size:** `0x{def.Size:X}`");
        sb.AppendLine($"**Count:** `0x{def.Fields.Count:X}`");
        sb.AppendLine();

        sb.AppendLine("## Structure");
        sb.AppendLine("| Offset | DataType | Name |");
        sb.AppendLine("| :-: | :- | :- |");

        foreach (var field in def.Fields)
        {
            sb.AppendLine($"| `0x{field.Offset:X2}` | {FormatDataType(field)} | **{field.Name}** |");
        }
        sb.AppendLine();

        AppendReferencesSection(sb, def.Name);
        return sb.ToString();
    }

    private static string GenerateMarkdownForEnum(EnumDefinition def)
    {
        var sb = new StringBuilder();
        // sb.AppendLine($"# {def.Name}");
        // sb.AppendLine();
        sb.AppendLine("## Values");
        sb.AppendLine("| Value | Name |");
        sb.AppendLine("| :-: | :- |");

        foreach (var kvp in def.Values.OrderBy(x => x.Key))
        {
            sb.AppendLine($"| `0x{kvp.Key:X8}` | **{kvp.Value}** |");
        }
        sb.AppendLine();

        AppendReferencesSection(sb, def.Name);
        return sb.ToString();
    }

    private static void AppendReferencesSection(StringBuilder sb, string typeName)
    {
        if (_usagesMap.TryGetValue(typeName, out var users) && users.Count > 0)
        {
            sb.AppendLine("> ### Reference");
            sb.AppendLine("> Used by:");
            foreach (var user in users.OrderBy(u => u))
            {
                sb.AppendLine($"> [`{user}`]({user})");
            }
            sb.AppendLine();
        }
    }

    private static string FormatDataType(FieldDefinition field)
    {
        if (field.Type == DataType.Struct)
        {
            var name = field.ElementType ?? "Unknown";
            return $"[`({name})`]({name})";
        }
        
        if (field.Type == DataType.Nullable)
        {
            var name = field.ElementType ?? "Unknown";
            return $"`Nullable` [`({name})`]({name})";
        }

        if (field.Type == DataType.Enum)
        {
            var name = field.EnumType ?? "Unknown";
            return name != "Unknown" ? $"`Enum` [`({name})`]({name})" : $"`Enum` `({name})`";
        }

        if (field.Type == DataType.Array)
        {
            var inner = field.ElementType ?? "Unknown";
            bool isComplexType = !Enum.TryParse<DataType>(inner, true, out _);
            
            if (inner == "Enum")
            {
                 var enumName = field.EnumType ?? "Unknown";
                 return enumName != "Unknown" 
                    ? $"`Array` `Enum` [`({enumName})`]({enumName})" 
                    : $"`Array` `Enum`";
            }
            else if (isComplexType)
            {
                return $"`Array` [`({inner})`]({inner})";
            }
            else
            {
                return $"`Array` `({inner})`";
            }
        }

        return $"`{field.Type.ToString()}`";
    }
}