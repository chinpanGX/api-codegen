namespace api_codegen.Models;

/// <summary>
/// config.yaml のルート。api-codegen はAtlas以外のプロジェクトでも使い回せる共通ツールなので、
/// プロジェクト固有のパス・名前空間は全てここ経由で外から渡す(ソース側にハードコードしない)。
/// </summary>
public sealed class ApiCodeGenConfig
{
    public InputConfig Input { get; set; } = new();
    public OutputConfig Output { get; set; } = new();
    public CopyConfig? Copy { get; set; }
}

public sealed class InputConfig
{
    /// <summary>OpenAPI仕様書(yaml)へのパス。config.yamlからの相対パス。</summary>
    public string OpenApiPath { get; set; } = "";
}

public sealed class OutputConfig
{
    /// <summary>生成したC#コードの出力先ディレクトリ。config.yamlからの相対パス。</summary>
    public string Dir { get; set; } = "";

    /// <summary>生成コードの名前空間。DTO・APIクライアントともにこの名前空間そのままで出力する。</summary>
    public string Namespace { get; set; } = "";
}

public sealed class CopyConfig
{
    /// <summary>`copy`コマンドの配置先ディレクトリ。config.yamlからの相対パス。</summary>
    public string DestDir { get; set; } = "";
}
