# Y-TEC 付箋

Windowsデスクトップの好きな位置へ置ける、キャンパスノート風のシンプルな付箋アプリです。起動直後から本文を編集でき、装飾・5種類の背景・位置保存・Windows自動起動を備えます。

## 主な機能

- 太字、斜体、下線、取り消し線、中央揃え、箇条書き
- PCにインストールされている全フォント（よく使うフォントは★付きで先頭表示）、5段階の文字サイズ、5色の文字色
- レモン、さくら、ミント、スカイ、アイボリーの5背景
- 文字の下端と自然に揃う固定行高の罫線、赤い縦罫線より右側だけを使う本文領域
- 本文、装飾、背景、ウィンドウ位置・サイズの自動保存
- 初回起動時にWindowsの自動起動を有効化（画面からオン・オフ可能）
- 実行ファイル横の `data` フォルダーへ保存するポータブル設計
- 保存前データの `.bak` バックアップ

## 保存方式

保存先は実行ファイルと同じ場所の `data/sticky-note.json` です。JSONは暗号化せず、装飾付き本文はRTFをBase64表現で、検索しやすいプレーンテキストも同時に保存します。ファイル更新は一時ファイル経由で行い、既存データを `.bak` へ退避してから置き換えます。

USBメモリやGoogle Driveで持ち運ぶ場合は、EXEだけでなくフォルダー全体を移動してください。移動後に一度手動起動すると、Windows自動起動のパスも新しい場所へ更新されます。

自動起動ではアプリ本体を直接呼び出さず、`%LOCALAPPDATA%\Y-TEC\StickyNote` に置いた小さな待機プログラムを使用します。Google Driveのプロセス有無ではなく、アプリ一式と登録時に存在した保存データを実際に読み取れ、配置先へ書き込める状態になるまで、最大10分間、エラー画面を出さずに待ってから起動します。10分を超えた場合はその回の起動を静かに見送ります。

## 開発

必要環境: Windows、.NET 10 SDK、Rust（自動起動待機プログラムのビルド用）

```powershell
dotnet build src/YtecStickyNote/YtecStickyNote.csproj -c Release
dotnet run --project tests/YtecStickyNote.Tests/YtecStickyNote.Tests.csproj -c Release
cargo test --manifest-path src/YtecStickyNote.Startup/Cargo.toml --release --locked
dotnet run --project tests/YtecStickyNote.VisualTest/YtecStickyNote.VisualTest.csproj -c Release -- 520 620 artifacts/visual-test/520x620.png
```

開発版を自動起動へ登録せず画面確認する場合:

```powershell
dotnet run --project src/YtecStickyNote/YtecStickyNote.csproj -c Release -- --test-mode
```

## 配布

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package.ps1
```

`artifacts/YTEC-Sticky-Note-win-x64/` と `artifacts/YTEC-Sticky-Note-1.1.0-win-x64.zip` を生成します。既存の配布フォルダーにある `data` は残し、ZIPには個人の保存データを含めません。自己完結型のポータブルフォルダーなので、利用PCへの.NETランタイム導入は不要です。EXEだけを取り出さず、フォルダー全体を一緒に移動してください。

## 対象外

複数付箋、クラウド同期、認証、暗号化、印刷、PDF出力、画像添付、共有機能は含みません。

## ライセンスとクレジット

本ソースコードとデザインの著作権はY-TECに帰属します。外部UIライブラリや外部アセットは使用していません。実行基盤としてMicrosoft .NET / WPFを使用しています。
