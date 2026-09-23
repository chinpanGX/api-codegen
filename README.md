# api-codegen

OpenAPI仕様書(`openapi.yaml`)から、Unity(UniTask)向けのDTO(POCO)と通信APIクライアントを
生成するツールです。特定プロジェクト専用ではない共通ツールとして作られており、
プロジェクト固有の設定はすべて`config.yaml`経由で渡します(ソースコードに直接パスや
名前空間をハードコードしていません)。

`master-data-pipeline`(スプレッドシート/CSV駆動のマスターデータ専用パイプライン)とは別物です。
こちらはコードから自動生成される`openapi.yaml`が入力になる点が異なります。

## 前提ツール

- .NET SDK(net10.0)

## Getting Started

1. `config.yaml`を導入先プロジェクトの実パスに書き換える(後述)。
   `config.yaml`はプロジェクト固有の値を持つため`.gitignore`で追跡対象外なので、
   このリポジトリを導入するたびに新規作成する(下記の内容を参考にする)。
2. 入力元の`openapi.yaml`を用意する(サーバー側のフレームワーク・ツールから
   エクスポートする想定。生成方法はプロジェクトによって異なる)。
3. 下記「コマンド」の`generate`→`copy`の順に実行する。

## コマンド

作業ディレクトリは常に`api-codegen/`(このリポジトリのルート)。

```bash
dotnet run -- generate   # openapi.yaml -> config.yamlのoutput.dir配下にC#コードを生成
dotnet run -- copy       # output.dir配下の生成物をconfig.yamlのcopy.dest_dirへコピー
```

- `generate`と`copy`はコマンドを分離しています(`master-data-pipeline`で確立した
  「生成と配置を分離する」規約を踏襲)。`copy`は`generate`の出力(`output.dir`)を前提とするため、
  必ず`generate`を先に実行する(`output.dir`が無い状態で`copy`だけ実行するとエラーになる)
- 引数を省略すると`generate`として扱われる(`dotnet run`のみでも`generate`が走る)。
  `generate`/`copy`以外の引数を渡すとエラーになる
- `generate`・`copy`とも、出力先ディレクトリの中身を実行直前に全削除してから書き込む
  (差分マージはしない)。`copy`の配置先(`config.yaml`の`copy.dest_dir`、通常
  `Client/Assets/Scripts/.../Generated/`のような専用フォルダ)を手で編集していた場合は失われる
  (生成物なので手編集しない)
- Rust側のhandler・DTOを変更した後は、まずサーバー側で`openapi.yaml`を再生成してから
  `generate`を実行する(入力が古いままだと反映されない。Atlasプロジェクトでの具体的な手順は
  `api-codegen`スキル参照)

### Unityの`.meta`ファイルについて

`copy`は`output.dir`にある`.cs`ファイルのみをコピーし、Unity用の`.meta`ファイルは対象外です。
配置先の既存`.meta`は削除→再作成のタイミングで失われるため、Unity Editorを開いた際に
新しいGUIDで再生成されます。生成されたDTO/APIクライアントをInspector上のシリアライズ
フィールド等でGUID参照している場合は再リンクが必要になる点に注意してください
(通常のC#コードからの参照は名前空間・クラス名ベースのため影響ありません)。

## config.yaml

```yaml
input:
  open_api_path: "../path/to/openapi.yaml"      # 入力元(このファイルからの相対パス)

output:
  dir: "./out/generated_csharp"                 # generateの出力先
  namespace: "YourApp.Infrastructure.Api"       # 生成コードの名前空間(DTO・APIクライアントともにこのまま出力する)

copy:
  dest_dir: "../YourUnityProject/Assets/Scripts/Infrastructure/Api" # copyの配置先
```

別プロジェクトで使う場合は、このファイルを丸ごとそのプロジェクトの値に書き換えるだけで使い回せます。

## 生成物

```
{output.dir}/
  Dto/
    {SchemaName}.cs        -- components.schemas 1件につき1クラス(POCO、System.Text.Json属性付き)
  Client/
    {Tag}ApiClient.cs       -- OpenAPIのtag単位でまとめた通信APIクラス
    ApiRequest.cs            -- UnityWebRequestを介した送受信の共通ヘルパー(1回だけ生成)
    ApiException.cs          -- 非2xxレスポンス時にスローされる例外(1回だけ生成)
```

- `{Tag}ApiClient`は`UnityEngine.Networking.UnityWebRequest`を`Cysharp.Threading.Tasks.UniTask`
  でラップした薄いメソッドのみを持つ。**VContainer等のDIコンテナには依存しない**
  (`new {Tag}ApiClient(baseUrl)` または、認証が必要なタグは
  `new {Tag}ApiClient(baseUrl, () => currentAccessToken)` として素朴にnewできる)
- 生成される全ファイルの先頭に`DO NOT EDIT`コメントを付与する(`master-data-pipeline`と同じ規約)
- OpenAPIの`security`が付いているオペレーションは`Authorization: Bearer <token>`ヘッダーを
  自動付与する(コンストラクタで渡した`Func<string>`を呼び出し時に評価する)
- パスパラメータ(`in: path`)はメソッド引数として展開され、パステンプレートへ埋め込まれる

## 現時点で対応していないこと

- OpenAPIの型は `string` / `integer` / `number` / `boolean` / `array` / `$ref` のみ対応。
  `nullable`・`oneOf`/`anyOf`・`enum`(文字列列挙)等が入力に含まれる場合は生成時にエラーになる
- リクエストボディ・レスポンスボディとも `application/json` のみ対応
- クエリパラメータ(`in: query`)は未対応
- 生成したC#コードの実コンパイル確認はUnityプロジェクト側で行う想定(このツール自体は
  `UnityEngine`/`Cysharp.Threading.Tasks`を参照しないプレーンなdotnetコンソールアプリのため)。
  DTO・APIクライアント・共通ランタイム(`ApiRequest`/`ApiException`)とも、Roslyn構文木で
  組み立てるため構文レベルは常に妥当(`RuntimeSupportGenerator`はメソッド本体を
  `SyntaxFactory.ParseStatement`でブロックごとパースする形で組み立てている)
