namespace api_codegen.Models;

/// <summary>パスパラメータ1件(例: `/scout/banners/{id}/offers` の `id`)。</summary>
public sealed record PathParameterInfo(string Name, string TypeName);

/// <summary>OpenAPIの1オペレーション(パス×メソッド)から、APIクライアント生成に必要な情報だけを抜き出したもの。</summary>
public sealed record OperationInfo(
    string Tag,
    string MethodName,
    string HttpMethod,
    string Path,
    IReadOnlyList<PathParameterInfo> PathParameters,
    string? RequestTypeName,
    string? ResponseTypeName,
    bool RequiresAuth
);
