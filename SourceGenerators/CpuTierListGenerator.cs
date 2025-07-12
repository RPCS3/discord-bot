using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace SourceGenerators;

[Generator(LanguageNames.CSharp)]
public class CpuTierListGenerator: IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var resourceProvider = context.AdditionalTextsProvider.Where(
            static f => Path.GetFileName(f.Path) is {} fname
                        && fname.StartsWith("cpu_tier_list")
                        && fname.EndsWith(".conf")
        );
        var dataProvider = resourceProvider.Combine(context.AnalyzerConfigOptionsProvider.Combine(context.CompilationProvider));
        context.RegisterSourceOutput(dataProvider, Execute);
    }

    private static void Execute(SourceProductionContext context, (AdditionalText resource, (AnalyzerConfigOptionsProvider configOptions, Compilation compilation) generatorContext) args)
    {
        var resource = args.resource;
        using var stream = File.Open(resource.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream is null)
            throw new InvalidOperationException("Failed to open {resource.Path}");

        if (!args.generatorContext.configOptions.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns))
            ns = args.generatorContext.compilation.AssemblyName;
        const string cn = "CpuTierList";
        var result = new StringBuilder().AppendLine($$"""
            using System;
            using System.Text.RegularExpressions;

            namespace {{ns}};
            
            internal static class {{cn}}
            {
                private const RegexOptions DefaultOptions = RegexOptions.Singleline | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Compiled;
            """
        );
        using var reader = new StreamReader(stream, Encoding.UTF8, false);
        var currentTier = "unknown";
        var idx = 0;
        List<(string model, string tier)> tierMap = [];
        while (reader.ReadLine() is string line)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#"))
                continue;

            if (line.StartsWith("["))
            {
                currentTier = line.Substring(1, line.Length - 2);
                result.AppendLine($"""
                    
                        // {currentTier} Tier
                    """
                );
                continue;
            }

            tierMap.Add((line, currentTier));
            line = line.Replace(" ", ".*");
            // todo: use generated regex when it's possible https://github.com/dotnet/roslyn/discussions/48358
            /*
            result.AppendLine($"""
                    [GeneratedRegex(@"{line}", DefaultOptions)]
                    private static partial Regex Model{i++}();
                """
            );
            */
            result.AppendLine($"""
                    private static readonly Regex Model{idx++} = new(@"{line}", DefaultOptions);
                """
            );
        }
        result.AppendLine($"""

                public static readonly List<(string model, string tier, Regex regex)> List = [
            """
        );
        for (var i=0; i<idx; i++)
            result.AppendLine($"""
                        (@"{tierMap[i].model}", "{tierMap[i].tier}", Model{i}),
                """
            );
        result.AppendLine("""
                ];
            }
            """
        );
        context.AddSource($"{cn}.Patterns.Generated.cs", SourceText.From(result.ToString(), Encoding.UTF8));
    }
}