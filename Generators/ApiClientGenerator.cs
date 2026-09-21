using System.Text;
using api_codegen.Models;

namespace api_codegen.Generators;

/// <summary>
/// タグ単位でまとめた`OperationInfo`から、UnityWebRequestを`UniTask`でラップした
/// 薄い通信APIクラス(`{Tag}ApiClient`)の.csソースを組み立てる。
/// 制御構文(コンストラクタ分岐等)を含むため、DtoGeneratorと異なりRoslynではなく
/// 文字列テンプレートで組み立てる(master-data-pipeline/csharp-codegenのAesCryptoGeneratorと同じ方針)。
/// </summary>
public static class ApiClientGenerator
{
    public static string Generate(string tag, IReadOnlyList<OperationInfo> operations, string rootNamespace)
    {
        var className = $"{NameConversion.ToPascalCase(tag)}ApiClient";
        var requiresAuth = operations.Any(op => op.RequiresAuth);

        var sb = new StringBuilder();
        sb.Append(GeneratedFileHeader.Text);
        sb.AppendLine("using System;");
        sb.AppendLine($"using {rootNamespace}.Dto;");
        sb.AppendLine("using Cysharp.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine($"namespace {rootNamespace}.Client");
        sb.AppendLine("{");
        sb.AppendLine($"    public sealed class {className}");
        sb.AppendLine("    {");
        sb.AppendLine("        private readonly string _baseUrl;");

        if (requiresAuth)
        {
            sb.AppendLine("        private readonly Func<string> _accessTokenProvider;");
            sb.AppendLine();
            sb.AppendLine($"        public {className}(string baseUrl, Func<string> accessTokenProvider)");
            sb.AppendLine("        {");
            sb.AppendLine("            _baseUrl = baseUrl;");
            sb.AppendLine("            _accessTokenProvider = accessTokenProvider;");
            sb.AppendLine("        }");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine($"        public {className}(string baseUrl)");
            sb.AppendLine("        {");
            sb.AppendLine("            _baseUrl = baseUrl;");
            sb.AppendLine("        }");
        }

        foreach (var op in operations)
        {
            sb.AppendLine();
            AppendMethod(sb, op);
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void AppendMethod(StringBuilder sb, OperationInfo op)
    {
        var accessTokenArg = op.RequiresAuth ? "_accessTokenProvider()" : "null";
        var requestArg = op.RequestTypeName is not null ? "request" : "null";

        // パスパラメータ(あれば先頭)→ボディ、の順でメソッド引数を並べる
        var parameters = op.PathParameters
            .Select(p => $"{p.TypeName} {p.Name}")
            .Concat(op.RequestTypeName is not null ? [$"{op.RequestTypeName} request"] : [])
            .ToArray();
        var parameterList = string.Join(", ", parameters);

        // パスパラメータが無ければ元のパスをそのまま使い、あれば {name} をC#の文字列補間として評価する
        // (OpenAPI上のプレースホルダ名とメソッド引数名を一致させているため、テンプレートをそのまま$"..."化できる)
        var pathExpr = op.PathParameters.Count == 0 ? $"\"{op.Path}\"" : $"$\"{op.Path}\"";

        if (op.ResponseTypeName is not null)
        {
            sb.AppendLine($"        public UniTask<{op.ResponseTypeName}> {op.MethodName}({parameterList})");
            sb.AppendLine(
                $"            => ApiRequest.SendAsync<{op.ResponseTypeName}>(_baseUrl, \"{op.HttpMethod}\", {pathExpr}, {requestArg}, {accessTokenArg});");
        }
        else
        {
            sb.AppendLine($"        public UniTask {op.MethodName}({parameterList})");
            sb.AppendLine(
                $"            => ApiRequest.SendAsync(_baseUrl, \"{op.HttpMethod}\", {pathExpr}, {requestArg}, {accessTokenArg});");
        }
    }
}
