using api_codegen.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace api_codegen;

/// <summary>config.yaml の読み込みを担当する(master-data-pipeline/csharp-codegen の SchemaLoader と同じ規約)。</summary>
public static class ConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>AppContext.BaseDirectory から上に遡って config.yaml を探す。</summary>
    public static string FindToolRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "config.yaml")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"config.yaml が見つかりません(探索起点: {AppContext.BaseDirectory})");
    }

    public static ApiCodeGenConfig LoadConfig(string toolRoot)
    {
        var path = Path.Combine(toolRoot, "config.yaml");
        using var reader = new StreamReader(path);
        return Deserializer.Deserialize<ApiCodeGenConfig>(reader) ??
               throw new InvalidOperationException($"{path} の読み込みに失敗しました");
    }
}
