using System.Globalization;
using System.Text;

namespace api_codegen.Generators;

/// <summary>
/// OpenAPI上の名前(スキーマ名・camelCaseなプロパティ名・snake_caseなoperationId)を
/// C#の識別子(PascalCase)に変換する。
/// </summary>
public static class NameConversion
{
    /// <summary>
    /// "deviceId"(camelCase) → "DeviceId"、"register_device_handler"(snake_case) → "RegisterDeviceHandler"
    /// のように、区切り文字の有無に関わらずPascalCaseへ変換する。
    /// </summary>
    public static string ToPascalCase(string name)
    {
        if (!name.Contains('_'))
        {
            // camelCase(またはPascalCase)入力: 先頭だけ大文字化すれば足りる
            return char.ToUpper(name[0], CultureInfo.InvariantCulture) + name[1..];
        }

        var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(char.ToUpper(part[0], CultureInfo.InvariantCulture));
            if (part.Length > 1)
            {
                sb.Append(part[1..]);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// operationId("register_device_handler"等)からAPIクライアントのメソッド名を作る。
    /// Rust側handlerの命名規約(`_handler`サフィックス)を剥がし、`Async`サフィックスを付ける。
    /// </summary>
    public static string ToClientMethodName(string operationId)
    {
        var trimmed = operationId.EndsWith("_handler", StringComparison.Ordinal)
            ? operationId[..^"_handler".Length]
            : operationId;

        return ToPascalCase(trimmed) + "Async";
    }
}
