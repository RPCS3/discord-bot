using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace SourceGenerators;

[Generator(LanguageNames.CSharp)]
public class ConfusablesSourceGenerator : IIncrementalGenerator
{
    private static readonly char[] CommentSplitter = ['#'];
    private static readonly char[] FieldSplitter = [';'];
    private static readonly char[] PairSplitter = [' '];

    private static readonly DiagnosticDescriptor ConfusablesCheckWarning = new(
        id: "CONFUSABLES001",
        title: "Failed to check confusables version",
        messageFormat: "Error while checking confusables version: '{0}'",
        category: nameof(ConfusablesSourceGenerator),
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    private static readonly DiagnosticDescriptor ConfusablesVersionWarning = new(
        id: "CONFUSABLES002",
        title: "Outdated confusables version",
        messageFormat: "Local confusables version: {0} ({1}), remote confusables version: {2} ({3})",
        category: nameof(ConfusablesSourceGenerator),
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
        
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var resourceProvider = context.AdditionalTextsProvider.Where(static f => Path.GetFileName(f.Path).Equals("confusables.txt"));
        var dataProvider = resourceProvider.Combine(context.AnalyzerConfigOptionsProvider.Combine(context.CompilationProvider));
        context.RegisterSourceOutput(dataProvider, Execute);
    }

    private static void Execute(SourceProductionContext context, (AdditionalText resource, (AnalyzerConfigOptionsProvider configOptions, Compilation compilation) generatorContext) args)
    {
        using var httpClient = new HttpClient();
        using var msg = new HttpRequestMessage(HttpMethod.Get, "https://www.unicode.org/Public/security/latest/confusables.txt");
        msg.Headers.Range = new(0, 512);
        var requestTask = httpClient.SendAsync(msg);

        var resource = args.resource;
        using var stream = File.Open(resource.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream is null)
            throw new InvalidOperationException("Failed to get confusables.txt stream");

        var mapping = new Dictionary<uint, uint[]>();
        var date = "";
        var version = "";
        using var reader = new StreamReader(stream, Encoding.UTF8, false);
        while (reader.ReadLine() is string line)
        {
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
            {
                if (line is {Length: > 10})
                {
                    if (line.StartsWith("# Date: "))
                        date = line.Substring(8).Trim();
                    else if (line.StartsWith("# Version: "))
                        version = line.Substring(11).Trim();
                }
                continue;
            }

            var lineParts = line.Split(CommentSplitter, 2);
            var mappingParts = lineParts[0].Split(FieldSplitter, 3);
            if (mappingParts.Length < 2)
                throw new InvalidOperationException("Invalid confusable mapping line: " + line);

            try
            {
                var confusableChar = uint.Parse(mappingParts[0].Trim(), NumberStyles.HexNumber);
                var skeletonChars = mappingParts[1].Split(PairSplitter, StringSplitOptions.RemoveEmptyEntries).Select(l => uint.Parse(l, NumberStyles.HexNumber)).ToArray();
                mapping.Add(confusableChar, skeletonChars);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("Invalid confusable mapping line:" + line, e);
            }
        }
        if (mapping.Count == 0)
            throw new InvalidOperationException("Empty confusable mapping source");

        if (!args.generatorContext.configOptions.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns))
            ns = args.generatorContext.compilation.AssemblyName;
        var cn = Path.GetFileNameWithoutExtension(resource.Path);
        if (cn.Length == 1)
            cn = cn.ToUpper();
        else
            cn = char.ToUpper(cn[0]) + cn.Substring(1);
        if (!Version.TryParse(version, out _))
            version = "";
            
        var result = new StringBuilder()
            .AppendLine("using System;")
            .AppendLine("using System.Collections.Generic;")
            .AppendLine()
            .AppendLine($"namespace {ns}")
            .AppendLine("{")
            .AppendLine($"    internal static class {cn}")
            .AppendLine("    {")
            .AppendLine($"        public const string Version = \"{version}\";")
            .AppendLine()
            .AppendLine($"        public const string Date = \"{date}\";")
            .AppendLine()
            .AppendLine("        public static readonly Dictionary<uint, uint[]> Mapping = new()")
            .AppendLine("        {");
        foreach (var kvp in mapping.OrderBy(i => i.Key))
            result.AppendLine($@"            [0x{kvp.Key:X5}u] = new[] {{ {string.Join(", ", kvp.Value!.OrderBy(i => i).Select(n => $"0x{n:X5}u"))} }},");
        result.AppendLine("        };")
            .AppendLine("    }")
            .AppendLine("}");

        context.AddSource($"{cn}.Generated.cs", SourceText.From(result.ToString(), Encoding.UTF8));

        try
        {
            var requestResult = requestTask.ConfigureAwait(false).GetAwaiter().GetResult();
            var response = requestResult.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult().Split('\n');
            var remoteVer = "";
            var remoteDate = "";
            foreach (var l in response)
            {
                if (l.StartsWith("# Date: "))
                    remoteDate = l.Substring(8).Trim();
                else if (l.StartsWith("# Version: "))
                    remoteVer = l.Substring(11).Trim();
            }
            if (!string.IsNullOrEmpty(remoteDate) && remoteDate != date
                || !string.IsNullOrEmpty(remoteVer) && remoteVer != version)
            {
                context.ReportDiagnostic(Diagnostic.Create(ConfusablesVersionWarning, Location.None, version, date, remoteVer, remoteDate));
            }
        }
        catch (Exception e)
        {
            context.ReportDiagnostic(Diagnostic.Create(ConfusablesCheckWarning, Location.None, e.Message));
        }
    }
}