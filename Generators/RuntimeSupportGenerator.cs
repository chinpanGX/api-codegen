namespace api_codegen.Generators;

/// <summary>
/// タグ別APIクライアント(Generators/ApiClientGenerator.cs)が共通で使う、
/// UnityWebRequest経由のHTTP送受信ヘルパー(ApiRequest)と例外型(ApiException)。
/// 1回だけ生成し、各クライアントクラスから直接参照させる
/// (master-data-pipelineの「pipeline内で完結する参照は直接参照する」原則を踏襲)。
/// UniTask(Cysharp.Threading.Tasks)には依存するが、VContainer等のDIコンテナには依存しない。
/// </summary>
public static class RuntimeSupportGenerator
{
    public static string GenerateApiException(string rootNamespace)
    {
        return $$"""
            {{GeneratedFileHeader.Text}}using System;

            namespace {{rootNamespace}}.Client
            {
                /// <summary>APIが非2xxを返した場合にスローされる例外。</summary>
                public sealed class ApiException : Exception
                {
                    public int StatusCode { get; }
                    public string? ResponseBody { get; }

                    public ApiException(int statusCode, string message, string? responseBody)
                        : base(message)
                    {
                        StatusCode = statusCode;
                        ResponseBody = responseBody;
                    }
                }
            }
            """;
    }

    public static string GenerateApiRequest(string rootNamespace)
    {
        return $$"""
            {{GeneratedFileHeader.Text}}using System.Text;
            using System.Text.Json;
            using Cysharp.Threading.Tasks;
            using UnityEngine.Networking;

            namespace {{rootNamespace}}.Client
            {
                /// <summary>各ApiClientから使う、UnityWebRequestを介した薄いJSON HTTP送受信ヘルパー。</summary>
                public static class ApiRequest
                {
                    /// <summary>レスポンスボディをTResponseとしてデシリアライズして返す。</summary>
                    public static async UniTask<TResponse> SendAsync<TResponse>(
                        string baseUrl, string method, string path, object? body, string? accessToken)
                    {
                        using var www = await ExecuteAsync(baseUrl, method, path, body, accessToken);
                        return JsonSerializer.Deserialize<TResponse>(www.downloadHandler.text)!;
                    }

                    /// <summary>レスポンスボディを使わない(200が空ボディの)エンドポイント用。</summary>
                    public static async UniTask SendAsync(
                        string baseUrl, string method, string path, object? body, string? accessToken)
                    {
                        using var www = await ExecuteAsync(baseUrl, method, path, body, accessToken);
                    }

                    private static async UniTask<UnityWebRequest> ExecuteAsync(
                        string baseUrl, string method, string path, object? body, string? accessToken)
                    {
                        var www = new UnityWebRequest(baseUrl + path, method)
                        {
                            downloadHandler = new DownloadHandlerBuffer(),
                        };

                        if (body is not null)
                        {
                            var json = JsonSerializer.Serialize(body);
                            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                            www.SetRequestHeader("Content-Type", "application/json");
                        }

                        if (accessToken is not null)
                        {
                            www.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                        }

                        await www.SendWebRequest();

                        if (www.result != UnityWebRequest.Result.Success)
                        {
                            throw new ApiException((int)www.responseCode, www.error, www.downloadHandler?.text);
                        }

                        return www;
                    }
                }
            }
            """;
    }
}
