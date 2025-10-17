using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JsonMinifier
{
    class Program
    {
        private static Dictionary<string, string> nameMap = new Dictionary<string, string>();
        private static int counter = 1;

        // Names that should never be replaced (important structural/asset names)
        private static readonly HashSet<string> excludedNames = new HashSet<string>
        {
            "Static",
            "MP_Limestone_Terrain",
            "MP_Limestone_Assets"
        };

        // Settings for different optimization features
        private static bool enableNameReplacement = true;
        private static bool enablePrecisionReduction = true;
        private static bool enableIdReplacement = true;
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
                if (enableNameReplacement || enableIdReplacement)
                {
                    if (enableNameReplacement)
                        Console.WriteLine("Replacing names with short identifiers...");
                    if (enableIdReplacement)
                        Console.WriteLine("Replacing IDs with short identifiers...");
                    ReplaceNamesRecursively(rootNode);
                }

                // Serialize with configurable formatting
                var options = new JsonSerializerOptions
                {
                    WriteIndented = useFormattedOutput,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string minifiedJson = JsonSerializer.Serialize(rootNode, options);

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

                if (enableNameReplacement || enableIdReplacement)
                {
                    if (enableNameReplacement && enableIdReplacement)
                        Console.WriteLine($"Replaced {nameMap.Count} unique names and IDs");
                    else if (enableNameReplacement)
                        Console.WriteLine($"Replaced {nameMap.Count} unique names");
                    else
                        Console.WriteLine($"Replaced {nameMap.Count} unique IDs");
                }
                Console.WriteLine($"Original size: {new FileInfo(inputFile).Length:N0} bytes");
                Console.WriteLine($"Minified size: {new FileInfo(outputFile).Length:N0} bytes");

                var originalSize = new FileInfo(inputFile).Length;
                var minifiedSize = new FileInfo(outputFile).Length;
                var reductionPercent = ((double)(originalSize - minifiedSize) / originalSize) * 100;
                Console.WriteLine($"Size reduction: {reductionPercent:F1}%");

                // Optionally print the name/ID mappings
                if (showNameMappings && (enableNameReplacement || enableIdReplacement) && nameMap.Count > 0)
                {
                    string mappingType = (enableNameReplacement && enableIdReplacement) ? "Name/ID mappings" :
                                       enableNameReplacement ? "Name mappings" : "ID mappings";
                    Console.WriteLine($"\n{mappingType}:");
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

                    case "--no-names":
                        enableNameReplacement = false;
                        Console.WriteLine("Name replacement disabled");
                        break;

                    case "--no-precision":
                        enablePrecisionReduction = false;
                        Console.WriteLine("Precision reduction disabled");
                        break;

                    case "--no-ids":
                        enableIdReplacement = false;
                        Console.WriteLine("ID replacement disabled");
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
            Console.WriteLine("  --no-names           Disable name replacement with short identifiers");
            Console.WriteLine("  --no-ids             Disable ID replacement with short identifiers");
            Console.WriteLine("  --no-precision       Disable numeric precision reduction");
            Console.WriteLine("  --precision DIGITS   Set precision digits (1-15, default: 6)");
            Console.WriteLine("  --show-mappings      Show name mappings in output");
            Console.WriteLine("  --formatted, --pretty Output with whitespace and indentation (default: minified)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  JsonMinifier input.json");
            Console.WriteLine("  JsonMinifier --out custom-output.json input.json");
            Console.WriteLine("  JsonMinifier --no-names --precision 3 input.json");
            Console.WriteLine("  JsonMinifier --formatted --show-mappings input.json");
            Console.WriteLine("  JsonMinifier --show-mappings --precision 7 -i input.json --out output.json");
        }

        private static void ReplaceNamesRecursively(JsonNode? node)
        {
            if (node == null)
                return;

            switch (node)
            {
                case JsonObject obj:
                    // Handle objects
                    var propertiesToUpdate = new List<(string oldKey, string newKey, JsonNode value)>();

                    foreach (var property in obj)
                    {
                        // Replace names in "name" property values
                        if (property.Key == "name" && property.Value is JsonValue jsonValue && enableNameReplacement)
                        {
                            if (jsonValue.TryGetValue<string>(out string? nameValue) && nameValue != null)
                            {
                                string newName = GetOrCreateShortName(nameValue);
                                obj["name"] = newName;
                            }
                        }
                        // Replace names in "id" property values
                        else if (property.Key == "id" && property.Value is JsonValue idValue && enableIdReplacement)
                        {
                            if (idValue.TryGetValue<string>(out string? idValueString) && idValueString != null)
                            {
                                string newId = GetOrCreateShortId(idValueString);
                                obj["id"] = newId;
                            }
                        }
                        // Handle arrays that might contain name strings
                        else if (property.Value is JsonArray array)
                        {
                            for (int i = 0; i < array.Count; i++)
                            {
                                if (array[i] is JsonValue arrayValue && arrayValue.TryGetValue<string>(out string? arrayStringValue) && arrayStringValue != null)
                                {
                                    // Replace names/IDs in array string values (like InfantrySpawns)
                                    string newValue = arrayStringValue;
                                    if (enableNameReplacement || enableIdReplacement)
                                    {
                                        // These could be IDs or names, try ID replacement first, then name replacement
                                        if (enableIdReplacement)
                                            newValue = GetOrCreateShortId(newValue);
                                        else if (enableNameReplacement)
                                            newValue = ReplaceNamesInString(newValue);
                                    }
                                    array[i] = newValue;
                                }
                                else
                                {
                                    // Recursively process array elements
                                    ReplaceNamesRecursively(array[i]);
                                }
                            }
                        }
                        else
                        {
                            // Recursively process nested objects
                            ReplaceNamesRecursively(property.Value);
                        }
                    }
                    break;

                case JsonArray array:
                    // Handle arrays
                    for (int i = 0; i < array.Count; i++)
                    {
                        ReplaceNamesRecursively(array[i]);
                    }
                    break;
            }
        }

        private static string GetOrCreateShortName(string originalName)
        {
            // Don't replace names that are in the exclusion list
            if (excludedNames.Contains(originalName))
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

        private static string ReplaceNamesInString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            string result = input;

            // Replace known names in the string (for IDs and references that contain names)
            foreach (var kvp in nameMap)
            {
                result = result.Replace(kvp.Key, kvp.Value);
            }

            // Also handle cases where the entire string is a name that needs replacing
            if (nameMap.ContainsKey(input))
            {
                return nameMap[input];
            }

            return result;
        }

        private static string GetOrCreateShortId(string originalId)
        {
            if (string.IsNullOrEmpty(originalId))
                return originalId;

            // Don't replace IDs that are in the exclusion list
            if (excludedNames.Contains(originalId))
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
                    if (excludedNames.Contains(parts[i]))
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