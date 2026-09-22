using api_codegen.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace api_codegen.Generators;

/// <summary>
/// タグ単位でまとめた`OperationInfo`から、UnityWebRequestを`UniTask`でラップした
/// 薄い通信APIクラス(`{Tag}ApiClient`)の.csソースをRoslyn構文木で組み立てる
/// (DtoGeneratorと同じ方針。コンストラクタの分岐等は生成側のC#コードで行うため、
/// 出力自体は常に構文的に正しい)。
/// </summary>
public static class ApiClientGenerator
{
    public static string Generate(string tag, IReadOnlyList<OperationInfo> operations, string rootNamespace)
    {
        var className = $"{NameConversion.ToPascalCase(tag)}ApiClient";
        var requiresAuth = operations.Any(op => op.RequiresAuth);

        var members = new List<MemberDeclarationSyntax>
        {
            BuildField("string", "baseUrl"),
        };

        if (requiresAuth)
        {
            members.Add(BuildField("Func<string>", "accessTokenProvider"));
        }

        members.Add(BuildConstructor(className, requiresAuth));
        members.AddRange(operations.Select(BuildMethod));

        var classDeclaration = ClassDeclaration(className)
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.SealedKeyword))
            .AddMembers(members.ToArray());

        var namespaceDeclaration = NamespaceDeclaration(ParseName(rootNamespace))
            .AddUsings(
                UsingDirective(ParseName("System")),
                UsingDirective(ParseName("Cysharp.Threading.Tasks")))
            .AddMembers(classDeclaration);

        var compilationUnit = CompilationUnit()
            .AddMembers(namespaceDeclaration)
            .NormalizeWhitespace();

        return GeneratedFileHeader.Text + compilationUnit.ToFullString() + Environment.NewLine;
    }

    private static FieldDeclarationSyntax BuildField(string typeName, string fieldName)
    {
        return FieldDeclaration(VariableDeclaration(ParseTypeName(typeName))
                .AddVariables(VariableDeclarator(fieldName)))
            .AddModifiers(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.ReadOnlyKeyword))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }

    private static ConstructorDeclarationSyntax BuildConstructor(string className, bool requiresAuth)
    {
        var parameters = new List<ParameterSyntax> { Parameter(Identifier("baseUrl")).WithType(ParseTypeName("string")) };
        var assignments = new List<StatementSyntax> { AssignFieldFromParameter("baseUrl") };

        if (requiresAuth)
        {
            parameters.Add(Parameter(Identifier("accessTokenProvider")).WithType(ParseTypeName("Func<string>")));
            assignments.Add(AssignFieldFromParameter("accessTokenProvider"));
        }

        return ConstructorDeclaration(className)
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddParameterListParameters(parameters.ToArray())
            .WithBody(Block(assignments));
    }

    private static StatementSyntax AssignFieldFromParameter(string name)
    {
        return ExpressionStatement(AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName(name)),
            IdentifierName(name)));
    }

    private static MethodDeclarationSyntax BuildMethod(OperationInfo op)
    {
        var returnType = op.ResponseTypeName is not null
            ? ParseTypeName($"UniTask<{op.ResponseTypeName}>")
            : ParseTypeName("UniTask");

        // パスパラメータ(あれば先頭)→ボディ、の順でメソッド引数を並べる
        var parameters = op.PathParameters
            .Select(p => Parameter(Identifier(p.Name)).WithType(ParseTypeName(p.TypeName)))
            .Concat(op.RequestTypeName is not null
                ? [Parameter(Identifier("request")).WithType(ParseTypeName(op.RequestTypeName))]
                : [])
            .ToArray();

        var invocation = InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("ApiRequest"), BuildSendAsyncName(op)))
            .AddArgumentListArguments(
                Argument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName("baseUrl"))),
                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(op.HttpMethod))),
                Argument(BuildPathExpression(op)),
                Argument(BuildRequestArgument(op)),
                Argument(BuildAccessTokenArgument(op)));

        return MethodDeclaration(returnType, op.MethodName)
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddParameterListParameters(parameters)
            .WithExpressionBody(ArrowExpressionClause(invocation))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }

    private static SimpleNameSyntax BuildSendAsyncName(OperationInfo op)
    {
        if (op.ResponseTypeName is null)
        {
            return IdentifierName("SendAsync");
        }

        return GenericName(Identifier("SendAsync")).AddTypeArgumentListArguments(ParseTypeName(op.ResponseTypeName));
    }

    private static ExpressionSyntax BuildPathExpression(OperationInfo op)
    {
        if (op.PathParameters.Count == 0)
        {
            return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(op.Path));
        }

        // OpenAPI上のパスパラメータ名とメソッド引数名を一致させているため、
        // パステンプレートの`{name}`をそのままC#の文字列補間の穴としてパースできる
        return ParseExpression($"$\"{op.Path}\"");
    }

    private static ExpressionSyntax BuildRequestArgument(OperationInfo op)
    {
        return op.RequestTypeName is not null
            ? IdentifierName("request")
            : LiteralExpression(SyntaxKind.NullLiteralExpression);
    }

    private static ExpressionSyntax BuildAccessTokenArgument(OperationInfo op)
    {
        if (!op.RequiresAuth)
        {
            return LiteralExpression(SyntaxKind.NullLiteralExpression);
        }

        return InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, ThisExpression(), IdentifierName("accessTokenProvider")));
    }
}
