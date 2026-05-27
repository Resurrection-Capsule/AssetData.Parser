using System.Text.RegularExpressions;
using System.Xml;
using AssetData.Parser;

namespace AssetData.Parser.CLI;

/// <summary>
/// CLI for AssetData.Parser with DBPF support.
/// </summary>
public static partial class Program
{
    // Pattern for catalog files: catalog_N.bin
    [GeneratedRegex(@"^catalog_\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex CatalogPattern();
    
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        // Parse arguments
        string? inputFile = null;
        string? dbpfPackage = null;
        string? assetName = null;
        string? registryDir = null;
        string? outputDir = null;
        bool outputXml = false;
        bool verbose = false;
        bool listAssets = false;
        string? filterType = null;
        int? randomSeed = null;
        
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-d" or "--dbpf":
                    if (i + 1 < args.Length)
                        dbpfPackage = args[++i];
                    break;
                case "-a" or "--asset":
                    if (i + 1 < args.Length)
                        assetName = args[++i];
                    break;
                case "-r" or "--registries":
                    if (i + 1 < args.Length)
                        registryDir = args[++i];
                    break;
                case "-o" or "--output":
                    if (i + 1 < args.Length)
                        outputDir = args[++i];
                    break;
                case "--xml":
                    outputXml = true;
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                case "-l" or "--list":
                    listAssets = true;
                    break;
                case "-t" or "--type":
                    if (i + 1 < args.Length)
                        filterType = args[++i];
                    break;
                case "--seed":
                    if (i + 1 < args.Length)
                        randomSeed = int.Parse(args[++i]);
                    break;
                case "-h" or "--help":
                    PrintUsage();
                    return 0;
                default:
                    if (!args[i].StartsWith('-'))
                        inputFile = args[i];
                    break;
            }
        }
        
        try
        {
            // Mode 1: DBPF package
            if (!string.IsNullOrEmpty(dbpfPackage))
            {
                return HandleDbpf(dbpfPackage, assetName, registryDir, outputDir, verbose, 
                    listAssets, filterType, randomSeed);
            }
            
            // Mode 2: Single file
            if (!string.IsNullOrEmpty(inputFile))
            {
                return HandleSingleFile(inputFile, outputDir, outputXml, verbose);
            }
            
            Console.Error.WriteLine("Error: No input specified.");
            PrintUsage();
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (verbose)
                Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
    
    /// <summary>
    /// Parse random asset specification: random:Type:Count
    /// </summary>
    private static (bool isRandom, string? type, int count) ParseRandomSpec(string assetArg)
    {
        if (!assetArg.StartsWith("random:", StringComparison.OrdinalIgnoreCase))
            return (false, null, 1);
        
        var parts = assetArg.Split(':', StringSplitOptions.RemoveEmptyEntries);
        
        // random: → any type, count 1
        if (parts.Length == 1)
            return (true, null, 1);
        
        // random:Phase → Phase type, count 1
        // random:* → any type, count 1
        if (parts.Length == 2)
        {
            var type = parts[1] == "*" ? null : parts[1];
            return (true, type, 1);
        }
        
        // random:Phase:5 → Phase type, count 5
        // random:*:5 → any type, count 5
        if (parts.Length == 3)
        {
            var type = parts[1] == "*" ? null : parts[1];
            var count = int.Parse(parts[2]);
            return (true, type, count);
        }
        
        throw new ArgumentException($"Invalid random specification: {assetArg}");
    }
    
    /// <summary>
    /// Get random assets from catalog
    /// </summary>
    private static List<string> GetRandomAssets(DbpfReader dbpf, AssetService service, 
        string? typeFilter, int count, int? seed)
    {
        // Find catalog file
        byte[]? catalogData = null;
        
        for (int i = 131; i <= 150; i++)
        {
            var testName = $"catalog_{i}.bin";
            var data = dbpf.GetAsset(testName);
            if (data != null && data.Length > 0)
            {
                catalogData = data;
                break;
            }
        }
        
        if (catalogData == null)
        {
            var data = dbpf.GetAsset("catalog_0.bin");
            if (data != null && data.Length > 0)
                catalogData = data;
        }
        
        if (catalogData == null)
            throw new InvalidOperationException("No catalog found in package. Cannot select random assets.");
        
        // Parse catalog
        var catalogRoot = service.Parser.Parse(catalogData, "Catalog", 8);
        var entriesArray = catalogRoot.Children
            .OfType<ArrayNode>()
            .FirstOrDefault(n => n.Name == "entries");
        
        if (entriesArray == null)
            throw new InvalidOperationException("Invalid catalog structure.");
        
        // Extract asset names
        var assetNames = new List<string>();
        
        foreach (var entryNode in entriesArray.Children.OfType<StructNode>())
        {
            var nameNode = entryNode.Children
                .OfType<StringNode>()
                .FirstOrDefault(n => n.Name == "assetNameWType");
            
            if (nameNode == null || string.IsNullOrWhiteSpace(nameNode.Value))
                continue;
            
            // Apply type filter if specified
            if (!string.IsNullOrEmpty(typeFilter))
            {
                var parts = nameNode.Value.Split('.', 2);
                if (parts.Length < 2 || !parts[1].Equals(typeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            
            assetNames.Add(nameNode.Value);
        }
        
        if (assetNames.Count == 0)
        {
            var filterMsg = string.IsNullOrEmpty(typeFilter) ? "any type" : $"type '{typeFilter}'";
            throw new InvalidOperationException($"No assets found with {filterMsg}.");
        }
        
        // Select random assets
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var selected = new List<string>();
        
        // If requesting more than available, just return all (shuffled)
        if (count >= assetNames.Count)
        {
            selected = assetNames.OrderBy(_ => random.Next()).ToList();
        }
        else
        {
            // Select without replacement
            var indices = Enumerable.Range(0, assetNames.Count).ToList();
            for (int i = 0; i < count; i++)
            {
                var idx = random.Next(indices.Count);
                selected.Add(assetNames[indices[idx]]);
                indices.RemoveAt(idx);
            }
        }
        
        return selected;
    }
    
    /// <summary>
    /// Determine asset type from name, handling special patterns like catalog_N.bin
    /// </summary>
    private static (string baseName, string? typeName) ParseAssetName(string assetName)
    {
        // Remove .bin extension if present
        var name = assetName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) 
            ? assetName[..^4] 
            : assetName;
        
        // Check for catalog pattern: catalog_N → Catalog type
        if (CatalogPattern().IsMatch(name))
        {
            return (name, "Catalog");
        }
        
        // Standard format: name.Type
        var parts = name.Split('.', 2);
        if (parts.Length > 1)
        {
            return (parts[0], parts[1]);
        }
        
        // No type extension - return null for type
        return (name, null);
    }
    
    private static int HandleDbpf(string dbpfPath, string? assetName, string? registryDir, 
        string? outputDir, bool verbose, bool listAssets, string? filterType, int? randomSeed)
    {
        if (!File.Exists(dbpfPath))
        {
            Console.Error.WriteLine($"Error: DBPF file not found: {dbpfPath}");
            return 1;
        }
        
        using var dbpf = new DbpfReader(dbpfPath);
        
        if (verbose)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($">>> Opening: {Path.GetFileName(dbpfPath)}");
            Console.ResetColor();
        }
        
        // Load registries if provided
        if (!string.IsNullOrEmpty(registryDir))
        {
            dbpf.LoadRegistries(registryDir);
            if (verbose)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"    Registries: {registryDir}");
                Console.ResetColor();
            }
        }
        
        if (verbose)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"    Entries: {dbpf.Entries.Count}");
            Console.ResetColor();
        }
        
        // Mode: List assets
        if (listAssets)
        {
            if (!string.IsNullOrEmpty(filterType))
            {
                foreach (var (name, _) in dbpf.ListAssetsByType(filterType))
                    Console.WriteLine(name);
            }
            else
            {
                foreach (var name in dbpf.ListAssets())
                    Console.WriteLine(name);
            }
            return 0;
        }
        
        // Mode: Parse specific asset(s)
        if (!string.IsNullOrEmpty(assetName))
        {
            // Check for random specification
            var (isRandom, typeFilter, count) = ParseRandomSpec(assetName);
            
            if (isRandom)
            {
                // Random mode
                var service = new AssetService();
                var randomAssets = GetRandomAssets(dbpf, service, typeFilter, count, randomSeed);
                
                if (verbose)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    var typeMsg = string.IsNullOrEmpty(typeFilter) ? "any type" : $"type '{typeFilter}'";
                    var seedMsg = randomSeed.HasValue ? $" (seed: {randomSeed})" : "";
                    Console.WriteLine($">>> Selected {randomAssets.Count} random asset(s) of {typeMsg}{seedMsg}");
                    Console.ResetColor();
                    Console.WriteLine();
                }
                
                // Process each random asset
                int successCount = 0;
                int failCount = 0;
                
                foreach (var randomAsset in randomAssets)
                {
                    try
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[Processing: {randomAsset}]");
                        Console.ResetColor();
                        
                        var result = ProcessSingleAsset(dbpf, randomAsset, registryDir, outputDir, verbose);
                        if (result == 0)
                            successCount++;
                        else
                            failCount++;
                        
                        if (randomAssets.Count > 1)
                            Console.WriteLine(); // Separator between assets
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Error processing {randomAsset}: {ex.Message}");
                        Console.ResetColor();
                        failCount++;
                    }
                }
                
                // Summary
                if (randomAssets.Count > 1)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[Result]:");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Success: {successCount}");
                    if (failCount > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Failed: {failCount}");
                    }
                    Console.ResetColor();
                }
                
                return failCount > 0 ? 1 : 0;
            }
            else
            {
                // Specific asset mode
                return ProcessSingleAsset(dbpf, assetName, registryDir, outputDir, verbose);
            }
        }
        
        // No specific asset requested - just show info
        Console.WriteLine($"DBPF: {Path.GetFileName(dbpfPath)}");
        Console.WriteLine($"Entries: {dbpf.Entries.Count}");
        Console.WriteLine("Use -l to list assets, or -a <n> to parse a specific asset.");
        Console.WriteLine("Use -a random:Type to parse a random asset of that type.");
        return 0;
    }
    
    private static int ProcessSingleAsset(DbpfReader dbpf, string assetName, 
        string? registryDir, string? outputDir, bool verbose)
    {
        var data = dbpf.GetAsset(assetName);
        if (data == null)
        {
            Console.Error.WriteLine($"Error: Asset not found: {assetName}");
            return 1;
        }
        
        // Parse asset name to get type
        var (baseName, typeName) = ParseAssetName(assetName);
        
        if (string.IsNullOrEmpty(typeName))
        {
            Console.Error.WriteLine($"Error: Cannot determine asset type from name: {assetName}");
            Console.Error.WriteLine($"       Try using the full name with extension (e.g., 'default.AffixTuning')");
            Console.Error.WriteLine($"       Or for catalog files: 'catalog_131.bin' or 'catalog_131'");
            return 1;
        }
        
        var service = new AssetService();

        if (verbose)
        {
            var reg = service.Parser.Registry;
            var issues = service.Parser.RegistryIssues;
            Console.ForegroundColor = issues.Count == 0 ? ConsoleColor.DarkGray : ConsoleColor.DarkRed;
            Console.WriteLine($"    Registry: {reg.Count} types, {issues.Count} unresolved ref(s)");
            foreach (var issue in issues)
                Console.WriteLine($"      - {issue}");
            Console.ResetColor();
        }

        var fileType = service.GetFileType(typeName);

        if (fileType == null)
        {
            Console.Error.WriteLine($"Error: Unknown asset type: {typeName}");
            return 1;
        }
        
        var root = service.Parser.Parse(data, fileType.RootStruct, fileType.HeaderSize);
        
        if (verbose)
            PrintTree(root, 0);
        
        // Output XML if requested
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
            var outputPath = Path.Combine(outputDir, baseName + ".xml");
            
            var xml = AssetSerializer.ToXml(root);
            var settings = new XmlWriterSettings { Indent = true };
            using var writer = XmlWriter.Create(outputPath, settings);
            xml.WriteTo(writer);
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"    {assetName} -> {Path.GetFileName(outputPath)}");
            Console.ResetColor();
        }
        
        return 0;
    }
    
    private static int HandleSingleFile(string inputPath, string? outputDir, bool outputXml, bool verbose)
    {
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: File not found: {inputPath}");
            return 1;
        }
        
        var service = new AssetService();
        var root = service.LoadFile(inputPath);
        
        if (verbose || !outputXml)
            PrintTree(root, 0);
        
        if (outputXml || !string.IsNullOrEmpty(outputDir))
        {
            var xml = AssetSerializer.ToXml(root);
            
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                var outputPath = Path.Combine(outputDir, 
                    Path.GetFileNameWithoutExtension(inputPath) + ".xml");
                
                var settings = new XmlWriterSettings { Indent = true };
                using var writer = XmlWriter.Create(outputPath, settings);
                xml.WriteTo(writer);
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Written: {outputPath}");
                Console.ResetColor();
            }
            else
            {
                // Output to stdout
                var settings = new XmlWriterSettings { Indent = true };
                using var writer = XmlWriter.Create(Console.Out, settings);
                xml.WriteTo(writer);
            }
        }
        
        return 0;
    }
    
    /// <summary>
    /// Renders tree with syntax highlighting.
    /// </summary>
    private static void PrintTree(AssetNode node, int indent)
    {
        // 1. Ocultar completamente campos Nullables vazios para limpar o log
        if (node is NullNode) return;

        var prefix = new string(' ', indent * 2);
        Console.Write(prefix);

        // --- Determinar o Nome Exato do Tipo ---
        string typeLabel = node.Kind.ToString();

        switch (node)
        {
            case NumberNode n:
                // Ex: Int32, UInt32, Float, HashId
                typeLabel = n.OriginalType.ToString(); 
                break;
            case VectorNode v:
                // Ex: Vector3, Vector2, Orientation
                typeLabel = v.VectorType.ToString(); 
                break;
            case BooleanNode:
                typeLabel = "Bool";
                break;
            // Asset, Struct, Enum, String, Array usam o Kind padrão
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write(typeLabel);
        
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(".");
        
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(node.Name);
        
        // --- Valor (Início) ---
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("(");
        
        // --- Renderização do Valor ---
        if (node is VectorNode vec)
        {
             PrintVector(vec);
        }
        else
        {
            switch (node)
            {
                case StringNode sn:
                    Console.ForegroundColor = ConsoleColor.Green; 
                    Console.Write(sn.Value);
                    break;
                case NumberNode nn:
                    if (nn.Format == NumberFormat.Hex)
                        Console.ForegroundColor = ConsoleColor.Blue;
                    else
                        Console.ForegroundColor = ConsoleColor.Magenta;
                    
                    Console.Write(nn.DisplayValue);
                    break;
                case BooleanNode bn:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write(bn.DisplayValue);
                    break;
                case EnumNode en:
                    Console.ForegroundColor = ConsoleColor.Blue; 
                    Console.Write(en.DisplayValue);
                    break;
                case ArrayNode an:
                    Console.ForegroundColor = ConsoleColor.DarkYellow; 
                    Console.Write(an.Children.Count);
                    break;
                case StructNode sn:
                    Console.ForegroundColor = ConsoleColor.DarkCyan; 
                    Console.Write(sn.TypeName);
                    break;
                case AssetNode an when an.Kind == AssetNodeKind.Asset:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write(an.DisplayValue);
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write(node.DisplayValue);
                    break;
            }
        }
        
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write(")");
        Console.ResetColor();
        Console.WriteLine();
        
        // Recursão para filhos
        foreach (var child in node.Children)
            PrintTree(child, indent + 1);
    }

    /// <summary>
    /// Helper to print styled vector components (X=Red, Y=Green, Z=Blue)
    /// </summary>
    private static void PrintVector(VectorNode vec)
    {
        string fmt = "0.######";
        var ci = System.Globalization.CultureInfo.InvariantCulture;

        void PrintComp(string label, float val, ConsoleColor color, bool comma = true)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(label);
            Console.Write(": ");

            Console.ForegroundColor = color;
            Console.Write(val.ToString(fmt, ci));
            
            if (comma)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(", ");
            }
        }

        switch (vec.VectorType)
        {
            case VectorType.Vector2:
                PrintComp("x", vec.X, ConsoleColor.Magenta);
                PrintComp("y", vec.Y, ConsoleColor.Magenta, false);
                break;
            case VectorType.Vector3:
                PrintComp("x", vec.X, ConsoleColor.Magenta);
                PrintComp("y", vec.Y, ConsoleColor.Magenta);
                PrintComp("z", vec.Z, ConsoleColor.Magenta, false);
                break;
            case VectorType.Vector4:
            case VectorType.Orientation:
                PrintComp("x", vec.X, ConsoleColor.Magenta);
                PrintComp("y", vec.Y, ConsoleColor.Magenta);
                PrintComp("z", vec.Z, ConsoleColor.Magenta);
                PrintComp("w", vec.W, ConsoleColor.Yellow, false);
                break;
        }
    }
    
    private static void PrintUsage()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("AssetData.Parser");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine("Darkspore Binary Asset Parser\n");
        Console.ResetColor();
        
        Console.WriteLine(@"Usage:
  AssetData.Parser <file>                     Parse single asset file
  AssetData.Parser -d <package> [options]     Parse from DBPF package

Single File Options:
  <file>              Input asset file (.noun, .phase, etc.)
  --xml               Output as XML
  -o <dir>            Output directory
  -v, --verbose       Verbose output

DBPF Package Options:
  -d, --dbpf <file>   DBPF/DBBF package file
  -a, --asset <n>     Asset to extract (e.g., 'default.AffixTuning', 'catalog_131.bin')
                      Or use 'random:Type' for random selection:
                        random:Phase      - 1 random Phase
                        random:Phase:5    - 5 random Phases
                        random:*          - 1 random asset (any type)
                        random:*:10       - 10 random assets
  -r, --registries    Registry directory for name resolution
  -l, --list          List all assets in package
  -t, --type <ext>    Filter by type extension (with -l)
  -o <dir>            Output directory for XML
  -v, --verbose       Show parsed tree
  --seed <n>          Random seed for reproducible selection

Examples:");
        
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  # Parse single file");
        Console.WriteLine("  AssetData.Parser creature.noun --xml -o output/");
        Console.WriteLine();
        Console.WriteLine("  # List assets in package");
        Console.WriteLine("  AssetData.Parser -d AssetData_Binary.package -l");
        Console.WriteLine("  AssetData.Parser -d AssetData_Binary.package -l -t noun");
        Console.WriteLine();
        Console.WriteLine("  # Parse specific asset");
        Console.WriteLine("  AssetData.Parser -d AssetData_Binary.package -a default.AffixTuning -r registries -v");
        Console.WriteLine("  AssetData.Parser -d AssetData_Binary.package -a ZelemBoss.phase -r registries -o output/");
        Console.WriteLine();
        Console.WriteLine("  # Random asset selection");
        Console.WriteLine("  AssetData.Parser -d AssetData_Binary.package -a random:Phase -r registries -v");
        Console.WriteLine("  AssetData.Parser -d AssetData_Binary.package -a random:Noun:5 -r registries");
        Console.WriteLine("  AssetData.Parser -d AssetData_Binary.package -a random:*:3 -r registries --seed 42");
        Console.ResetColor();
    }
}