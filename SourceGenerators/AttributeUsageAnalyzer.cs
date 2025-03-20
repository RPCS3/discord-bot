using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SourceGenerators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AttributeUsageAnalyzer : DiagnosticAnalyzer
{
    // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
    // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Localizing%20Analyzers.md for more on localization

    private static readonly DiagnosticDescriptor AccessCheckAttributeOnGroupCommandRule = new(
        "DSP0001",
        "Access check attributes are ignored",
        "Attribute {0} will be ignored for GroupCommand",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "GroupCommand methods will silently ignore any access check attributes, so instead create an instance of the required check attribute and call it explicitly inside the method."
    );
    private static readonly DiagnosticDescriptor DescriptionLengthRule = new(
        "DSP0002",
        "Description is too long",
        "Description is {0} characters long, which is {1} characters longer than allowed",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Description must be less than or equal to 100 characters."
    );
    private static readonly DiagnosticDescriptor CommandWithEmojiVariationSelector = new(
        "DSP0003",
        "Emoji with variation selector",
        "Command name has an emoji character with VS{0} ({1}), which may not work as a command name",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Commands should avoid using variation selectors for emoji characters in command names."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [
        AccessCheckAttributeOnGroupCommandRule,
        DescriptionLengthRule,
        CommandWithEmojiVariationSelector,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // TODO: Consider registering other actions that act on syntax instead of or in addition to symbols
        // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Analyzer%20Actions%20Semantics.md for more information
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterOperationAction(AnalyzeDescriptionAttribute, OperationKind.Attribute);
        context.RegisterOperationAction(AnalyzeCommandAttribute, OperationKind.Attribute);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var methodSymbol = (IMethodSymbol)context.Symbol;
        var methodAttributes = methodSymbol.GetAttributes();
        if (methodAttributes.IsEmpty)
            return;

        var hasGroupCommand = false;
        foreach (var attr in methodAttributes)
            if (IsDescendantOfAttribute(attr, "DSharpPlus.Commands.Attributes.GroupCommandAttribute"))
            {
                hasGroupCommand = true;
                break;
            }

        if (!hasGroupCommand)
            return;

        foreach (var attr in methodAttributes)
            if (IsDescendantOfAttribute(attr, "DSharpPlus.Commands.Attributes.CheckBaseAttribute"))
            {
                var attrLocation = attr.ApplicationSyntaxReference?.SyntaxTree.GetLocation(attr.ApplicationSyntaxReference.Span);
                var diagnostic = Diagnostic.Create(AccessCheckAttributeOnGroupCommandRule, attrLocation ?? methodSymbol.Locations[0], attr.AttributeClass?.Name ?? methodSymbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
    }

    private void AnalyzeDescriptionAttribute(OperationAnalysisContext context)
    {
        // The Roslyn architecture is based on inheritance.
        // To get the required metadata, we should match the 'Operation' and 'Syntax' objects to the particular types,
        // which are based on the 'OperationKind' parameter specified in the 'Register...' method.
        if (context.Operation is not IAttributeOperation attributeOperation
            || context.Operation.Syntax is not AttributeSyntax attributeSyntax)
            return;

        if (attributeOperation.Kind != OperationKind.Attribute
            || attributeOperation.Operation is not IObjectCreationOperation
            {
                Kind: OperationKind.ObjectCreation,
                Type:
                {
                    ContainingNamespace:
                    {
                        ContainingNamespace.Name: "System",
                        Name: "ComponentModel"
                    },
                    Name: "DescriptionAttribute"
                }
            } attrCtorOp)
            return;

        if (attrCtorOp.Arguments.Length is 0
            || attrCtorOp.Arguments.FirstOrDefault(arg => arg is
            {
                Parameter:
                {
                    Name: "description",
                    Type: {ContainingNamespace.Name: "System", Name: "String"}
                }
            }) is not
            {
                Value: ILiteralOperation
                {
                    ConstantValue:
                    {
                        HasValue: true,
                        Value: string actualDescription
                    }
                }
            })
            return;

        const int maxDescriptionLength = 100;
        if (actualDescription.Length <= maxDescriptionLength)
            return;

        var diagnostic = Diagnostic.Create(DescriptionLengthRule,
            // The highlighted area in the analyzed source code. Keep it as specific as possible.
            attributeSyntax.GetLocation(),
            // The value is passed to the 'MessageFormat' argument of your rule.
            actualDescription.Length, actualDescription.Length - maxDescriptionLength
        );

        // Reporting a diagnostic is the primary outcome of analyzers.
        context.ReportDiagnostic(diagnostic);
    }
    
    private void AnalyzeCommandAttribute(OperationAnalysisContext context)
    {
        // The Roslyn architecture is based on inheritance.
        // To get the required metadata, we should match the 'Operation' and 'Syntax' objects to the particular types,
        // which are based on the 'OperationKind' parameter specified in the 'Register...' method.
        if (context.Operation is not IAttributeOperation attributeOperation
            || context.Operation.Syntax is not AttributeSyntax attributeSyntax)
            return;

        if (attributeOperation.Kind != OperationKind.Attribute
            || attributeOperation.Operation is not IObjectCreationOperation
            {
                Kind: OperationKind.ObjectCreation,
                Type:
                {
                    ContainingNamespace:
                    {
                        ContainingNamespace.Name: "DSharpPlus",
                        Name: "Commands"
                    },
                    Name: "CommandAttribute"
                }
            } attrCtorOp)
            return;

        if (attrCtorOp.Arguments.Length is 0
            || attrCtorOp.Arguments.FirstOrDefault(arg => arg is
            {
                Parameter:
                {
                    Name: "name",
                    Type: {ContainingNamespace.Name: "System", Name: "String"}
                }
            }) is not
            {
                Value: ILiteralOperation
                {
                    ConstantValue:
                    {
                        HasValue: true,
                        Value: string actualName
                    }
                }
            })
            return;

        if (actualName is not {Length: >0})
            return;

        var vs = actualName.ToCharArray().FirstOrDefault(VariationSelectors.Contains);
        if (vs is default(char))
            return;
        
        var diagnostic = Diagnostic.Create(CommandWithEmojiVariationSelector,
            // The highlighted area in the analyzed source code. Keep it as specific as possible.
            attributeSyntax.GetLocation(),
            // The value is passed to the 'MessageFormat' argument of your rule.
            vs - 0xFE00 + 1, $"0x{(int)vs:X4}"
        );

        // Reporting a diagnostic is the primary outcome of analyzers.
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsDescendantOfAttribute(AttributeData attributeData, string baseAttributeClassNameWithNamespace)
    {
        var attrClass = attributeData.AttributeClass;
        while (attrClass is not null)
        {
            if (attrClass.ToDisplayString() == baseAttributeClassNameWithNamespace)
                return true;

            attrClass = attrClass.BaseType;
        } 
        return false;
    }

    private static readonly HashSet<char> VariationSelectors = [.. Enumerable.Range(0xFE00, 16).Select(i => (char)i)];
}