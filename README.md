# 罫彩（けいさい）

Windowsデスクトップの好きな位置へ置ける、罫線ノート風のシンプルな付箋アプリです。起動直後から本文を編集でき、装飾・10種類の背景・位置保存を備えます。個人・法人を問わず無料で利用できるWindows専用フリーソフトです。

- 公式配布ページ: https://ytec.cloudfree.jp/ytb/keisai/
- 対応環境: Windows 10 / 11（64ビット）
- インストール: 不要（ZIPを展開して `Keisai.exe` を起動）
- 通信機能: なし

## 主な機能

- 太字、斜体、下線、取り消し線、中央揃え、箇条書き
- PCにインストールされている全フォント（よく使うフォントは★付きで先頭表示）、5段階の文字サイズ、10色の文字色
- レモン、さくら、ミント、スカイ、アイボリー、ラベンダー、ピーチ、アクア、グレー、モカの10背景
- 文字の下端と自然に揃う固定行高の罫線、赤い縦罫線より右側だけを使う本文領域
- カーソル位置・選択範囲のフォント、サイズ、文字色をツールバーへ反映
- 箇条書きの自然な折り返しと、Shift+Enterによる項目内改行
- 本文、装飾、背景、ウィンドウ位置・サイズの自動保存
- 実行ファイル横の `data` フォルダーへ保存するポータブル設計
- 保存前データの `.bak` バックアップ
- 専用アプリアイコンとタスクトレイ常駐（タスクバーには表示しない）

## タスクトレイ

起動時は付箋を保存位置へ表示し、タスクバーにはボタンを出さず、通知領域へ専用アイコンを表示します。最小化または右上の×で付箋を隠し、トレイアイコンのダブルクリックで再表示できます。完全に終了するときは、トレイアイコンを右クリックして「終了」を選びます。

## 保存方式

保存先は実行ファイルと同じ場所の `data/sticky-note.json` です。JSONは暗号化せず、装飾付き本文は箇条書き構造を保持するXAMLパッケージと互換用RTFをBase64表現で、検索しやすいプレーンテキストも同時に保存します。旧版のRTFだけのデータも読み込め、新形式を読めない場合はRTFへフォールバックします。ファイル更新は一時ファイル経由で行い、既存データを `.bak` へ退避してから置き換えます。v1から初めて移行する時は、上書きされない `sticky-note.json.v1.bak` も作成します。

USBメモリやGoogle Driveで持ち運ぶ場合は、EXEだけでなくフォルダー全体を移動してください。

## Windowsと一緒に起動する場合

画面下部の「自動起動」を利用者が明示的にオンにした時だけ、WindowsのRun登録と待機ヘルパーを設定します。通常のアプリ起動時は登録状態を読み取るだけで、既に正しく登録されている内容を書き直しません。オフ操作も利用者が行った時だけ実行します。

自動起動では `%LOCALAPPDATA%\Y-TEC\StickyNote` に置いた小さな待機ヘルパーを先に起動します。Google Driveのプロセス有無ではなく、アプリ一式と登録時に存在した保存データを実際に読み取れ、配置先へ書き込める状態になるまで最大10分待ってから起動します。10分を超えた場合は、その回の起動をエラー表示なしで見送ります。

アプリの配置場所を移動した場合は、移動後のアプリで「自動起動」を一度オンにし直してください。職場PCでは管理者やセキュリティ製品の運用ルールを優先してください。明示操作時の登録も環境によっては検知対象になる可能性があります。

### 旧版で自動起動を有効にしていた場合

旧版の登録先と待機方式は互換性があります。配置場所が同じで登録内容が有効ならチェックはオンで表示され、通常起動時に書き直しません。

## 開発

必要環境: Windows、.NET 10 SDK、Rust（待機ヘルパーのビルド用）

```powershell
dotnet build src/YtecStickyNote/YtecStickyNote.csproj -c Release
dotnet run --project tests/YtecStickyNote.Tests/YtecStickyNote.Tests.csproj -c Release
cargo test --manifest-path src/YtecStickyNote.Startup/Cargo.toml --release --locked
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

`artifacts/Keisai-win-x64/`、`artifacts/Keisai-1.5.1-win-x64.zip`、SHA-256を記載した同名の `.sha256.txt` を生成します。公開ZIPには `output/pdf/罫彩_操作説明書.pdf` も同梱します。既存の配布フォルダーにある `data` は残し、ZIPには個人の保存データを含めません。自己完結型のポータブルフォルダーなので、利用PCへの.NETランタイム導入は不要です。EXEや待機ヘルパーだけを取り出さず、フォルダー全体を一緒に移動してください。

旧版の自動起動登録との互換性を維持するため、公開ZIPには `Keisai.exe` と同じアプリを起動する `YTEC-Sticky-Note.exe` も同梱します。新規利用者には `Keisai.exe` を案内します。

## 対象外

複数付箋、クラウド同期、認証、暗号化、印刷、PDF出力、画像添付、共有機能は含みません。

## ライセンスとクレジット

本ソースコードとデザインの著作権はY-TECに帰属します。個人・法人、私的利用・業務利用を問わず無料で利用できます。詳しい条件は [LICENSE.md](LICENSE.md)、データの取り扱いは [docs/PRIVACY.txt](docs/PRIVACY.txt) を確認してください。

アプリアイコンは本プロジェクト専用に生成・加工したオリジナルです。外部UIライブラリや外部アセットは使用していません。実行基盤としてMicrosoft .NET / WPFを使用しています。
