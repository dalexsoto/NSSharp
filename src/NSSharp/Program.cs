using System.CommandLine;
using NSSharp;
using NSSharp.Binding;
using NSSharp.Json;
using NSSharp.Lexer;
using NSSharp.Parser;
using NSSharp.Ast;

var filesArg = new Argument<FileInfo[]>("files")
{
    Arity = ArgumentArity.ZeroOrMore,
    Description = "One or more Objective-C header files to parse.",
};

var xcframeworkOpt = new Option<DirectoryInfo?>("--xcframework")
{
    Description = "Path to an .xcframework bundle to discover and parse all headers.",
};

var sliceOpt = new Option<string?>("--slice")
{
    Description = "Select a specific xcframework slice (e.g. ios-arm64). Use --list-slices to see available slices.",
};

var listSlicesOpt = new Option<bool>("--list-slices")
{
    Description = "List available slices in the xcframework and exit.",
};

var outputOpt = new Option<FileInfo?>("--output", "-o")
{
    Description = "Write output to a file instead of stdout (or a directory when --split-by-header is used).",
};

var compactOpt = new Option<bool>("--compact")
{
    Description = "Output compact JSON instead of pretty-printed.",
};

var formatOpt = new Option<string>("--format", "-f")
{
    Description = "Output format: csharp (default) or json.",
};
formatOpt.DefaultValueFactory = _ => "csharp";

var externMacrosOpt = new Option<string[]>("--extern-macros")
{
    Description = "Macro names to treat as extern (e.g. --extern-macros PSPDF_EXPORT MY_EXPORT).",
};
externMacrosOpt.DefaultValueFactory = _ => Array.Empty<string>();

var noMacroHeuristicOpt = new Option<bool>("--no-macro-heuristic")
{
    Description = "Disable automatic UPPER_SNAKE_CASE macro detection. By default, identifiers like NS_SWIFT_NAME or VENDOR_DEPRECATED are auto-skipped.",
};

var emitCBindingsOpt = new Option<bool>("--emit-c-bindings")
{
    Description = "Include C function declarations ([DllImport]) in C# binding output. Extern constants ([Field]) are always included.",
};

var splitByHeaderOpt = new Option<bool>("--split-by-header")
{
    Description = "For C# format, write one .cs file per input header (requires --output directory).",
};

var namespaceOpt = new Option<string?>("--namespace")
{
    Description = "Namespace for generated C# output. Defaults to xcframework name when --xcframework is used.",
};

var rootCommand = new RootCommand("NSSharp — Objective-C header parser and C# binding generator");
rootCommand.Arguments.Add(filesArg);
rootCommand.Options.Add(xcframeworkOpt);
rootCommand.Options.Add(sliceOpt);
rootCommand.Options.Add(listSlicesOpt);
rootCommand.Options.Add(outputOpt);
rootCommand.Options.Add(compactOpt);
rootCommand.Options.Add(formatOpt);
rootCommand.Options.Add(externMacrosOpt);
rootCommand.Options.Add(noMacroHeuristicOpt);
rootCommand.Options.Add(emitCBindingsOpt);
rootCommand.Options.Add(splitByHeaderOpt);
rootCommand.Options.Add(namespaceOpt);

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var files = parseResult.GetValue(filesArg);
    var xcframework = parseResult.GetValue(xcframeworkOpt);
    var slice = parseResult.GetValue(sliceOpt);
    var listSlices = parseResult.GetValue(listSlicesOpt);
    var output = parseResult.GetValue(outputOpt);
    var compact = parseResult.GetValue(compactOpt);
    var format = parseResult.GetValue(formatOpt) ?? "json";
    var externMacros = parseResult.GetValue(externMacrosOpt) ?? [];
    var noMacroHeuristic = parseResult.GetValue(noMacroHeuristicOpt);
    var emitCBindings = parseResult.GetValue(emitCBindingsOpt);
    var splitByHeader = parseResult.GetValue(splitByHeaderOpt);
    var csharpNamespace = parseResult.GetValue(namespaceOpt);

    var lexerOptions = new ObjCLexerOptions
    {
        MacroHeuristic = !noMacroHeuristic,
        ExternMacros = externMacros,
    };

    // Handle --list-slices
    if (listSlices)
    {
        if (xcframework == null)
        {
            Console.Error.WriteLine("--list-slices requires --xcframework to be specified.");
            Environment.ExitCode = 1;
            return;
        }
        try
        {
            var available = XCFrameworkResolver.ListSlices(xcframework.FullName);
            foreach (var s in available)
                Console.WriteLine(s);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Environment.ExitCode = 1;
        }
        return;
    }

    var headerPaths = new List<string>();

    if (files is { Length: > 0 })
    {
        foreach (var f in files)
        {
            if (!f.Exists)
            {
                Console.Error.WriteLine($"File not found: {f.FullName}");
                Environment.ExitCode = 1;
                return;
            }
            headerPaths.Add(f.FullName);
        }
    }

    if (xcframework != null)
    {
        try
        {
            var resolved = XCFrameworkResolver.ResolveHeaders(xcframework.FullName, slice);
            if (resolved.Count == 0)
            {
                Console.Error.WriteLine($"No headers found in {xcframework.FullName}");
                Environment.ExitCode = 1;
                return;
            }
            headerPaths.AddRange(resolved);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error resolving xcframework: {ex.Message}");
            Environment.ExitCode = 1;
            return;
        }
    }

    if (headerPaths.Count == 0)
    {
        Console.Error.WriteLine("No input files specified. Provide header files or --xcframework.");
        Environment.ExitCode = 1;
        return;
    }

    var headers = new List<ObjCHeader>();
    var parsedSourcePaths = new List<string>();
    foreach (var path in headerPaths)
    {
        try
        {
            var source = await File.ReadAllTextAsync(path, cancellationToken);
            var lexer = new ObjCLexer(source, lexerOptions);
            var tokens = lexer.Tokenize();
            var parser = new ObjCParser(tokens);
            var header = parser.Parse(Path.GetFileName(path));
            headers.Add(header);
            parsedSourcePaths.Add(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error parsing {path}: {ex.Message}");
        }
    }

    string result;
    if (format.Equals("csharp", StringComparison.OrdinalIgnoreCase) ||
        format.Equals("cs", StringComparison.OrdinalIgnoreCase))
    {
        // Merge ObjC categories into parent classes across all headers
        CSharpBindingGenerator.MergeCategories(headers);

        // Build typedef resolution map from all headers
        CSharpBindingGenerator.BuildTypedefMap(headers);

        var generator = new CSharpBindingGenerator();
        var effectiveNamespace = ResolveNamespace(csharpNamespace, xcframework);

        if (splitByHeader)
        {
            if (output == null)
            {
                Console.Error.WriteLine("--split-by-header requires --output to be a directory path.");
                Environment.ExitCode = 1;
                return;
            }

            var outputDir = output.FullName;
            if (File.Exists(outputDir))
            {
                Console.Error.WriteLine($"--split-by-header requires --output to be a directory, but found file: {outputDir}");
                Environment.ExitCode = 1;
                return;
            }
            if (Path.HasExtension(outputDir) && !Directory.Exists(outputDir))
            {
                Console.Error.WriteLine($"--split-by-header requires --output to be a directory path: {outputDir}");
                Environment.ExitCode = 1;
                return;
            }

            Directory.CreateDirectory(outputDir);

            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
            {
                var header = headers[i];
                var sourcePath = parsedSourcePaths[i];
                var baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
                if (string.IsNullOrWhiteSpace(baseName))
                    baseName = $"Header{i + 1}";

                if (nameCounts.TryGetValue(baseName, out var count))
                {
                    count++;
                    nameCounts[baseName] = count;
                    baseName = $"{baseName}_{count}";
                }
                else
                {
                    nameCounts[baseName] = 1;
                }

                var outPath = Path.Combine(outputDir, baseName + ".cs");
                var content = generator.Generate(header, emitCBindings);
                content = WrapInNamespace(content, effectiveNamespace);
                await File.WriteAllTextAsync(outPath, content, cancellationToken);
                Console.Error.WriteLine($"Written to {outPath}");
            }

            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var header in headers)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"// ========== {header.File} ==========");
            var content = generator.Generate(header, emitCBindings);
            sb.AppendLine(WrapInNamespace(content, effectiveNamespace));
        }
        result = sb.ToString().TrimEnd();
    }
    else
    {
        if (splitByHeader)
        {
            Console.Error.WriteLine("--split-by-header is only supported with --format csharp.");
            Environment.ExitCode = 1;
            return;
        }
        if (!string.IsNullOrWhiteSpace(csharpNamespace))
        {
            Console.Error.WriteLine("--namespace is only supported with --format csharp.");
            Environment.ExitCode = 1;
            return;
        }

        bool pretty = !compact;
        result = headers.Count == 1
            ? ObjCJsonSerializer.Serialize(headers[0], pretty)
            : ObjCJsonSerializer.Serialize(headers, pretty);
    }

    if (output != null)
    {
        await File.WriteAllTextAsync(output.FullName, result, cancellationToken);
        Console.Error.WriteLine($"Written to {output.FullName}");
    }
    else
    {
        Console.WriteLine(result);
    }
});

var config = new CommandLineConfiguration(rootCommand);
return await config.InvokeAsync(args);

static string SanitizeFileName(string name)
{
    if (string.IsNullOrEmpty(name))
        return string.Empty;

    var invalid = Path.GetInvalidFileNameChars();
    var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
    return new string(chars);
}

static string? ResolveNamespace(string? explicitNamespace, DirectoryInfo? xcframework)
{
    if (!string.IsNullOrWhiteSpace(explicitNamespace))
        return SanitizeNamespace(explicitNamespace);

    if (xcframework != null)
        return SanitizeNamespace(Path.GetFileNameWithoutExtension(xcframework.Name));

    return null;
}

static string WrapInNamespace(string content, string? namespaceName)
{
    if (string.IsNullOrWhiteSpace(namespaceName))
        return content.TrimEnd();

    var ns = SanitizeNamespace(namespaceName);
    if (string.IsNullOrWhiteSpace(ns))
        return content.TrimEnd();

    var normalized = content.Replace("\r\n", "\n");
    var lines = normalized.Split('\n');
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"namespace {ns}");
    sb.AppendLine("{");
    foreach (var line in lines)
    {
        if (line.Length == 0)
            sb.AppendLine();
        else
            sb.Append("    ").AppendLine(line);
    }
    sb.AppendLine("}");
    return sb.ToString().TrimEnd();
}

static string SanitizeNamespace(string value)
{
    var segments = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var sanitized = new List<string>();
    foreach (var seg in segments)
    {
        if (string.IsNullOrWhiteSpace(seg))
            continue;

        var chars = seg.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var segment = new string(chars);
        if (string.IsNullOrWhiteSpace(segment))
            continue;
        if (!char.IsLetter(segment[0]) && segment[0] != '_')
            segment = "_" + segment;
        sanitized.Add(segment);
    }

    return string.Join(".", sanitized);
}
