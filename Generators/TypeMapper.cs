using Microsoft.OpenApi;

namespace api_codegen.Generators;

/// <summary>OpenAPIスキーマをC#の型名に解決する。</summary>
public static class TypeMapper
{
    public static string Resolve(IOpenApiSchema schema)
    {
        if (schema is OpenApiSchemaReference reference)
        {
            // $ref先のスキーマ名はそのままDTOクラス名として使う(utoipaがPascalCaseで出力するため変換不要)
            return reference.Reference!.Id!;
        }

        return schema.Type switch
        {
            JsonSchemaType.String when schema.Format == "date-time" => "System.DateTime",
            JsonSchemaType.String => "string",
            JsonSchemaType.Integer when schema.Format == "int64" => "long",
            JsonSchemaType.Integer => "int",
            JsonSchemaType.Number => "double",
            JsonSchemaType.Boolean => "bool",
            JsonSchemaType.Array => $"System.Collections.Generic.List<{Resolve(schema.Items!)}>",
            _ => throw new InvalidOperationException(
                $"未対応のOpenAPI型です: {schema.Type}(nullable/oneOf等はapi-codegen未対応)"),
        };
    }
}
