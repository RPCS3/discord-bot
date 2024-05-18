using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGenerators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AttributeUsageAnalyzer : DiagnosticAnalyzer
{
    // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
    // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Localizing%20Analyzers.md for more on localization
    private const string Category = "Usage";
    private const string DiagnosticId = "DSharpPlusAttributeUsage";

    private static readonly DiagnosticDescriptor AccessCheckAttributeOnGroupCommandRule = new DiagnosticDescriptor(
        DiagnosticId,
        "Access check attributes are ignored",
        "Attribute {0} will be ignored for GroupCommand",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "GroupCommand methods will silently ignore any access check attributes, so instead create an instance of the required check attribute and call it explicitly inside the method."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [AccessCheckAttributeOnGroupCommandRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // TODO: Consider registering other actions that act on syntax instead of or in addition to symbols
        // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Analyzer%20Actions%20Semantics.md for more information
        context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.Method);
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        var methodSymbol = (IMethodSymbol)context.Symbol;
        var methodAttributes = methodSymbol.GetAttributes();
        if (methodAttributes.IsEmpty)
            return;

        var hasGroupCommand = false;
        foreach (var attr in methodAttributes)
            if (IsDescendantOfAttribute(attr, "DSharpPlus.CommandsNext.Attributes.GroupCommandAttribute"))
            {
                hasGroupCommand = true;
                break;
            }

        if (!hasGroupCommand)
            return;

        foreach (var attr in methodAttributes)
            if (IsDescendantOfAttribute(attr, "DSharpPlus.CommandsNext.Attributes.CheckBaseAttribute"))
            {
                var attrLocation = attr.ApplicationSyntaxReference?.SyntaxTree.GetLocation(attr.ApplicationSyntaxReference.Span);
                var diagnostic = Diagnostic.Create(AccessCheckAttributeOnGroupCommandRule, attrLocation ?? methodSymbol.Locations[0], attr.AttributeClass?.Name ?? methodSymbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
    }

    private static bool IsDescendantOfAttribute(AttributeData attributeData, string baseAttributeClassNameWithNamespace)
    {
        var attrClass = attributeData.AttributeClass;
        do
        {
            if (attrClass.ToDisplayString() == baseAttributeClassNameWithNamespace)
                return true;

            attrClass = attrClass.BaseType;
        } while (attrClass != null);
        return false;
    }
}