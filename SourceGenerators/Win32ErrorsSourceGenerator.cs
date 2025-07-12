using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace SourceGenerators;

[Generator(LanguageNames.CSharp)]
public class Win32ErrorsSourceGenerator: IIncrementalGenerator
{
    private static readonly char[] Separator = ['\t'];
        
    private static readonly DiagnosticDescriptor Win32ErrorFormatError = new(
        id: "WIN32CODE001",
        title: "Invalid Win32 error code line",
        messageFormat: "Error while parsing win32 error code description. Code: '{0}', description: '{1}'.",
        category: nameof(Win32ErrorsSourceGenerator),
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var resourceProvider = context.AdditionalTextsProvider.Where(
            static f => Path.GetFileName(f.Path) is string fname
                        && fname.StartsWith("win32_error_codes", StringComparison.OrdinalIgnoreCase)
                        && fname.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
        );
        var dataProvider = resourceProvider.Combine(context.AnalyzerConfigOptionsProvider.Combine(context.CompilationProvider));
        context.RegisterSourceOutput(dataProvider, Execute);
    }

    private static void Execute(SourceProductionContext context, (AdditionalText resource, (AnalyzerConfigOptionsProvider configOptions, Compilation compilation) generatorContext) args)
    {
        var resource = args.resource;
        using var stream = File.Open(resource.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream is null)
            throw new InvalidOperationException("Failed to get win32_error_codes txt stream");

        if (!args.generatorContext.configOptions.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns))
            ns = args.generatorContext.compilation.AssemblyName;
        const string cn = "Win32ErrorCodes";
        var result = new StringBuilder().AppendLine($$"""
            using System.Collections.Generic;

            namespace {{ns}};

            public static class {{cn}}
            {
                public static readonly Dictionary<int, (string name, string description)> Map = new()
                {
            """
        );

        var previousPos = 0;
        var line = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, false);
        while (reader.ReadLine() is string errorCodeLine)
        {
            line++;
            if (string.IsNullOrWhiteSpace(errorCodeLine))
                continue;

            var codeLine = line - 1;
            string? errorNameAndDescriptionLine;
            do
            {
                errorNameAndDescriptionLine = reader.ReadLine();
                line++;
            } while (string.IsNullOrWhiteSpace(errorNameAndDescriptionLine));
            var descLine = line - 1;
                
            var nameDescParts = errorNameAndDescriptionLine.Split(Separator, 2);
            if (nameDescParts.Length != 2 || !Regex.IsMatch(errorCodeLine, @"0x[0-9a-f]+"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Win32ErrorFormatError,
                    Location.Create(
                        resource.Path,
                        TextSpan.FromBounds(
                            previousPos,
                            (int)stream.Position
                        ),
                        new(
                            new(codeLine, 0),
                            new(descLine, errorNameAndDescriptionLine.Length)
                        )),
                    errorCodeLine,
                    errorNameAndDescriptionLine));
                previousPos = (int)stream.Position;
                continue;
            }
            previousPos = (int)stream.Position;

            var name = nameDescParts[0];
            var desc = nameDescParts[1].Replace(@"\", @"\\").Replace("\"", "\\\"");
            result.AppendLine($"""
                            [{errorCodeLine.Trim()}] = ("{name.Trim()}", "{desc.Trim()}"),
                """
            );
        }
        result.AppendLine("""
                };
            }
            """
        );
        context.AddSource($"{cn}.Generated.cs", SourceText.From(result.ToString(), Encoding.UTF8));
    }
}