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
    Description = "Write output to a file instead of stdout.",
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

var rootCommand = new RootCommand("NSSharp — Objective-C header parser and C# binding generator");
rootCommand.Arguments.Add(filesArg);
rootCommand.Options.Add(xcframeworkOpt);
rootCommand.Options.Add(sliceOpt);
rootCommand.Options.Add(listSlicesOpt);
rootCommand.Options.Add(outputOpt);
rootCommand.Options.Add(compactOpt);
rootCommand.Options.Add(formatOpt);

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var files = parseResult.GetValue(filesArg);
    var xcframework = parseResult.GetValue(xcframeworkOpt);
    var slice = parseResult.GetValue(sliceOpt);
    var listSlices = parseResult.GetValue(listSlicesOpt);
    var output = parseResult.GetValue(outputOpt);
    var compact = parseResult.GetValue(compactOpt);
    var format = parseResult.GetValue(formatOpt) ?? "json";

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
    foreach (var path in headerPaths)
    {
        try
        {
            var source = await File.ReadAllTextAsync(path, cancellationToken);
            var lexer = new ObjCLexer(source);
            var tokens = lexer.Tokenize();
            var parser = new ObjCParser(tokens);
            var header = parser.Parse(Path.GetFileName(path));
            headers.Add(header);
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
        var generator = new CSharpBindingGenerator();
        var sb = new System.Text.StringBuilder();
        foreach (var header in headers)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"// ========== {header.File} ==========");
            sb.AppendLine(generator.Generate(header));
        }
        result = sb.ToString().TrimEnd();
    }
    else
    {
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
