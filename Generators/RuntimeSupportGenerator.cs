using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace api_codegen.Generators;

/// <summary>
/// UnityWebRequest経由のHTTP送受信ヘルパー(ApiRequest)と例外型(ApiException)を
/// Roslyn構文木で組み立てる。1回だけ生成し、各クライアントクラスから直接参照させる。
/// UniTaskには依存するが、VContainer等のDIコンテナには依存しない。
/// </summary>
public static class RuntimeSupportGenerator
{
    public static string GenerateApiException(string rootNamespace)
    {
        var statusCodeProperty = ReadOnlyAutoProperty("int", "StatusCode");
        var responseBodyProperty = ReadOnlyAutoProperty("string?", "ResponseBody");

        var constructor = ConstructorDeclaration("ApiException")
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddParameterListParameters(
                Parameter(Identifier("statusCode")).WithType(ParseTypeName("int")),
                Parameter(Identifier("message")).WithType(ParseTypeName("string")),
                Parameter(Identifier("responseBody")).WithType(ParseTypeName("string?")))
            .WithInitializer(ConstructorInitializer(
                SyntaxKind.BaseConstructorInitializer,
                ArgumentList(SingletonSeparatedList(Argument(IdentifierName("message"))))))
            .WithBody(Block(
                AssignProperty("StatusCode", "statusCode"),
                AssignProperty("ResponseBody", "responseBody")));

        var classDeclaration = ClassDeclaration("ApiException")
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.SealedKeyword))
            .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(ParseTypeName("Exception")))))
            .AddMembers(statusCodeProperty, responseBodyProperty, constructor)
            .WithLeadingTrivia(DocSummary("APIが非2xxを返した場合にスローされる例外。"));

        var namespaceDeclaration = NamespaceDeclaration(ParseName(rootNamespace))
            .AddUsings(UsingDirective(ParseName("System")))
            .AddMembers(classDeclaration);

        var compilationUnit = CompilationUnit()
            .AddMembers(namespaceDeclaration)
            .NormalizeWhitespace();

        return GeneratedFileHeader.Text + compilationUnit.ToFullString() + Environment.NewLine;
    }

    public static string GenerateApiRequest(string rootNamespace)
    {
        var classDeclaration = ClassDeclaration("ApiRequest")
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .AddMembers(BuildSendAsyncGeneric(), BuildSendAsyncVoid(), BuildExecuteAsync())
            .WithLeadingTrivia(DocSummary("各ApiClientから使う、UnityWebRequestを介した薄いJSON HTTP送受信ヘルパー。"));

        var namespaceDeclaration = NamespaceDeclaration(ParseName(rootNamespace))
            .AddUsings(
                UsingDirective(ParseName("System.Text")),
                UsingDirective(ParseName("System.Text.Json")),
                UsingDirective(ParseName("Cysharp.Threading.Tasks")),
                UsingDirective(ParseName("UnityEngine")),
                UsingDirective(ParseName("UnityEngine.Networking")))
            .AddMembers(classDeclaration);

        var compilationUnit = CompilationUnit()
            .AddMembers(namespaceDeclaration)
            .NormalizeWhitespace();

        return GeneratedFileHeader.Text + compilationUnit.ToFullString() + Environment.NewLine;
    }

    private static PropertyDeclarationSyntax ReadOnlyAutoProperty(string typeName, string propertyName)
    {
        return PropertyDeclaration(ParseTypeName(typeName), propertyName)
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddAccessorListAccessors(
                AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
    }

    private static StatementSyntax AssignProperty(string propertyName, string parameterName)
    {
        return ExpressionStatement(AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            IdentifierName(propertyName),
            IdentifierName(parameterName)));
    }

    private static SyntaxTriviaList DocSummary(string text)
    {
        return TriviaList(Comment($"/// <summary>{text}</summary>"), CarriageReturnLineFeed);
    }

    // SendAsync(2種)・ExecuteAsyncはすべて同じ5引数を取るため、共通化する
    private static MethodDeclarationSyntax WithRequestParameters(MethodDeclarationSyntax method)
    {
        return method.AddParameterListParameters(
            Parameter(Identifier("baseUrl")).WithType(ParseTypeName("string")),
            Parameter(Identifier("method")).WithType(ParseTypeName("string")),
            Parameter(Identifier("path")).WithType(ParseTypeName("string")),
            Parameter(Identifier("body")).WithType(ParseTypeName("object?")),
            Parameter(Identifier("accessToken")).WithType(ParseTypeName("string?")));
    }

    // `using var request = await ExecuteAsync(baseUrl, method, path, body, accessToken);`
    // SendAsyncの2オーバーロード共通の1文目
    private static StatementSyntax BuildUsingRequestDeclaration()
    {
        var invocation = InvocationExpression(IdentifierName("ExecuteAsync"))
            .AddArgumentListArguments(
                Argument(IdentifierName("baseUrl")),
                Argument(IdentifierName("method")),
                Argument(IdentifierName("path")),
                Argument(IdentifierName("body")),
                Argument(IdentifierName("accessToken")));

        return LocalDeclarationStatement(
                VariableDeclaration(IdentifierName("var"))
                    .AddVariables(VariableDeclarator("request")
                        .WithInitializer(EqualsValueClause(AwaitExpression(invocation)))))
            .WithUsingKeyword(Token(SyntaxKind.UsingKeyword));
    }

    private static MethodDeclarationSyntax BuildSendAsyncGeneric()
    {
        var deserializeInvocation = InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("JsonSerializer"),
                    GenericName(Identifier("Deserialize")).AddTypeArgumentListArguments(IdentifierName("TResponse"))))
            .AddArgumentListArguments(Argument(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("request"), IdentifierName("downloadHandler")),
                    IdentifierName("text"))));

        var returnStatement = ReturnStatement(
            PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression, deserializeInvocation));

        var method = MethodDeclaration(ParseTypeName("UniTask<TResponse>"), "SendAsync")
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.AsyncKeyword))
            .WithTypeParameterList(TypeParameterList(SingletonSeparatedList(TypeParameter("TResponse"))))
            .WithBody(Block(BuildUsingRequestDeclaration(), returnStatement))
            .WithLeadingTrivia(DocSummary("レスポンスボディをTResponseとしてデシリアライズして返す。"));

        return WithRequestParameters(method);
    }

    private static MethodDeclarationSyntax BuildSendAsyncVoid()
    {
        var method = MethodDeclaration(ParseTypeName("UniTask"), "SendAsync")
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.AsyncKeyword))
            .WithBody(Block(BuildUsingRequestDeclaration()))
            .WithLeadingTrivia(DocSummary("レスポンスボディを使わない(200が空ボディの)エンドポイント用。"));

        return WithRequestParameters(method);
    }

    private static ExpressionSyntax IsNotNull(string identifierName)
    {
        return IsPatternExpression(
            IdentifierName(identifierName),
            UnaryPattern(ConstantPattern(LiteralExpression(SyntaxKind.NullLiteralExpression))));
    }

    private static StatementSyntax BuildSetRequestHeaderStatement(string headerName, ExpressionSyntax valueExpression)
    {
        var invocation = InvocationExpression(
                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("request"), IdentifierName("SetRequestHeader")))
            .AddArgumentListArguments(
                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(headerName))),
                Argument(valueExpression));

        return ExpressionStatement(invocation);
    }

    private static MethodDeclarationSyntax BuildExecuteAsync()
    {
        var urlDeclaration = LocalDeclarationStatement(
            VariableDeclaration(IdentifierName("var"))
                .AddVariables(VariableDeclarator("url")
                    .WithInitializer(EqualsValueClause(
                        BinaryExpression(SyntaxKind.AddExpression, IdentifierName("baseUrl"), IdentifierName("path"))))));

        var requestObjectCreation = ObjectCreationExpression(ParseTypeName("UnityWebRequest"))
            .AddArgumentListArguments(Argument(IdentifierName("url")), Argument(IdentifierName("method")))
            .WithInitializer(InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SingletonSeparatedList<ExpressionSyntax>(AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    IdentifierName("downloadHandler"),
                    ObjectCreationExpression(ParseTypeName("DownloadHandlerBuffer")).WithArgumentList(ArgumentList())))));

        var requestDeclaration = LocalDeclarationStatement(
            VariableDeclaration(IdentifierName("var"))
                .AddVariables(VariableDeclarator("request")
                    .WithInitializer(EqualsValueClause(requestObjectCreation))));

        var jsonDeclaration = LocalDeclarationStatement(
            VariableDeclaration(IdentifierName("var"))
                .AddVariables(VariableDeclarator("json")
                    .WithInitializer(EqualsValueClause(
                        InvocationExpression(
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("JsonSerializer"), IdentifierName("Serialize")))
                            .AddArgumentListArguments(Argument(IdentifierName("body")))))));

        var uploadHandlerAssignment = ExpressionStatement(AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("request"), IdentifierName("uploadHandler")),
            ObjectCreationExpression(ParseTypeName("UploadHandlerRaw"))
                .AddArgumentListArguments(Argument(
                    InvocationExpression(
                            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                                MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("Encoding"), IdentifierName("UTF8")),
                                IdentifierName("GetBytes")))
                        .AddArgumentListArguments(Argument(IdentifierName("json")))))));

        var contentTypeHeaderStatement = BuildSetRequestHeaderStatement(
            "Content-Type", LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("application/json")));

        var bodyIfStatement = IfStatement(
            IsNotNull("body"),
            Block(jsonDeclaration, uploadHandlerAssignment, contentTypeHeaderStatement));

        var authorizationHeaderStatement = BuildSetRequestHeaderStatement(
            "Authorization", ParseExpression("$\"Bearer {accessToken}\""));

        var accessTokenIfStatement = IfStatement(IsNotNull("accessToken"), Block(authorizationHeaderStatement));

        var awaitSendStatement = ExpressionStatement(AwaitExpression(
            InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("request"), IdentifierName("SendWebRequest")))));

        var logErrorStatement = ExpressionStatement(
            InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("Debug"), IdentifierName("LogError")))
                .AddArgumentListArguments(Argument(
                    ParseExpression("$\"[ApiRequest] {method} {url} -> {(int)e.ResponseCode} {e.Error}\\n{e.Text}\""))));

        var throwStatement = ThrowStatement(
            ObjectCreationExpression(ParseTypeName("ApiException"))
                .AddArgumentListArguments(
                    Argument(CastExpression(ParseTypeName("int"),
                        MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("e"), IdentifierName("ResponseCode")))),
                    Argument(ParseExpression("$\"{method} {path}: {e.Error}\"")),
                    Argument(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("e"), IdentifierName("Text")))));

        var catchClause = CatchClause()
            .WithDeclaration(CatchDeclaration(ParseTypeName("UnityWebRequestException"), Identifier("e")))
            .WithBlock(Block(logErrorStatement, throwStatement));

        // UniTaskのSendWebRequest()拡張はHTTPエラー時にrequest.resultを見て返るのではなく、
        // 自前でUnityWebRequestExceptionをthrowする(request.result判定を後段に置いても
        // 到達しない)ため、失敗経路はtry/catchで捕まえる必要がある。
        var tryStatement = TryStatement()
            .WithBlock(Block(awaitSendStatement))
            .AddCatches(catchClause)
            .WithLeadingTrivia(
                Comment("// UniTaskのSendWebRequest()拡張はHTTPエラー時にrequest.resultを見て返るのではなく、"),
                CarriageReturnLineFeed,
                Comment("// 自前でUnityWebRequestExceptionをthrowする(request.result判定を後段に置いても"),
                CarriageReturnLineFeed,
                Comment("// 到達しない)ため、失敗経路はtry/catchで捕まえる必要がある。"),
                CarriageReturnLineFeed);

        var returnRequestStatement = ReturnStatement(IdentifierName("request"));

        var method = MethodDeclaration(ParseTypeName("UniTask<UnityWebRequest>"), "ExecuteAsync")
            .AddModifiers(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.AsyncKeyword))
            .WithBody(Block(
                urlDeclaration,
                requestDeclaration,
                bodyIfStatement,
                accessTokenIfStatement,
                tryStatement,
                returnRequestStatement));

        return WithRequestParameters(method);
    }
}
