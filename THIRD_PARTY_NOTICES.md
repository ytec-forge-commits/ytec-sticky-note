# Third-party notices

罫彩は、次のオープンソースソフトウェアを実行基盤またはビルド環境として利用しています。

## 実行時の主な依存関係

- Microsoft .NET Runtime / WPF — MIT Licenseおよび各コンポーネントに付属するライセンス
- Rust standard library — Apache License 2.0 OR MIT License

## ビルド時の依存関係

- winresource — MIT License（Windows実行ファイルの製品名・版数・アイコン設定に使用）
- version_check — MIT OR Apache License 2.0（winresourceの推移的ビルド依存関係）
- ReportLab — BSD License（操作説明書PDFの生成に使用）
- Pillow — HPND License（Windowsアイコンの生成に使用）

自己完結型のWindows配布物には、Microsoft .NET Runtimeの推移的依存関係が含まれます。正確なコンポーネントとライセンス・著作権表示は、.NET Runtime配布元のThird-Party Noticesを参照してください。

罫彩本体はApache License 2.0で提供します。詳しくは `LICENSE.txt` と `NOTICE` を確認してください。
