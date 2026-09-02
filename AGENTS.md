# 罫彩 固有作業規則

## 正本と対象

- アプリの正本は `src/YtecStickyNote`、手動テストの正本は `tests/YtecStickyNote.Tests` とする。
- Windows専用の .NET 10 / WPF アプリとして維持し、Web化やクロスプラットフォーム化は明示依頼なしに行わない。
- 機能は1ウィンドウ内の複数ページ、文字装飾、10背景、位置・サイズ保存、ローカル保存に絞る。複数ウィンドウ、タグ、クラウド同期、添付等へは明示依頼なしに拡張しない。

## 保存データ

- ポータブル版の全ページの本文・装飾・ページ別背景は実行ファイルと同じ場所の `data/sticky-note.json` が正本。Store版はWindowsのアプリ専用LocalState内の同名ファイルを正本とし、両者を自動同期しない。隣の `.bak` は直前保存のバックアップ。
- ウィンドウ位置・サイズは `data/window-state.json` にモニター構成別で最大12件保存し、`window-state.backup.json` を直前バックアップとする。
- 保存形式には `version` を持たせる。形式変更時は旧版読込、移行、失敗時の復旧を先に設計する。
- 保存形式v3では安定したページIDと現在ページIDを持つ。v1/v2の単一ページは1ページ目へ移行し、初回保存前の版別バックアップを維持する。
- 実データをテスト・スクリーンショットへ使わない。テストは専用一時フォルダーだけを使う。
- `data`、配布フォルダー、既存ZIPを一括削除しない。

## Windows機能

- 通常起動時は正しい自動起動設定を書き換えない。画面の「自動起動」を利用者が明示操作した時だけ、Run登録とローカル起動キャッシュを変更する。旧補助EXE方式を検出した場合だけ、更新または解除を利用者が選んだ後に変更する。
- ポータブル版の自動起動は、Y-TEC署名の `startup-runtime-manifest.json` / `.p7s` と、そこへ列挙したトップ階層のEXE・DLL・ランタイムJSON/DATを `%LOCALAPPDATA%\\Y-TEC\\StickyNote\\app` へコピーする。署名者、完全なファイル集合、全SHA-256をstaging・配置後・登録状態確認時に検証したローカル本体だけをRun登録から直接起動し、専用の待機・起動補助EXEは配布しない。
- 保存JSONはraw byte、ページ数、総本文量をLoad/Save双方で制限し、XAMLパッケージはBase64 decode前と展開時の項目数・単体量・総量・文書構造を制限する。超過時は切り詰めず、元ファイルを変更しない。
- Windowsサインイン時はGoogle Drive上のコードを起動せず、本文・位置データの正本だけを元のポータブル版 `data` から読み書きする。保存先と既存データが読み書き可能な状態で3秒間安定するまで最大10分待ち、タイムアウト時はエラーを表示せず終了する。
- 配布更新では既存配布フォルダーの `data` を保護し、配布ZIPへ `data` を含めない。
- 画面検証では実データへ触れないよう `--test-mode` 引数を使う。
- 付箋ウィンドウはタスクバーへ表示せず、通知領域アイコンから表示・非表示・終了を操作する。×と最小化は終了ではなく非表示とする。
- ウィンドウ位置は仮想デスクトップ座標で保存し、モニター数・配置・解像度・作業領域・拡大率から作る構成IDごとに復元する。構成変更後も実在モニターの作業領域内へ戻るよう検証する。
- 外部モニターのスリープ・切断・復帰中にWindowsが自動変更した位置とサイズは保存しない。画面構成が安定してから構成別プロファイルを復元し、利用者の明示的な移動・サイズ変更だけを保存対象とする。

## OSSと配布

- ソースコードはApache License 2.0で公開し、配布ZIPへ `LICENSE.txt`、`NOTICE.txt`、`THIRD_PARTY_NOTICES.txt` を含める。
- Microsoft Store版はPartner CenterのIdentity/Publisherを推測せず、審査後のMicrosoft署名を利用する。直接配布版はY-TEC自己署名とSHA-256を使用する。SignPath Foundation申請は不採択のため現在の配布経路に含めず、自己署名・未署名・第三者署名を同じ表現で公開しない。
- 公開工程は `build → test → package → sign → verify → final bundle → hash → publish` の順を守り、署名・hash後の成果物を書き換えない。

## コマンド

- ビルド: `dotnet build src/YtecStickyNote/YtecStickyNote.csproj -c Release`
- テスト: `dotnet run --project tests/YtecStickyNote.Tests/YtecStickyNote.Tests.csproj -c Release`
- ビジュアルテスト: `dotnet run --project tests/YtecStickyNote.VisualTest/YtecStickyNote.VisualTest.csproj -c Release -- 520 620 artifacts/visual-test/520x620.png`
- 配布: `powershell -ExecutionPolicy Bypass -File scripts/package.ps1`
- 自己署名直接配布: `powershell -ExecutionPolicy Bypass -File scripts/package-self-signed-direct.ps1`
- Store候補: `powershell -ExecutionPolicy Bypass -File scripts/package-msix.ps1 -PackageIdentityName <Partner Center値> -Publisher <Partner Center値> -CreateUpload`

完了時は、見た目確認とWindowsネイティブの保存・復元確認を分けて報告する。
