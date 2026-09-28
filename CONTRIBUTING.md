# Contributing to 罫彩

罫彩への改善提案や不具合報告を歓迎します。

## Issue

不具合では、罫彩のバージョン、Windowsのバージョン、モニターの台数・配置・拡大率、再現手順、期待した結果、実際の結果を記載してください。スクリーンショットやテストデータへ、付箋の本文、個人情報、職場の機密情報を含めないでください。

## Development

必要な環境はWindowsと.NET 10 SDKです。

```powershell
dotnet build tests/YtecStickyNote.Tests/YtecStickyNote.Tests.csproj -c Release
dotnet run --project tests/YtecStickyNote.Tests/YtecStickyNote.Tests.csproj -c Release --no-build
dotnet build src/YtecStickyNote/YtecStickyNote.csproj -c Release
```

実データへ触れずにUIを確認する場合は、`--test-mode`を使用してください。

```powershell
dotnet run --project src/YtecStickyNote/YtecStickyNote.csproj -c Release -- --test-mode
```

## Pull request

- 変更理由と利用者への影響を説明してください。
- UI変更では、可能なら実データを含まないスクリーンショットを添付してください。
- 保存形式を変更する場合は、旧版読込、移行前バックアップ、回帰テストを含めてください。
- モニター構成・ウィンドウ位置を変更する場合は、複数構成の位置プロファイルが上書きされないテストを含めてください。
- 新しい外部通信や依存関係を追加する場合は、目的、送信する情報、ライセンス、無効化方法を記載してください。

明示しない限り、提出されたContributionにはApache License 2.0が適用されます。
