using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SourceGenerators
{
    [Generator]
    public class NamesSourceGenerator : ISourceGenerator
    {
        private const string Indent = "    ";
        private const string NameSuffix = " (Rule 7)";
        //private const int DiscordUsernameLengthLimit = 32-10; //" #12345678"
        private const int DiscordUsernameLengthLimit = 32;

        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var resources = context.AdditionalFiles
                .Where(f => Path.GetFileName(f.Path).ToLower().StartsWith("names_") && f.Path.ToLower().EndsWith(".txt"))
                .OrderBy(f => f.Path)
                .ToList();
            if (resources.Count == 0)
                return;

            var names = new HashSet<string>();
            foreach (var resource in resources)
            {
                using var stream = File.Open(resource.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is string line)
                {
                    if (line.Length < 2 || line.StartsWith("#"))
                        continue;

                    var commentPos = line.IndexOf(" (");
                    if (commentPos > 1)
                        line = line.Substring(0, commentPos);
                    line = line.Trim()
                        .Replace("  ", " ")
                        .Replace('`', '\'') // consider ’
                        .Replace("\"", "\\\"");
                    //if (line.Length + NameSuffix.Length > DiscordUsernameLengthLimit)
                    //    line = line.Split(' ')[0];
                    if (line.Length + NameSuffix.Length > DiscordUsernameLengthLimit)
                        continue;

                    if (line.Contains('@')
                        || line.Contains('#')
                        || line.Contains(':'))
                        continue;

                    names.Add(line);
                    //if (line.Contains(' '))
                    //    names.Add(line.Split(' ')[0]);
                }
            }
            
            if (!context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns))
                ns = context.Compilation.AssemblyName;
            var cn = "NamesPool";
            var result = new StringBuilder()
                .AppendLine("using System.Collections.Generic;")
                .AppendLine()
                .AppendLine($"namespace {ns}")
                .AppendLine("{")
                .AppendLine($"{Indent}public static class {cn}")
                .AppendLine($"{Indent}{{")
                .AppendLine($"{Indent}{Indent}public const string NameSuffix = \"{NameSuffix}\";")
                .AppendLine()
                .AppendLine($"{Indent}{Indent}public const int NameCount = {names.Count};")
                .AppendLine()
                .AppendLine($"{Indent}{Indent}public static readonly List<string> List = new()")
                .AppendLine($"{Indent}{Indent}{{");
            foreach (var name in names.OrderBy(n => n)) 
                result.AppendLine($"{Indent}{Indent}{Indent}\"{name}\",");
            result.AppendLine($"{Indent}{Indent}}};")
                .AppendLine($"{Indent}}}")
                .AppendLine("}");
            
            context.AddSource($"{cn}.Generated.cs", SourceText.From(result.ToString(), Encoding.UTF8));

        }
    }
}