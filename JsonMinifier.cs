using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JsonMinifier
{
    [JsonSerializable(typeof(JsonNode))]
    [JsonSourceGenerationOptions(
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    internal partial class JsonMinifierContext : JsonSerializerContext
    {
    }

    [JsonSerializable(typeof(JsonNode))]
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    internal partial class JsonFormattedContext : JsonSerializerContext
    {
    }

    class Program
    {
        private static Dictionary<string, string> nameMap = new Dictionary<string, string>();
        private static int counter = 1;

        // Names that should never be replaced (important structural/asset names)
        private static readonly HashSet<string> excludedNames = new HashSet<string>
        {
            "Static"
        };

        // Check if a name/ID should be excluded from replacement
        private static bool IsExcluded(string nameOrId)
        {
            if (string.IsNullOrEmpty(nameOrId))
                return true;

            // Exclude anything in the Static/ tree (IDs starting with "Static/")
            if (nameOrId.StartsWith("Static/"))
                return true;

            // Exclude exact matches from the exclusion list
            if (excludedNames.Contains(nameOrId))
                return true;

            return false;
        }

        // Properties that contain single ID references to other objects
        private static readonly HashSet<string> singleIdReferenceProperties = new HashSet<string>
        {
            "HQArea",
            "CombatVolume",
            "CaptureArea",
            "Area",
            "SurroundingCombatArea",
            "ExclusionAreaTeam1",
            "ExclusionAreaTeam2",
            "ExclusionAreaTeam1_OBB",
            "ExclusionAreaTeam2_OBB",
            "DestructionArea",
            "MapDetailRenderArea",
            "SectorArea",
            "RetreatArea",
            "RetreatFromArea",
            "AdvanceFromArea",
            "AdvanceToArea"
        };

        // Properties that contain arrays of ID references to other objects
        private static readonly HashSet<string> arrayIdReferenceProperties = new HashSet<string>
        {
            "InfantrySpawns",
            "ForwardSpawns",
            "InfantrySpawnPoints_Team1",
            "InfantrySpawnPoints_Team2",
            "SpawnPoints",
            "CapturePoints",
            "MCOMs"
        };

        // Settings for different optimization features
        private static bool enableNameIdReplacement = true;
        private static bool enablePrecisionReduction = true;
        private static bool showNameMappings = false;
        private static bool useFormattedOutput = false;
        private static int precisionDigits = 6;

        static void Main(string[] args)
        {
            string inputFile = "pl_badwater.spatial.json";
            string? outputFile = null; // Will be auto-generated from input filename

            // Parse command line arguments
            ParseArguments(args, ref inputFile, ref outputFile);

            // Auto-generate output filename if not specified
            if (outputFile == null)
            {
                outputFile = GenerateOutputFilename(inputFile);
            }

            try
            {
                Console.WriteLine($"Loading JSON from: {inputFile}");

                // Read the JSON file
                string jsonContent = File.ReadAllText(inputFile);

                // Parse as JsonNode for dynamic manipulation
                JsonNode? rootNode = JsonNode.Parse(jsonContent);
                if (rootNode == null)
                {
                    throw new InvalidOperationException("Failed to parse JSON content");
                }

                // Replace names and IDs recursively (if enabled)
                if (enableNameIdReplacement)
                {
                    Console.WriteLine("Replacing names and IDs with short identifiers...");
                    
                    // Two-pass approach:
                    // Pass 1: Collect all names and IDs to build the complete mapping
                    CollectNamesAndIds(rootNode);
                    
                    // Pass 2: Replace all references using the complete mapping
                    ReplaceReferencesRecursively(rootNode);
                }

                // Serialize with configurable formatting
                string minifiedJson;
                if (useFormattedOutput)
                {
                    // Serialize with formatted context (default 2-space indentation)
                    string tempJson = JsonSerializer.Serialize(rootNode, JsonFormattedContext.Default.JsonNode);
                    
                    // Convert 2-space indentation to 4-space indentation
                    minifiedJson = ConvertToFourSpaceIndentation(tempJson);
                }
                else
                {
                    minifiedJson = JsonSerializer.Serialize(rootNode, JsonMinifierContext.Default.JsonNode);
                }

                // Reduce numeric precision in the final JSON string (if enabled)
                string finalJson = minifiedJson;
                if (enablePrecisionReduction)
                {
                    Console.WriteLine($"Reducing numeric precision to {precisionDigits} digits...");
                    finalJson = ReduceNumericPrecision(minifiedJson, precisionDigits);
                }

                // Write the minified JSON
                File.WriteAllText(outputFile, finalJson);

                Console.WriteLine($"Minified JSON saved to: {outputFile}");

                if (enableNameIdReplacement)
                {
                    Console.WriteLine($"Replaced {nameMap.Count} unique names and IDs");
                }
                Console.WriteLine($"Original size: {new FileInfo(inputFile).Length:N0} bytes");
                Console.WriteLine($"Minified size: {new FileInfo(outputFile).Length:N0} bytes");

                var originalSize = new FileInfo(inputFile).Length;
                var minifiedSize = new FileInfo(outputFile).Length;
                var reductionPercent = ((double)(originalSize - minifiedSize) / originalSize) * 100;
                Console.WriteLine($"Size reduction: {reductionPercent:F1}%");

                // Optionally print the name/ID mappings
                if (showNameMappings && enableNameIdReplacement && nameMap.Count > 0)
                {
                    Console.WriteLine($"\nName/ID mappings:");
                    foreach (var kvp in nameMap)
                    {
                        Console.WriteLine($"  {kvp.Key} -> {kvp.Value}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private static string ConvertToFourSpaceIndentation(string json)
        {
            var lines = json.Split('\n');
            
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int leadingSpaces = 0;
                
                // Count leading spaces
                for (int j = 0; j < line.Length; j++)
                {
                    if (line[j] == ' ')
                        leadingSpaces++;
                    else
                        break;
                }
                
                // If we have leading spaces that are multiples of 2, convert to multiples of 4
                if (leadingSpaces > 0 && leadingSpaces % 2 == 0)
                {
                    int indentLevel = leadingSpaces / 2;
                    string newIndent = new string(' ', indentLevel * 4);
                    lines[i] = newIndent + line.Substring(leadingSpaces);
                }
            }
            
            return string.Join('\n', lines);
        }

        private static void ParseArguments(string[] args, ref string inputFile, ref string? outputFile)
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-h":
                    case "--help":
                        ShowHelp();
                        Environment.Exit(0);
                        break;

                    case "--no-rename":
                        enableNameIdReplacement = false;
                        Console.WriteLine("Name and ID replacement disabled");
                        break;

                    case "--no-precision":
                        enablePrecisionReduction = false;
                        Console.WriteLine("Precision reduction disabled");
                        break;

                    case "--show-mappings":
                        showNameMappings = true;
                        break;

                    case "--formatted":
                    case "--pretty":
                        useFormattedOutput = true;
                        Console.WriteLine("Formatted output enabled (with whitespace and indentation)");
                        break;

                    case "--precision":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int digits) && digits > 0 && digits <= 15)
                        {
                            precisionDigits = digits;
                            i++; // Skip the next argument as we've consumed it
                            Console.WriteLine($"Precision set to {precisionDigits} digits");
                        }
                        else
                        {
                            Console.WriteLine("Invalid precision value. Using default (6).");
                        }
                        break;

                    case "-i":
                    case "--input":
                        if (i + 1 < args.Length)
                        {
                            inputFile = args[i + 1];
                            i++; // Skip the next argument as we've consumed it
                        }
                        else
                        {
                            Console.WriteLine("Missing input file argument.");
                            Environment.Exit(1);
                        }
                        break;

                    case "--out":
                        if (i + 1 < args.Length)
                        {
                            outputFile = args[i + 1];
                            i++; // Skip the next argument as we've consumed it
                        }
                        else
                        {
                            Console.WriteLine("Missing output file argument.");
                            Environment.Exit(1);
                        }
                        break;

                    default:
                        // If it doesn't start with -, treat as positional argument (input file)
                        if (!args[i].StartsWith("-"))
                        {
                            inputFile = args[i];
                        }
                        else
                        {
                            Console.WriteLine($"Unknown argument: {args[i]}");
                            ShowHelp();
                            Environment.Exit(1);
                        }
                        break;
                }
            }
        }

        private static string GenerateOutputFilename(string inputFile)
        {
            string directory = Path.GetDirectoryName(inputFile) ?? "";
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputFile);
            string extension = Path.GetExtension(inputFile);

            // Generate output filename like: input.json -> input.minified.json
            string outputFileName = $"{fileNameWithoutExtension}.minified{extension}";
            return Path.Combine(directory, outputFileName);
        }

        private static void ShowHelp()
        {
            Console.WriteLine("BF6 Spatial JSON Minifier - Optimizes Battlefield 6 spatial editor exported JSON files");
            Console.WriteLine("Reduces file size by replacing names with short identifiers and reducing numeric precision");
            Console.WriteLine();
            Console.WriteLine("Usage: JsonMinifier [options] [input_file]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -h, --help           Show this help message");
            Console.WriteLine("  -i, --input FILE     Input JSON file (default: pl_badwater.spatial.json)");
            Console.WriteLine("  --out FILE           Output JSON file (default: auto-generated from input filename)");
            Console.WriteLine("  --no-rename          Disable name and ID replacement with short identifiers");
            Console.WriteLine("  --no-precision       Disable numeric precision reduction");
            Console.WriteLine("  --precision DIGITS   Set precision digits (1-15, default: 6)");
            Console.WriteLine("  --show-mappings      Show name/ID mappings in output");
            Console.WriteLine("  --formatted, --pretty Output with whitespace and indentation (default: minified)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  JsonMinifier input.json");
            Console.WriteLine("  JsonMinifier --out custom-output.json input.json");
            Console.WriteLine("  JsonMinifier --no-rename --precision 3 input.json");
            Console.WriteLine("  JsonMinifier --formatted --show-mappings input.json");
            Console.WriteLine("  JsonMinifier --show-mappings --precision 7 -i input.json --out output.json");
        }

        // Pass 1: Collect all names and IDs to build the complete mapping
        private static void CollectNamesAndIds(JsonNode? node)
        {
            if (node == null)
                return;

            switch (node)
            {
                case JsonObject obj:
                    foreach (var property in obj)
                    {
                        // Collect names from "name" property values
                        if (property.Key == "name" && property.Value is JsonValue jsonValue && enableNameIdReplacement)
                        {
                            if (jsonValue.TryGetValue<string>(out string? nameValue) && nameValue != null)
                            {
                                GetOrCreateShortName(nameValue);
                            }
                        }
                        // Collect IDs from "id" property values
                        else if (property.Key == "id" && property.Value is JsonValue idValue && enableNameIdReplacement)
                        {
                            if (idValue.TryGetValue<string>(out string? idValueString) && idValueString != null)
                            {
                                GetOrCreateShortId(idValueString);
                            }
                        }
                        
                        // Recursively collect from nested structures
                        CollectNamesAndIds(property.Value);
                    }
                    break;

                case JsonArray array:
                    for (int i = 0; i < array.Count; i++)
                    {
                        CollectNamesAndIds(array[i]);
                    }
                    break;
            }
        }

        // Pass 2: Replace all references using the complete mapping
        private static void ReplaceReferencesRecursively(JsonNode? node)
        {
            if (node == null)
                return;

            switch (node)
            {
                case JsonObject obj:
                    foreach (var property in obj)
                    {
                        // Replace names in "name" property values
                        if (property.Key == "name" && property.Value is JsonValue jsonValue && enableNameIdReplacement)
                        {
                            if (jsonValue.TryGetValue<string>(out string? nameValue) && nameValue != null)
                            {
                                // Check if this object has a Static/ ID - if so, don't rename its name
                                bool isStaticObject = false;
                                if (obj.TryGetPropertyValue("id", out JsonNode? idNode) && idNode is JsonValue objectIdValue)
                                {
                                    if (objectIdValue.TryGetValue<string>(out string? idString) && idString != null)
                                    {
                                        isStaticObject = idString.StartsWith("Static/");
                                    }
                                }

                                if (!isStaticObject && nameMap.ContainsKey(nameValue))
                                {
                                    obj["name"] = nameMap[nameValue];
                                }
                            }
                        }
                        // Replace IDs in "id" property values
                        else if (property.Key == "id" && property.Value is JsonValue idValue && enableNameIdReplacement)
                        {
                            if (idValue.TryGetValue<string>(out string? idValueString) && idValueString != null)
                            {
                                if (nameMap.ContainsKey(idValueString))
                                {
                                    obj["id"] = nameMap[idValueString];
                                }
                            }
                        }
                        // Handle single ID reference properties
                        else if (singleIdReferenceProperties.Contains(property.Key) && property.Value is JsonValue singleRefValue)
                        {
                            if (singleRefValue.TryGetValue<string>(out string? refId) && refId != null)
                            {
                                if (nameMap.ContainsKey(refId))
                                {
                                    obj[property.Key] = nameMap[refId];
                                }
                            }
                        }
                        // Handle array ID reference properties
                        else if (arrayIdReferenceProperties.Contains(property.Key) && property.Value is JsonArray refArray)
                        {
                            for (int i = 0; i < refArray.Count; i++)
                            {
                                if (refArray[i] is JsonValue arrayValue && arrayValue.TryGetValue<string>(out string? arrayRefId) && arrayRefId != null)
                                {
                                    if (nameMap.ContainsKey(arrayRefId))
                                    {
                                        refArray[i] = nameMap[arrayRefId];
                                    }
                                }
                            }
                        }
                        // Handle regular arrays (that might contain objects)
                        else if (property.Value is JsonArray array && !arrayIdReferenceProperties.Contains(property.Key))
                        {
                            for (int i = 0; i < array.Count; i++)
                            {
                                ReplaceReferencesRecursively(array[i]);
                            }
                        }
                        else
                        {
                            // Recursively process nested objects
                            ReplaceReferencesRecursively(property.Value);
                        }
                    }
                    break;

                case JsonArray array:
                    for (int i = 0; i < array.Count; i++)
                    {
                        ReplaceReferencesRecursively(array[i]);
                    }
                    break;
            }
        }

        private static string GetOrCreateShortName(string originalName)
        {
            // Don't replace names that are in the exclusion list
            if (IsExcluded(originalName))
                return originalName;

            if (nameMap.ContainsKey(originalName))
                return nameMap[originalName];

            string shortName = GenerateShortName(counter++);
            nameMap[originalName] = shortName;
            return shortName;
        }

        private static string GenerateShortName(int number)
        {
            // Generate short names like: a, b, c, ..., z, aa, ab, ac, etc.
            string result = "";
            while (number > 0)
            {
                number--; // Make it 0-based
                result = (char)('a' + (number % 26)) + result;
                number /= 26;
            }
            return result;
        }

        private static string GetOrCreateShortId(string originalId)
        {
            if (string.IsNullOrEmpty(originalId))
                return originalId;

            // Don't replace IDs that are in the exclusion list
            if (IsExcluded(originalId))
                return originalId;

            // Check if we already have a mapping for this ID
            if (nameMap.ContainsKey(originalId))
                return nameMap[originalId];

            // For hierarchical IDs (like "TEAM_1_HQ/SpawnPoint_1_1"), try to build from existing mappings
            if (originalId.Contains("/"))
            {
                var parts = originalId.Split('/');
                var newParts = new string[parts.Length];

                for (int i = 0; i < parts.Length; i++)
                {
                    // Check if this part is excluded
                    if (IsExcluded(parts[i]))
                    {
                        newParts[i] = parts[i]; // Keep the original
                    }
                    // If this part already has a mapping, use it
                    else if (nameMap.ContainsKey(parts[i]))
                    {
                        newParts[i] = nameMap[parts[i]];
                    }
                    else
                    {
                        // Create a new short name for this part
                        newParts[i] = GetOrCreateShortName(parts[i]);
                    }
                }

                string newId = string.Join("/", newParts);
                nameMap[originalId] = newId;
                return newId;
            }

            // For simple IDs, check if it matches a name we already have a mapping for
            if (nameMap.ContainsKey(originalId))
            {
                return nameMap[originalId];
            }

            // Otherwise, create a new short identifier for this ID
            string shortId = GetOrCreateShortName(originalId);
            return shortId;
        }

        private static string ReduceNumericPrecision(string json, int maxDigits)
        {
            // Use regex to find all decimal numbers in the JSON
            return Regex.Replace(json,
                @"-?\d+\.\d+",
                match =>
                {
                    if (double.TryParse(match.Value, out double value))
                    {
                        // Round to the specified number of significant digits
                        double rounded = Math.Round(value, maxDigits);

                        // Convert back to string, removing unnecessary trailing zeros
                        string result = rounded.ToString($"G{maxDigits}");

                        // Ensure we don't have more decimal places than needed
                        if (result.Contains('.'))
                        {
                            result = result.TrimEnd('0').TrimEnd('.');
                        }

                        return result;
                    }
                    return match.Value; // Return original if parsing fails
                });
        }
    }
}