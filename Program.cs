using api_codegen;
using api_codegen.Generators;
using api_codegen.Models;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

var mode = args.Length > 0 ? args[0] : "generate";

var toolRoot = ConfigLoader.FindToolRoot();
var config = ConfigLoader.LoadConfig(toolRoot);

switch (mode)
{
    case "generate":
        Generate(toolRoot, config);
        break;
    case "copy":
        Copy(toolRoot, config);
        break;
    default:
        Console.Error.WriteLine($"未知のコマンドです: {mode}(generate または copy を指定してください)");
        return 1;
}

return 0;

static void Generate(string toolRoot, ApiCodeGenConfig config)
{
    var readerSettings = new OpenApiReaderSettings();
    readerSettings.AddYamlReader();

    var openApiPath = Path.Combine(toolRoot, config.Input.OpenApiPath);
    using var stream = new MemoryStream(File.ReadAllBytes(openApiPath));
    var readResult = OpenApiDocument.Load(stream, "yaml", readerSettings);
    var doc = readResult.Document ?? throw new InvalidOperationException(
        $"{openApiPath} の読み込みに失敗しました: {string.Join(", ", readResult.Diagnostic?.Errors ?? [])}");

    var rootNamespace = config.Output.Namespace;
    var outputDir = Path.Combine(toolRoot, config.Output.Dir);
    CleanDir(outputDir);
    var dtoDir = Directory.CreateDirectory(Path.Combine(outputDir, "Dto")).FullName;
    var clientDir = Directory.CreateDirectory(Path.Combine(outputDir, "Client")).FullName;

    // 1. DTO(components.schemas 1件ごとに1クラス)
    foreach (var (name, schema) in doc.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>())
    {
        var source = DtoGenerator.Generate(name, schema, rootNamespace);
        File.WriteAllText(Path.Combine(dtoDir, $"{name}.cs"), source);
        Console.WriteLine($"generated: Dto/{name}.cs");
    }

    // 2. 全オペレーションをタグ別に収集
    var operationsByTag = new Dictionary<string, List<OperationInfo>>();
    foreach (var (path, pathItem) in doc.Paths!)
    {
        foreach (var (httpMethod, operation) in pathItem.Operations ?? new Dictionary<HttpMethod, OpenApiOperation>())
        {
            var tag = operation.Tags?.FirstOrDefault()?.Name
                ?? throw new InvalidOperationException($"{path} にtagがありません(api-codegenはtag必須)");
            var operationId = operation.OperationId
                ?? throw new InvalidOperationException($"{path} にoperationIdがありません(api-codegenはoperationId必須)");

            string? requestTypeName = null;
            if (operation.RequestBody?.Content?.TryGetValue("application/json", out var reqMediaType) == true
                && reqMediaType.Schema is { } reqSchema)
            {
                requestTypeName = TypeMapper.Resolve(reqSchema);
            }

            string? responseTypeName = null;
            var successResponse = operation.Responses?.FirstOrDefault(r => r.Key.StartsWith('2')).Value;
            if (successResponse?.Content?.TryGetValue("application/json", out var respMediaType) == true
                && respMediaType.Schema is { } respSchema)
            {
                responseTypeName = TypeMapper.Resolve(respSchema);
            }

            var pathParameters = (operation.Parameters ?? [])
                .Where(p => p.In == ParameterLocation.Path)
                .Select(p => new PathParameterInfo(p.Name!, TypeMapper.Resolve(p.Schema!)))
                .ToList();

            var info = new OperationInfo(
                Tag: tag,
                MethodName: NameConversion.ToClientMethodName(operationId),
                HttpMethod: httpMethod.Method,
                Path: path,
                PathParameters: pathParameters,
                RequestTypeName: requestTypeName,
                ResponseTypeName: responseTypeName,
                RequiresAuth: (operation.Security?.Count ?? 0) > 0);

            if (!operationsByTag.TryGetValue(tag, out var list))
            {
                list = [];
                operationsByTag[tag] = list;
            }

            list.Add(info);
        }
    }

    // 3. タグ別APIクライアント
    foreach (var (tag, operations) in operationsByTag)
    {
        var className = $"{NameConversion.ToPascalCase(tag)}ApiClient";
        var source = ApiClientGenerator.Generate(tag, operations, rootNamespace);
        File.WriteAllText(Path.Combine(clientDir, $"{className}.cs"), source);
        Console.WriteLine($"generated: Client/{className}.cs");
    }

    // 4. 共通ランタイム(ApiRequest / ApiException、1回だけ)
    File.WriteAllText(Path.Combine(clientDir, "ApiException.cs"), RuntimeSupportGenerator.GenerateApiException(rootNamespace));
    Console.WriteLine("generated: Client/ApiException.cs");
    File.WriteAllText(Path.Combine(clientDir, "ApiRequest.cs"), RuntimeSupportGenerator.GenerateApiRequest(rootNamespace));
    Console.WriteLine("generated: Client/ApiRequest.cs");
}

static void Copy(string toolRoot, ApiCodeGenConfig config)
{
    if (config.Copy is null)
    {
        throw new InvalidOperationException("config.yaml に copy セクションがありません");
    }

    var outputDir = Path.Combine(toolRoot, config.Output.Dir);
    if (!Directory.Exists(outputDir))
    {
        throw new InvalidOperationException($"{outputDir} がありません。先に `dotnet run -- generate` を実行してください");
    }

    var destDir = Path.Combine(toolRoot, config.Copy.DestDir);
    CleanDir(destDir);
    CopyDir(outputDir, destDir);
    Console.WriteLine($"copied: {config.Output.Dir} -> {config.Copy.DestDir}");
}

static void CleanDir(string path)
{
    if (Directory.Exists(path))
    {
        Directory.Delete(path, recursive: true);
    }

    Directory.CreateDirectory(path);
}

static void CopyDir(string sourceDir, string destDir)
{
    foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(dir.Replace(sourceDir, destDir));
    }

    foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
    {
        File.Copy(file, file.Replace(sourceDir, destDir), overwrite: true);
    }
}
