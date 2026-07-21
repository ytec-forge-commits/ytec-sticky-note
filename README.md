# Y-TEC 付箋

Windowsデスクトップの好きな位置へ置ける、キャンパスノート風のシンプルな付箋アプリです。起動直後から本文を編集でき、装飾・5種類の背景・位置保存を備えます。

## 主な機能

- 太字、斜体、下線、取り消し線、中央揃え、箇条書き
- PCにインストールされている全フォント（よく使うフォントは★付きで先頭表示）、5段階の文字サイズ、5色の文字色
- レモン、さくら、ミント、スカイ、アイボリーの5背景
- 文字の下端と自然に揃う固定行高の罫線、赤い縦罫線より右側だけを使う本文領域
- 本文、装飾、背景、ウィンドウ位置・サイズの自動保存
- 実行ファイル横の `data` フォルダーへ保存するポータブル設計
- 保存前データの `.bak` バックアップ
- 専用アプリアイコンとタスクトレイ常駐（タスクバーには表示しない）

## タスクトレイ

起動時は付箋を保存位置へ表示し、タスクバーにはボタンを出さず、通知領域へ専用アイコンを表示します。最小化または右上の×で付箋を隠し、トレイアイコンのダブルクリックで再表示できます。完全に終了するときは、トレイアイコンを右クリックして「終了」を選びます。

## 保存方式

保存先は実行ファイルと同じ場所の `data/sticky-note.json` です。JSONは暗号化せず、装飾付き本文はRTFをBase64表現で、検索しやすいプレーンテキストも同時に保存します。ファイル更新は一時ファイル経由で行い、既存データを `.bak` へ退避してから置き換えます。

USBメモリやGoogle Driveで持ち運ぶ場合は、EXEだけでなくフォルダー全体を移動してください。

## Windowsと一緒に起動する場合

このアプリ自身は、レジストリやスタートアップフォルダーを変更しません。必要な場合は `Win + R` で `shell:startup` を開き、`YTEC-Sticky-Note.exe` のショートカットを利用者自身で配置してください。職場PCでは管理者やセキュリティ製品の運用ルールを優先してください。

Google Drive上に置く場合、スタートアップフォルダーからの起動はGoogle Driveのサインイン完了を待ちません。起動順が問題になる環境では、タスクスケジューラなど職場で許可された方法で遅延起動を設定してください。

### 1.2.0以前で自動起動を有効にしていた場合

旧版が作成した登録は、新版から自動では解除しません。可能なら更新前に旧版の「自動起動」をオフにしてください。既に更新した場合は、レジストリエディターの `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` にある値 `Y-TEC Sticky Note` だけを削除し、その後 `%LOCALAPPDATA%\Y-TEC\StickyNote` を削除できます。ほかの値やフォルダーは削除しないでください。

## 開発

必要環境: Windows、.NET 10 SDK

```powershell
dotnet build src/YtecStickyNote/YtecStickyNote.csproj -c Release
dotnet run --project tests/YtecStickyNote.Tests/YtecStickyNote.Tests.csproj -c Release
dotnet run --project tests/YtecStickyNote.VisualTest/YtecStickyNote.VisualTest.csproj -c Release -- 520 620 artifacts/visual-test/520x620.png
```

実データへ触れずに画面確認する場合:

```powershell
dotnet run --project src/YtecStickyNote/YtecStickyNote.csproj -c Release -- --test-mode
```

## 配布

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package.ps1
```

`artifacts/YTEC-Sticky-Note-win-x64/` と `artifacts/YTEC-Sticky-Note-1.3.0-win-x64.zip` を生成します。既存の配布フォルダーにある `data` は残し、ZIPには個人の保存データを含めません。自己完結型のポータブルフォルダーなので、利用PCへの.NETランタイム導入は不要です。EXEだけを取り出さず、フォルダー全体を一緒に移動してください。

## 対象外

複数付箋、クラウド同期、認証、暗号化、印刷、PDF出力、画像添付、共有機能は含みません。

## ライセンスとクレジット

本ソースコードとデザインの著作権はY-TECに帰属します。アプリアイコンは本プロジェクト専用に生成・加工したオリジナルです。外部UIライブラリや外部アセットは使用していません。実行基盤としてMicrosoft .NET / WPFを使用しています。
