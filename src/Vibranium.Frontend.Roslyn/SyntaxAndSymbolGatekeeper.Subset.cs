using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;

[assembly: InternalsVisibleTo("Vibranium.Tests")]

namespace Vibranium.Frontend.Roslyn;

internal sealed partial class SyntaxAndSymbolGatekeeper
{
    internal enum SyntaxKindSupport
    {
        Unclassified,
        NonExecutable,
        Admitted,
        CheckedRestriction,
        Unsupported,
    }

    // These references only identify values the gate rejects. No experimental
    // syntax APIs or language features are used. Keep the suppression local.
#pragma warning disable RSEXPERIMENTAL006
    private const SyntaxKind ExperimentalUnsafeExpression = SyntaxKind.UnsafeExpression;
    private const SyntaxKind ExperimentalSafeKeyword = SyntaxKind.SafeKeyword;
#pragma warning restore RSEXPERIMENTAL006

    // This is deliberately not based on SyntaxFacts' numeric ranges or on a
    // fallback that admits known enum values. A new Roslyn kind must be reviewed.
    internal static SyntaxKindSupport ClassifySyntaxKind(SyntaxKind kind) => kind switch
    {
        // Parser sentinels, recovery, documentation XML, and lexer-only artifacts.
        SyntaxKind.None or SyntaxKind.List or SyntaxKind.EndOfFileToken or SyntaxKind.EndOfDirectiveToken or
        SyntaxKind.EndOfDocumentationCommentToken or SyntaxKind.BadToken or SyntaxKind.BadDirectiveTrivia or
        SyntaxKind.IncompleteMember or SyntaxKind.UnknownAccessorDeclaration or SyntaxKind.SkippedTokensTrivia or
        SyntaxKind.ConflictMarkerTrivia or SyntaxKind.EndOfLineTrivia or SyntaxKind.WhitespaceTrivia or
        SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia or
        SyntaxKind.DocumentationCommentExteriorTrivia or SyntaxKind.SingleLineDocumentationCommentTrivia or
        SyntaxKind.MultiLineDocumentationCommentTrivia or SyntaxKind.DisabledTextTrivia or
        SyntaxKind.PreprocessingMessageTrivia or SyntaxKind.SlashGreaterThanToken or SyntaxKind.LessThanSlashToken or
        SyntaxKind.XmlCommentStartToken or SyntaxKind.XmlCommentEndToken or SyntaxKind.XmlCDataStartToken or
        SyntaxKind.XmlCDataEndToken or SyntaxKind.XmlProcessingInstructionStartToken or
        SyntaxKind.XmlProcessingInstructionEndToken or SyntaxKind.XmlEntityLiteralToken or
        SyntaxKind.XmlTextLiteralToken or SyntaxKind.XmlTextLiteralNewLineToken or SyntaxKind.XmlElement or
        SyntaxKind.XmlElementStartTag or SyntaxKind.XmlElementEndTag or SyntaxKind.XmlEmptyElement or
        SyntaxKind.XmlTextAttribute or SyntaxKind.XmlCrefAttribute or SyntaxKind.XmlNameAttribute or
        SyntaxKind.XmlName or SyntaxKind.XmlPrefix or SyntaxKind.XmlText or SyntaxKind.XmlCDataSection or
        SyntaxKind.XmlComment or SyntaxKind.XmlProcessingInstruction or SyntaxKind.TypeCref or
        SyntaxKind.QualifiedCref or SyntaxKind.NameMemberCref or SyntaxKind.IndexerMemberCref or
        SyntaxKind.OperatorMemberCref or SyntaxKind.ConversionOperatorMemberCref or SyntaxKind.CrefParameterList or
        SyntaxKind.CrefBracketedParameterList or SyntaxKind.CrefParameter or SyntaxKind.ExtensionMemberCref or
        SyntaxKind.DollarToken or SyntaxKind.BackslashToken or SyntaxKind.DoubleQuoteToken or
        SyntaxKind.SingleQuoteToken or SyntaxKind.InterpolatedStringToken or SyntaxKind.RazorContentToken or
        SyntaxKind.ElifKeyword or SyntaxKind.EndIfKeyword or SyntaxKind.RegionKeyword or
        SyntaxKind.EndRegionKeyword or SyntaxKind.DefineKeyword or SyntaxKind.UndefKeyword or
        SyntaxKind.WarningKeyword or SyntaxKind.ErrorKeyword or SyntaxKind.LineKeyword or SyntaxKind.PragmaKeyword or
        SyntaxKind.HiddenKeyword or SyntaxKind.ChecksumKeyword or SyntaxKind.DisableKeyword or
        SyntaxKind.RestoreKeyword or SyntaxKind.ReferenceKeyword or SyntaxKind.LoadKeyword or
        SyntaxKind.NullableKeyword or SyntaxKind.EnableKeyword or SyntaxKind.WarningsKeyword or
        SyntaxKind.AnnotationsKeyword or SyntaxKind.LineDirectivePosition => SyntaxKindSupport.NonExecutable,

        // No executable context admits these forms.
        SyntaxKind.ArgListKeyword or SyntaxKind.MakeRefKeyword or SyntaxKind.RefTypeKeyword or
        SyntaxKind.RefValueKeyword or SyntaxKind.RequiredKeyword or SyntaxKind.VolatileKeyword or
        SyntaxKind.AsExpression or SyntaxKind.ListPattern or SyntaxKind.SlicePattern or SyntaxKind.LabeledStatement or
        ExperimentalUnsafeExpression or ExperimentalSafeKeyword or SyntaxKind.SpreadElement or
        SyntaxKind.OmittedTypeArgument or SyntaxKind.AnonymousObjectCreationExpression or
        SyntaxKind.TypeOfExpression or SyntaxKind.TupleType or SyntaxKind.TupleExpression or SyntaxKind.WithElement or
        SyntaxKind.ErrorDirectiveTrivia or SyntaxKind.ReferenceDirectiveTrivia or
        SyntaxKind.LoadDirectiveTrivia or SyntaxKind.ShebangDirectiveTrivia or SyntaxKind.IgnoredDirectiveTrivia => SyntaxKindSupport.Unsupported,

        // Diagnosed by a token, containing syntax, a declaration cut (including init), or a specific rule (e.g. VIB008).
        SyntaxKind.ArgListExpression or SyntaxKind.MakeRefExpression or SyntaxKind.RefTypeExpression or
        SyntaxKind.RefValueExpression or SyntaxKind.IfDirectiveTrivia or SyntaxKind.ElifDirectiveTrivia or
        SyntaxKind.ElseDirectiveTrivia or SyntaxKind.EndIfDirectiveTrivia or SyntaxKind.DefineDirectiveTrivia or
        SyntaxKind.UndefDirectiveTrivia or SyntaxKind.ThisConstructorInitializer or
        SyntaxKind.BaseConstructorInitializer or SyntaxKind.RelationalPattern or SyntaxKind.AndPattern or
        SyntaxKind.FileKeyword or SyntaxKind.PartialKeyword or SyntaxKind.NullableType or SyntaxKind.ScopedType or
        SyntaxKind.ConditionalAccessExpression or SyntaxKind.MemberBindingExpression or
        SyntaxKind.ElementBindingExpression or SyntaxKind.SuppressNullableWarningExpression or
        SyntaxKind.CoalesceExpression or SyntaxKind.CoalesceAssignmentExpression or
        SyntaxKind.NullLiteralExpression or SyntaxKind.NullKeyword or SyntaxKind.QuestionQuestionToken or
        SyntaxKind.QuestionQuestionEqualsToken or SyntaxKind.ArrayType or SyntaxKind.ArrayCreationExpression or
        SyntaxKind.ImplicitArrayCreationExpression or SyntaxKind.ArrayRankSpecifier or
        SyntaxKind.OmittedArraySizeExpression or SyntaxKind.OmittedArraySizeExpressionToken or
        SyntaxKind.ArrayInitializerExpression or SyntaxKind.StackAllocArrayCreationExpression or
        SyntaxKind.StackAllocKeyword or SyntaxKind.CheckedExpression or SyntaxKind.CheckedStatement or
        SyntaxKind.GotoStatement or SyntaxKind.GotoCaseStatement or SyntaxKind.GotoDefaultStatement or
        SyntaxKind.ThrowStatement or SyntaxKind.ThrowExpression or SyntaxKind.TryStatement or
        SyntaxKind.SimpleLambdaExpression or SyntaxKind.ParenthesizedLambdaExpression or
        SyntaxKind.AnonymousMethodExpression or SyntaxKind.QueryExpression or SyntaxKind.UnsafeStatement or
        SyntaxKind.PointerType or SyntaxKind.FunctionPointerType or SyntaxKind.FixedStatement or
        SyntaxKind.AwaitExpression or SyntaxKind.YieldReturnStatement or SyntaxKind.YieldBreakStatement or
        SyntaxKind.LockStatement or SyntaxKind.ExtensionBlockDeclaration or SyntaxKind.WithExpression or
        SyntaxKind.SizeOfExpression or SyntaxKind.InterpolatedStringExpression or
        SyntaxKind.ImplicitStackAllocArrayCreationExpression or SyntaxKind.ExplicitInterfaceSpecifier or
        SyntaxKind.TupleElement or
        SyntaxKind.ParenthesizedVariableDesignation or SyntaxKind.ForEachVariableStatement or
        SyntaxKind.CollectionInitializerExpression or SyntaxKind.ObjectInitializerExpression or
        SyntaxKind.ComplexElementInitializerExpression or SyntaxKind.ImplicitElementAccess or
        SyntaxKind.EventFieldDeclaration or SyntaxKind.EventDeclaration or SyntaxKind.DelegateDeclaration or
        SyntaxKind.InitAccessorDeclaration or SyntaxKind.ConversionOperatorDeclaration or
        SyntaxKind.InterfaceDeclaration or SyntaxKind.DestructorDeclaration or SyntaxKind.UnionDeclaration or
        SyntaxKind.AllowsConstraintClause or SyntaxKind.RefStructConstraint or SyntaxKind.UnsafeKeyword or
        SyntaxKind.AsyncKeyword or SyntaxKind.AbstractKeyword or SyntaxKind.VirtualKeyword or
        SyntaxKind.OverrideKeyword or SyntaxKind.ProtectedKeyword or SyntaxKind.ExternKeyword or
        SyntaxKind.DecimalKeyword or SyntaxKind.ObjectKeyword or SyntaxKind.TypeOfKeyword or
        SyntaxKind.SizeOfKeyword or SyntaxKind.TryKeyword or SyntaxKind.CatchKeyword or SyntaxKind.FinallyKeyword or
        SyntaxKind.LockKeyword or SyntaxKind.GotoKeyword or SyntaxKind.ThrowKeyword or SyntaxKind.EventKeyword or
        SyntaxKind.InterfaceKeyword or SyntaxKind.DelegateKeyword or SyntaxKind.CheckedKeyword or
        SyntaxKind.ExplicitKeyword or SyntaxKind.ImplicitKeyword or SyntaxKind.FixedKeyword or
        SyntaxKind.ParamsKeyword or SyntaxKind.ClosedKeyword or SyntaxKind.YieldKeyword or SyntaxKind.AddKeyword or
        SyntaxKind.RemoveKeyword or SyntaxKind.FromKeyword or SyntaxKind.GroupKeyword or SyntaxKind.JoinKeyword or
        SyntaxKind.IntoKeyword or SyntaxKind.LetKeyword or SyntaxKind.ByKeyword or SyntaxKind.SelectKeyword or
        SyntaxKind.OrderByKeyword or SyntaxKind.OnKeyword or SyntaxKind.EqualsKeyword or
        SyntaxKind.AscendingKeyword or SyntaxKind.DescendingKeyword or SyntaxKind.NameOfKeyword or
        SyntaxKind.AwaitKeyword or SyntaxKind.AndKeyword or SyntaxKind.WithKeyword or SyntaxKind.InitKeyword or
        SyntaxKind.ManagedKeyword or SyntaxKind.AllowsKeyword or SyntaxKind.ExtensionKeyword or
        SyntaxKind.UnionKeyword or SyntaxKind.MinusGreaterThanToken or SyntaxKind.EqualsGreaterThanToken or
        SyntaxKind.InterpolatedStringStartToken or SyntaxKind.InterpolatedStringEndToken or
        SyntaxKind.InterpolatedVerbatimStringStartToken or SyntaxKind.InterpolatedStringTextToken or
        SyntaxKind.InterpolatedSingleLineRawStringStartToken or SyntaxKind.InterpolatedMultiLineRawStringStartToken or
        SyntaxKind.InterpolatedRawStringEndToken or SyntaxKind.OmittedTypeArgumentToken or
        SyntaxKind.AnonymousObjectMemberDeclarator or SyntaxKind.PointerMemberAccessExpression or
        SyntaxKind.PointerIndirectionExpression or SyntaxKind.AddressOfExpression or SyntaxKind.QueryBody or
        SyntaxKind.FromClause or SyntaxKind.LetClause or SyntaxKind.JoinClause or SyntaxKind.JoinIntoClause or
        SyntaxKind.WhereClause or SyntaxKind.OrderByClause or SyntaxKind.AscendingOrdering or
        SyntaxKind.DescendingOrdering or SyntaxKind.SelectClause or SyntaxKind.GroupClause or
        SyntaxKind.QueryContinuation or SyntaxKind.CatchClause or SyntaxKind.CatchDeclaration or
        SyntaxKind.CatchFilterClause or SyntaxKind.FinallyClause or SyntaxKind.AddAccessorDeclaration or
        SyntaxKind.RemoveAccessorDeclaration or SyntaxKind.Interpolation or SyntaxKind.InterpolatedStringText or
        SyntaxKind.InterpolationAlignmentClause or SyntaxKind.InterpolationFormatClause or
        SyntaxKind.FunctionPointerParameter or SyntaxKind.FunctionPointerParameterList or
        SyntaxKind.FunctionPointerCallingConvention or SyntaxKind.WithInitializerExpression or
        SyntaxKind.DefaultConstraint or SyntaxKind.PrimaryConstructorBaseType or
        SyntaxKind.FunctionPointerUnmanagedCallingConventionList or
        SyntaxKind.FunctionPointerUnmanagedCallingConvention or
        SyntaxKind.ExternAliasDirective => SyntaxKindSupport.CheckedRestriction,

        // Punctuation and lexical forms used by admitted grammar. Syntax and symbol checks below still apply.
        SyntaxKind.TildeToken or SyntaxKind.ExclamationToken or SyntaxKind.PercentToken or SyntaxKind.CaretToken or
        SyntaxKind.AmpersandToken or SyntaxKind.AsteriskToken or SyntaxKind.OpenParenToken or
        SyntaxKind.CloseParenToken or SyntaxKind.MinusToken or SyntaxKind.PlusToken or SyntaxKind.EqualsToken or
        SyntaxKind.OpenBraceToken or SyntaxKind.CloseBraceToken or SyntaxKind.OpenBracketToken or
        SyntaxKind.CloseBracketToken or SyntaxKind.BarToken or SyntaxKind.ColonToken or SyntaxKind.SemicolonToken or
        SyntaxKind.LessThanToken or SyntaxKind.CommaToken or SyntaxKind.GreaterThanToken or SyntaxKind.DotToken or
        SyntaxKind.QuestionToken or SyntaxKind.HashToken or SyntaxKind.SlashToken or SyntaxKind.DotDotToken or
        SyntaxKind.BarBarToken or SyntaxKind.AmpersandAmpersandToken or SyntaxKind.MinusMinusToken or
        SyntaxKind.PlusPlusToken or SyntaxKind.ColonColonToken or SyntaxKind.ExclamationEqualsToken or
        SyntaxKind.EqualsEqualsToken or SyntaxKind.LessThanEqualsToken or SyntaxKind.LessThanLessThanToken or
        SyntaxKind.LessThanLessThanEqualsToken or SyntaxKind.GreaterThanEqualsToken or
        SyntaxKind.GreaterThanGreaterThanToken or SyntaxKind.GreaterThanGreaterThanEqualsToken or
        SyntaxKind.SlashEqualsToken or SyntaxKind.AsteriskEqualsToken or SyntaxKind.BarEqualsToken or
        SyntaxKind.AmpersandEqualsToken or SyntaxKind.PlusEqualsToken or SyntaxKind.MinusEqualsToken or
        SyntaxKind.CaretEqualsToken or SyntaxKind.PercentEqualsToken or
        SyntaxKind.GreaterThanGreaterThanGreaterThanToken or
        SyntaxKind.GreaterThanGreaterThanGreaterThanEqualsToken or SyntaxKind.BoolKeyword or SyntaxKind.ByteKeyword or
        SyntaxKind.SByteKeyword or SyntaxKind.ShortKeyword or SyntaxKind.UShortKeyword or SyntaxKind.IntKeyword or
        SyntaxKind.UIntKeyword or SyntaxKind.LongKeyword or SyntaxKind.ULongKeyword or SyntaxKind.DoubleKeyword or
        SyntaxKind.FloatKeyword or SyntaxKind.StringKeyword or SyntaxKind.CharKeyword or SyntaxKind.VoidKeyword or
        SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword or SyntaxKind.IfKeyword or SyntaxKind.ElseKeyword or
        SyntaxKind.WhileKeyword or SyntaxKind.ForKeyword or SyntaxKind.ForEachKeyword or SyntaxKind.DoKeyword or
        SyntaxKind.SwitchKeyword or SyntaxKind.CaseKeyword or SyntaxKind.DefaultKeyword or SyntaxKind.BreakKeyword or
        SyntaxKind.ContinueKeyword or SyntaxKind.ReturnKeyword or SyntaxKind.IsKeyword or SyntaxKind.AsKeyword or
        SyntaxKind.BaseKeyword or SyntaxKind.NamespaceKeyword or SyntaxKind.UsingKeyword or SyntaxKind.ClassKeyword or
        SyntaxKind.StructKeyword or SyntaxKind.EnumKeyword or SyntaxKind.UncheckedKeyword or
        SyntaxKind.OperatorKeyword or SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword or
        SyntaxKind.InternalKeyword or SyntaxKind.StaticKeyword or SyntaxKind.ReadOnlyKeyword or
        SyntaxKind.SealedKeyword or SyntaxKind.ConstKeyword or SyntaxKind.NewKeyword or SyntaxKind.RefKeyword or
        SyntaxKind.OutKeyword or SyntaxKind.InKeyword or SyntaxKind.ThisKeyword or SyntaxKind.ScopedKeyword or
        SyntaxKind.AliasKeyword or SyntaxKind.GlobalKeyword or SyntaxKind.AssemblyKeyword or
        SyntaxKind.ModuleKeyword or SyntaxKind.TypeKeyword or SyntaxKind.FieldKeyword or SyntaxKind.MethodKeyword or
        SyntaxKind.ParamKeyword or SyntaxKind.PropertyKeyword or SyntaxKind.TypeVarKeyword or SyntaxKind.GetKeyword or
        SyntaxKind.SetKeyword or SyntaxKind.WhereKeyword or SyntaxKind.WhenKeyword or SyntaxKind.OrKeyword or
        SyntaxKind.NotKeyword or SyntaxKind.RecordKeyword or SyntaxKind.UnmanagedKeyword or SyntaxKind.VarKeyword or
        SyntaxKind.NumericLiteralToken or SyntaxKind.CharacterLiteralToken or SyntaxKind.StringLiteralToken or
        SyntaxKind.SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken or
        SyntaxKind.Utf8StringLiteralToken or SyntaxKind.Utf8SingleLineRawStringLiteralToken or
        SyntaxKind.Utf8MultiLineRawStringLiteralToken or SyntaxKind.UnderscoreToken or SyntaxKind.IdentifierToken => SyntaxKindSupport.Admitted,

        // Directive forms admitted by the subset; diagnostic-only directives do not enter MIR.
        SyntaxKind.RegionDirectiveTrivia or SyntaxKind.EndRegionDirectiveTrivia or
        SyntaxKind.PragmaWarningDirectiveTrivia or SyntaxKind.PragmaChecksumDirectiveTrivia or
        SyntaxKind.NullableDirectiveTrivia or SyntaxKind.WarningDirectiveTrivia or SyntaxKind.LineDirectiveTrivia or
        SyntaxKind.LineSpanDirectiveTrivia => SyntaxKindSupport.Admitted,

        // Name, expression, and pattern grammar; symbol-dependent and contextual bans remain in force.
        SyntaxKind.QualifiedName or SyntaxKind.TypeArgumentList or SyntaxKind.AliasQualifiedName or
        SyntaxKind.PredefinedType or SyntaxKind.ParenthesizedExpression or SyntaxKind.ConditionalExpression or
        SyntaxKind.ElementAccessExpression or SyntaxKind.ArgumentList or SyntaxKind.BracketedArgumentList or
        SyntaxKind.NameColon or SyntaxKind.ObjectCreationExpression or SyntaxKind.IsPatternExpression or
        SyntaxKind.RangeExpression or SyntaxKind.ImplicitObjectCreationExpression or SyntaxKind.AddExpression or
        SyntaxKind.SubtractExpression or SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression or
        SyntaxKind.ModuloExpression or SyntaxKind.LeftShiftExpression or SyntaxKind.RightShiftExpression or
        SyntaxKind.LogicalOrExpression or SyntaxKind.LogicalAndExpression or SyntaxKind.BitwiseOrExpression or
        SyntaxKind.BitwiseAndExpression or SyntaxKind.ExclusiveOrExpression or SyntaxKind.EqualsExpression or
        SyntaxKind.NotEqualsExpression or SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression or
        SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression or SyntaxKind.IsExpression or
        SyntaxKind.SimpleMemberAccessExpression or SyntaxKind.UnsignedRightShiftExpression or
        SyntaxKind.SimpleAssignmentExpression or SyntaxKind.AddAssignmentExpression or
        SyntaxKind.SubtractAssignmentExpression or SyntaxKind.MultiplyAssignmentExpression or
        SyntaxKind.DivideAssignmentExpression or SyntaxKind.ModuloAssignmentExpression or
        SyntaxKind.AndAssignmentExpression or SyntaxKind.ExclusiveOrAssignmentExpression or
        SyntaxKind.OrAssignmentExpression or SyntaxKind.LeftShiftAssignmentExpression or
        SyntaxKind.RightShiftAssignmentExpression or SyntaxKind.UnsignedRightShiftAssignmentExpression or
        SyntaxKind.UnaryPlusExpression or SyntaxKind.UnaryMinusExpression or SyntaxKind.BitwiseNotExpression or
        SyntaxKind.LogicalNotExpression or SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression or
        SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression or SyntaxKind.IndexExpression or
        SyntaxKind.ThisExpression or SyntaxKind.BaseExpression or SyntaxKind.NumericLiteralExpression or
        SyntaxKind.StringLiteralExpression or SyntaxKind.CharacterLiteralExpression or
        SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression or
        SyntaxKind.DefaultLiteralExpression or SyntaxKind.Utf8StringLiteralExpression or SyntaxKind.FieldExpression or
        SyntaxKind.UncheckedExpression or SyntaxKind.DefaultExpression or SyntaxKind.DeclarationExpression or
        SyntaxKind.RefExpression or SyntaxKind.RefType or SyntaxKind.CollectionExpression or
        SyntaxKind.ExpressionElement or SyntaxKind.SingleVariableDesignation or SyntaxKind.DeclarationPattern or
        SyntaxKind.ConstantPattern or SyntaxKind.CasePatternSwitchLabel or SyntaxKind.WhenClause or
        SyntaxKind.DiscardDesignation or SyntaxKind.RecursivePattern or SyntaxKind.PropertyPatternClause or
        SyntaxKind.Subpattern or SyntaxKind.PositionalPatternClause or SyntaxKind.DiscardPattern or
        SyntaxKind.SwitchExpression or SyntaxKind.SwitchExpressionArm or SyntaxKind.VarPattern or
        SyntaxKind.ParenthesizedPattern or SyntaxKind.TypePattern or SyntaxKind.OrPattern or SyntaxKind.NotPattern or
        SyntaxKind.ExpressionColon or SyntaxKind.UsingStatement or SyntaxKind.BaseList or SyntaxKind.SimpleBaseType or
        SyntaxKind.MethodDeclaration or SyntaxKind.LocalFunctionStatement or SyntaxKind.PropertyDeclaration or
        SyntaxKind.IndexerDeclaration or SyntaxKind.OperatorDeclaration or SyntaxKind.GenericName or
        SyntaxKind.InvocationExpression or SyntaxKind.CastExpression or SyntaxKind.Argument or SyntaxKind.Attribute or
        SyntaxKind.AttributeList or SyntaxKind.AttributeTargetSpecifier or SyntaxKind.IdentifierName => SyntaxKindSupport.Admitted,

        // Statements and declarations; these may still violate an existing context or symbol rule.
        SyntaxKind.Block or SyntaxKind.LocalDeclarationStatement or SyntaxKind.VariableDeclaration or
        SyntaxKind.VariableDeclarator or SyntaxKind.EqualsValueClause or SyntaxKind.ExpressionStatement or
        SyntaxKind.EmptyStatement or SyntaxKind.BreakStatement or
        SyntaxKind.ContinueStatement or SyntaxKind.ReturnStatement or SyntaxKind.WhileStatement or
        SyntaxKind.DoStatement or SyntaxKind.ForStatement or SyntaxKind.ForEachStatement or
        SyntaxKind.UncheckedStatement or SyntaxKind.IfStatement or SyntaxKind.ElseClause or
        SyntaxKind.SwitchStatement or SyntaxKind.SwitchSection or SyntaxKind.CaseSwitchLabel or
        SyntaxKind.DefaultSwitchLabel or SyntaxKind.CompilationUnit or SyntaxKind.GlobalStatement or
        SyntaxKind.NamespaceDeclaration or SyntaxKind.UsingDirective or SyntaxKind.FileScopedNamespaceDeclaration or
        SyntaxKind.AttributeArgumentList or SyntaxKind.AttributeArgument or SyntaxKind.NameEquals or
        SyntaxKind.ClassDeclaration or SyntaxKind.StructDeclaration or SyntaxKind.EnumDeclaration or
        SyntaxKind.TypeParameterConstraintClause or SyntaxKind.ConstructorConstraint or SyntaxKind.ClassConstraint or
        SyntaxKind.StructConstraint or SyntaxKind.TypeConstraint or SyntaxKind.EnumMemberDeclaration or
        SyntaxKind.FieldDeclaration or SyntaxKind.ConstructorDeclaration or SyntaxKind.AccessorList or
        SyntaxKind.GetAccessorDeclaration or SyntaxKind.SetAccessorDeclaration or SyntaxKind.ParameterList or
        SyntaxKind.BracketedParameterList or SyntaxKind.Parameter or SyntaxKind.TypeParameterList or
        SyntaxKind.TypeParameter or SyntaxKind.ArrowExpressionClause or SyntaxKind.RecordDeclaration or
        SyntaxKind.RecordStructDeclaration => SyntaxKindSupport.Admitted,

        _ => SyntaxKindSupport.Unclassified,
    };

    internal static bool RejectsSyntaxKind(SyntaxKind kind) => ClassifySyntaxKind(kind) is
        SyntaxKindSupport.Unclassified or SyntaxKindSupport.Unsupported;
}
