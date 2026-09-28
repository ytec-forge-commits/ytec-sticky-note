# 罫彩（けいさい）

[English](README.en.md) | 日本語

Windowsデスクトップの好きな位置へ置ける、罫線ノート風のシンプルな付箋アプリです。起動直後から本文を編集でき、装飾・10種類の背景・位置保存を備えます。個人・法人を問わず無料で利用できるWindows専用フリーソフトです。

- 公式配布ページ: https://ytec.cloudfree.jp/forge/projects/keisai/
- Microsoft Store: https://apps.microsoft.com/detail/9PB166N90KQ8
- 対応環境: Windows 10 / 11（64ビット）
- 配布形式: Microsoft Store版（推奨）／自己署名ポータブルZIP版（補助配布）
- ポータブル版のインストール: 不要（ZIPを展開して `Keisai.exe` を起動）
- 通信機能: なし
- ソースコード: https://github.com/ytec-forge-commits/ytec-sticky-note
- 現在のStore版: [1.6.0](https://apps.microsoft.com/detail/9PB166N90KQ8)
- 現在の直接配布版: [1.6.0 プレビュー版（自己署名）](https://ytec.cloudfree.jp/forge/projects/keisai/)

## 主な機能

- 太字、斜体、下線、取り消し線、中央揃え、箇条書き
- 同じウィンドウ内で切り替えられる複数ページ（ページごとに本文・装飾・背景を保存）
- Undo／Redo、現在ページ内検索、文字装飾クリア、テキストのみ貼り付け
- PCにインストールされている全フォント（よく使うフォントは★付きで先頭表示）、5段階の文字サイズ、10色の文字色
- レモン、さくら、ミント、スカイ、アイボリー、ラベンダー、ピーチ、アクア、グレー、モカの10背景
- 文字の下端と自然に揃う固定行高の罫線、赤い縦罫線より右側だけを使う本文領域
- カーソル位置・選択範囲のフォント、サイズ、文字色をツールバーへ反映
- 箇条書きの自然な折り返しと、Shift+Enterによる項目内改行
- 本文、装飾、背景の自動保存と、モニター構成ごとのウィンドウ位置・サイズ保存
- 実行ファイル横の `data` フォルダーへ保存するポータブル設計
- 保存前データの `.bak` バックアップ
- 専用アプリアイコンとタスクトレイ常駐（タスクバーには表示しない）

## タスクトレイ

起動時は付箋を保存位置へ表示し、タスクバーにはボタンを出さず、通知領域へ専用アイコンを表示します。最小化または右上の×で付箋を隠し、トレイアイコンのダブルクリックで再表示できます。完全に終了するときは、トレイアイコンを右クリックして「終了」を選びます。

## 保存方式

ポータブル版では、複数ページの本文・装飾・ページ別背景を実行ファイルと同じ場所の `data/sticky-note.json` へ保存します。Microsoft Store版はパッケージの読み取り専用領域を避け、Windowsが用意するアプリ専用LocalStateの `data/sticky-note.json` へ保存します。両版の保存データ形式は同じですが、保存場所は分離され、自動では相互コピーしません。

JSONは暗号化せず、各ページの装飾付き本文は箇条書き構造を保持するXAMLパッケージと互換用RTFをBase64表現で、検索しやすいプレーンテキストも同時に保存します。1.5.4以前の単一ページデータは、本文・装飾・背景を失わず1ページ目へ移行します。ファイル更新は一時ファイル経由で行い、既存データを `.bak` へ退避してから置き換えます。保存形式を初めて更新する時は、上書きされない版別バックアップも作成します。

ウィンドウ位置とサイズは `data/window-state.json` へ分離し、モニター数・配置・解像度・作業領域・拡大率から作る構成IDごとに最大12件保存します。自宅3画面と職場2画面などを別の位置として復元でき、直前データは `window-state.backup.json` へ保存します。1.5.1以前の共有位置は、1.5.2を最初に起動したモニター構成へ自動移行します。

外部モニターのスリープ・切断・復帰時は、画面構成が安定するまで位置保存を止めます。Windowsが一時的に移動・縮小した値で保存済み位置を上書きせず、安定後に該当するモニター構成用の位置とサイズを復元します。

USBメモリやGoogle Driveで持ち運ぶ場合は、EXEだけでなくフォルダー全体を移動してください。

## Windowsと一緒に起動する場合

Microsoft Store版ではWindows標準のStartupTaskを使用します。ポータブル版では、署名済みの罫彩本体と自己完結ランタイムを `%LOCALAPPDATA%\Y-TEC\StickyNote\app` へコピーし、そのローカルの罫彩本体をWindowsのRun登録から直接起動します。ウイルス対策ソフトで誤検知された旧 `YTEC-Sticky-Note-Startup.exe` は1.6.0の配布物に含めません。

画面下部の「自動起動」を利用者が明示的にオンにした時だけ、Y-TEC署名の実行ファイル一覧マニフェストと全EXE・DLL・ランタイムJSON/DATのSHA-256を検証してローカルキャッシュを作成し、WindowsのRun登録を設定します。通常起動時は正しい登録を書き直しません。オフ操作も利用者が行った時だけ実行します。

保存ファイルは64 MiB、ページ数は1000ページまでの安全上限を設けています。上限超過や破損を検出した場合は元データを上書きせず、編集と保存を停止して警告します。

Windowsサインイン時はGoogle Drive上のコードを起動せず、ローカルにコピー済みの罫彩本体が起動します。本文と位置情報の正本は従来どおりGoogle Drive側の `data` に置いたままです。Google Driveのプロセス有無だけでは準備完了とみなさず、保存先と既存データが実際に読み書きできる状態が3秒間安定するまで最大10分待ちます。10分を超えた場合は、その回の起動をエラー表示なしで見送ります。

罫彩を更新した場合は、新版を手動起動して「自動起動」をいったんオフ・オンにし、新しい署名済み本体をローカルキャッシュへ登録し直してください。保存データの場所と形式は変わりません。

アプリの配置場所を移動した場合は、移動後のアプリで「自動起動」を一度オンにし直してください。職場PCでは管理者やセキュリティ製品の運用ルールを優先してください。明示操作時の登録も環境によっては検知対象になる可能性があります。

旧版の待機プログラム方式を使っている場合、1.6.0の初回起動時に更新確認を表示します。［はい］で旧補助EXEの登録をローカル罫彩本体方式へ置き換え、［いいえ］で古い登録を解除します。確認への応答なしにRun登録を書き換えることはありません。

安全な更新順序は、旧版を表示して「自動起動」をオフにしてから1.6.0へ入れ替え、1.6.0を手動起動して再度オンにする方法です。先にファイルを入れ替えた場合は、Windowsを再起動する前に正規の1.6.0を手動起動し、表示される安全性更新を完了してください。

### 旧版で自動起動を有効にしていた場合

登録先の親フォルダーは引き継ぎますが、旧補助EXEは削除し、署名済みの罫彩本体をローカルへコピーする方式へ移行します。Google Drive側の本文・位置データは移動しません。

## 開発

必要環境: Windows、.NET 10 SDK

操作説明書PDFの再生成にはPythonとReportLab、アイコンの再生成にはPythonとPillowが必要です。通常のアプリビルドとテストには不要です。

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
powershell -ExecutionPolicy Bypass -File scripts/package-self-signed-direct.ps1
```

1つ目は開発確認用の未署名ZIP、2つ目はForge／GitHubへ公開する自己署名ZIP、公開鍵だけのCER、操作説明書、`SHA256SUMS.txt` を生成します。公開ZIPには個人の `data` を含めません。自己署名は改ざん検出に利用できますが、一般の認証局による身元証明ではないため、WindowsやSmartScreenの警告をなくすものではありません。利用者の証明書ストアへ自動登録する処理はありません。

Store提出用MSIXは、Partner Centerで取得したIdentityとPublisherを明示して作成します。値を推測して公開パッケージを生成しません。

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-msix.ps1 `
  -PackageIdentityName '<Partner CenterのIdentity Name>' `
  -Publisher '<Partner CenterのPublisher>' `
  -CreateUpload
```

旧版の自動起動登録との互換性を維持するため、公開ZIPには `Keisai.exe` と同じアプリを起動する `YTEC-Sticky-Note.exe` も同梱します。新規利用者には `Keisai.exe` を案内します。

## 対象外

複数付箋、クラウド同期、認証、暗号化、印刷、PDF出力、画像添付、共有機能は含みません。

## Code signing policy

Microsoft Store版は認定を通過し、Microsoft Storeの署名・更新経路で正式公開しています。Forge／GitHubの直接配布版は、Y-TECの自己署名とSHA-256を使用します。SignPath Foundation申請は不採択となったため、現在はStore版と自己署名版の2経路です。自己署名版の制約、秘密鍵を配布しない工程、将来の署名プロバイダ切替は [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md) に記載しています。

## ライセンスとクレジット

罫彩のソースコードは [Apache License 2.0](LICENSE.txt) で公開します。著作権・帰属表示は [NOTICE](NOTICE)、第三者ソフトウェアは [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)、データの取り扱いは [PRIVACY.md](PRIVACY.md) を確認してください。

アプリアイコンは本プロジェクト専用に生成・加工したオリジナルです。外部UIライブラリや外部アセットは使用していません。実行基盤としてMicrosoft .NET / WPFを使用しています。
