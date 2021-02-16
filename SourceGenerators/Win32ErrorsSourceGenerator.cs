using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SourceGenerators
{
    [Generator]
    public class Win32ErrorsSourceGenerator : ISourceGenerator
    {
        private const string Indent = "    ";
        private static readonly char[] Separator = {'\t'};
        
        private static readonly DiagnosticDescriptor Win32ErrorFormatError = new(
            id: "WIN32CODE001",
            title: "Invalid Win32 error code line",
            messageFormat: "Error while parsing win32 error code description. Code: '{0}', description: '{1}'.",
            category: nameof(Win32ErrorsSourceGenerator),
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var resourceName = context.AdditionalFiles.FirstOrDefault(f => Path.GetFileName(f.Path).Equals("win32_error_codes.txt"));
            if (resourceName is null)
                return;
            
            using var stream = File.Open(resourceName.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream is null)
                throw new InvalidOperationException("Failed to get win32_error_codes.txt stream");

            if (!context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.RootNamespace", out var ns))
                ns = context.Compilation.AssemblyName;
            var cn = "Win32ErrorCodes";
            var result = new StringBuilder()
                .AppendLine("using System.Collections.Generic;")
                .AppendLine()
                .AppendLine($"namespace {ns}")
                .AppendLine("{")
                .AppendLine($"{Indent}public static class {cn}")
                .AppendLine($"{Indent}{{")
                .AppendLine($"{Indent}{Indent}public static readonly Dictionary<int, (string name, string description)> Map = new()")
                .AppendLine($"{Indent}{Indent}{{");

            var previousPos = 0;
            var line = 0;
            var codeLine = 0;
            var descLine = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, false);
            while (reader.ReadLine() is string errorCodeLine)
            {
                line++;
                if (string.IsNullOrWhiteSpace(errorCodeLine))
                    continue;

                codeLine = line - 1;
                string errorNameAndDescriptionLine;
                do
                {
                    errorNameAndDescriptionLine = reader.ReadLine();
                    line++;
                } while (string.IsNullOrWhiteSpace(errorNameAndDescriptionLine));
                descLine = line - 1;
                
                var nameDescParts = errorNameAndDescriptionLine.Split(Separator, 2);
                if (nameDescParts.Length != 2 || !Regex.IsMatch(errorCodeLine, @"0x[0-9a-f]+"))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Win32ErrorFormatError,
                        Location.Create(
                            resourceName.Path,
                            TextSpan.FromBounds(
                                previousPos,
                                (int)stream.Position),
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
                var desc = nameDescParts[1].Replace("\\", "\\\\").Replace("\"", "\\\"");
                result.AppendLine($"{Indent}{Indent}{Indent}[{errorCodeLine.Trim()}] = (\"{name.Trim()}\", \"{desc.Trim()}\"),");
            }

            result.AppendLine($"{Indent}{Indent}}};")
                .AppendLine($"{Indent}}}")
                .AppendLine("}");
            
            context.AddSource($"{cn}.Generated.cs", SourceText.From(result.ToString(), Encoding.UTF8));
        }
    }
}