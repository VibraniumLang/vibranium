using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Vibranium.Compiler.Core;

namespace Vibranium.Frontend.Roslyn;

internal sealed partial class CfgMirLowerer
{
    private const string FieldInitializerCutSuffix = " is not supported in a field or property initializer in Vibranium v0.2; assign the field in the constructor.";
    private const string FieldInitializerAndMessage = "'&&'" + FieldInitializerCutSuffix;
    private const string FieldInitializerOrMessage = "'||'" + FieldInitializerCutSuffix;
    private const string FieldInitializerConditionalMessage = "The '?:' operator" + FieldInitializerCutSuffix;
    private const string FieldInitializerSwitchMessage = "A switch expression" + FieldInitializerCutSuffix;
    private const string FieldInitializerIsMessage = "An 'is' expression" + FieldInitializerCutSuffix;

    private bool _loweringRawFieldInitializer;

    internal static bool IsFieldInitializerCutMessage(string message) => message is
        FieldInitializerAndMessage or FieldInitializerOrMessage or FieldInitializerConditionalMessage or
        FieldInitializerSwitchMessage or FieldInitializerIsMessage;

    internal static void CheckFieldInitializerControlFlow(SyntaxNode root, List<VibraniumDiagnostic> diagnostics)
    {
        // Check source, not the lowered constructors: constant folding, open generics and multiple
        // constructors cannot hide an initializer or change which token occurs first in the file.
        SyntaxToken first = default;
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            if (field.Parent is not (ClassDeclarationSyntax or StructDeclarationSyntax) ||
                field.Modifiers.Any(SyntaxKind.StaticKeyword) || field.Modifiers.Any(SyntaxKind.ConstKeyword)) continue;
            foreach (var variable in field.Declaration.Variables)
            {
                if (variable.Initializer is not { Value: var value }) continue;
                var token = FirstFieldInitializerControlFlowToken(value);
                if (token.RawKind != 0 && (first.RawKind == 0 || token.SpanStart < first.SpanStart)) first = token;
            }
        }
        var message = first.Kind() switch
        {
            SyntaxKind.AmpersandAmpersandToken => FieldInitializerAndMessage,
            SyntaxKind.BarBarToken => FieldInitializerOrMessage,
            SyntaxKind.QuestionToken => FieldInitializerConditionalMessage,
            SyntaxKind.SwitchKeyword => FieldInitializerSwitchMessage,
            SyntaxKind.IsKeyword => FieldInitializerIsMessage,
            _ => null,
        };
        if (message is not null)
        {
            diagnostics.Add(new VibraniumDiagnostic(VibraniumDiagnosticIds.Unsupported, message,
                VibraniumSymbolHelpers.ToVibraniumSpan(first.GetLocation())));
        }
    }

    private static SyntaxToken FirstFieldInitializerControlFlowToken(SyntaxNode expression)
    {
        SyntaxToken first = default;
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            var token = node switch
            {
                BinaryExpressionSyntax binary when binary.Kind() is SyntaxKind.LogicalAndExpression or
                    SyntaxKind.LogicalOrExpression or SyntaxKind.IsExpression => binary.OperatorToken,
                ConditionalExpressionSyntax conditional => conditional.QuestionToken,
                SwitchExpressionSyntax @switch => @switch.SwitchKeyword,
                IsPatternExpressionSyntax pattern => pattern.IsKeyword,
                _ => default,
            };
            // Pre-order visits a containing expression before its left operand.
            if (token.RawKind != 0 && !token.IsMissing && (first.RawKind == 0 || token.SpanStart < first.SpanStart)) first = token;
        }
        return first;
    }

    private MirRvalue LowerFieldInitializerRvalue(IFieldSymbol field, IOperation initializer, MirType type, VibraniumSpan span)
    {
        if (field.AssociatedSymbol is not IPropertySymbol &&
            FirstFieldInitializerControlFlowToken(initializer.Syntax).RawKind != 0)
        {
            // Keep the MIR field initialized for recovery, without lowering any part of the rejected
            // expression. On the ordinary path the source-order pass has already reported C23.
            if (!_diagnostics.Any(d => d.Span.FilePath == span.FilePath && IsFieldInitializerCutMessage(d.Message)))
            {
                CheckFieldInitializerControlFlow(initializer.Syntax.SyntaxTree.GetRoot(), _diagnostics);
                if (!_diagnostics.Any(d => d.Span.FilePath == span.FilePath && IsFieldInitializerCutMessage(d.Message)))
                    EmitDiag(VibraniumDiagnosticIds.Unsupported, "Unsupported control flow in raw field initializer lowering.", span);
            }
            return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(type), type, span));
        }

        _loweringRawFieldInitializer = true;
        try
        {
            return LowerRvalue(initializer, type);
        }
        finally
        {
            _loweringRawFieldInitializer = false;
        }
    }
}
