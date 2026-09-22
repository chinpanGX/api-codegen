using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.OpenApi;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace api_codegen.Generators;

/// <summary>
/// OpenAPIの1スキーマ(components.schemas)からRoslyn構文木でDTO(POCO)の.csソースを組み立てる。
/// JSONのプロパティ名(utoipa/serdeがcamelCaseで出力したもの)を[JsonPropertyName]で保持し、
/// C#側のプロパティ名はPascalCaseにする(master-data-pipeline/csharp-codegenのPocoClassGeneratorと同じ方針)。
/// </summary>
public static class DtoGenerator
{
    public static string Generate(string schemaName, IOpenApiSchema schema, string rootNamespace)
    {
        var properties = (schema.Properties ?? new Dictionary<string, IOpenApiSchema>())
            .Select(kv => BuildProperty(kv.Key, kv.Value))
            .ToArray();

        var classDeclaration = ClassDeclaration(schemaName)
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.SealedKeyword))
            .AddMembers(properties);

        var namespaceDeclaration = NamespaceDeclaration(ParseName(rootNamespace))
            .AddUsings(UsingDirective(ParseName("System.Text.Json.Serialization")))
            .AddMembers(classDeclaration);

        var compilationUnit = CompilationUnit()
            .AddMembers(namespaceDeclaration)
            .NormalizeWhitespace();

        return GeneratedFileHeader.Text + compilationUnit.ToFullString() + Environment.NewLine;
    }

    private static PropertyDeclarationSyntax BuildProperty(string jsonName, IOpenApiSchema schema)
    {
        var typeName = TypeMapper.Resolve(schema);
        var propertyName = NameConversion.ToPascalCase(jsonName);

        return PropertyDeclaration(ParseTypeName(typeName), propertyName)
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddAttributeLists(AttributeList(SingletonSeparatedList(
                Attribute(ParseName("JsonPropertyName"))
                    .AddArgumentListArguments(AttributeArgument(
                        LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(jsonName)))))))
            .AddAccessorListAccessors(
                AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
                AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));
    }
}
