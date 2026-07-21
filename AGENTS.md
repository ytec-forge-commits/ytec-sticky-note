# Y-TEC 付箋 固有作業規則

## 正本と対象

- アプリの正本は `src/YtecStickyNote`、手動テストの正本は `tests/YtecStickyNote.Tests` とする。
- Windows専用の .NET 10 / WPF アプリとして維持し、Web化やクロスプラットフォーム化は明示依頼なしに行わない。
- 機能は1枚の付箋、文字装飾、10背景、位置・サイズ保存、ポータブル保存に絞る。

## 保存データ

- 実行ファイルと同じ場所の `data/sticky-note.json` が正本。隣の `.bak` は直前保存のバックアップ。
- 保存形式には `version` を持たせる。形式変更時は旧版読込、移行、失敗時の復旧を先に設計する。
- 実データをテスト・スクリーンショットへ使わない。テストは専用一時フォルダーだけを使う。
- `data`、配布フォルダー、既存ZIPを一括削除しない。

## Windows機能

- 通常起動時は自動起動設定を書き換えない。画面の「自動起動」を利用者が明示操作した時だけ、Run登録と待機ヘルパーを変更する。
- 自動起動は `%LOCALAPPDATA%\\Y-TEC\\StickyNote` の待機ヘルパーを使用し、Google Drive上のアプリ一式と既存データが利用可能になるまで最大10分待つ。
- 配布更新では既存配布フォルダーの `data` を保護し、配布ZIPへ `data` を含めない。
- 画面検証では実データへ触れないよう `--test-mode` 引数を使う。
- 付箋ウィンドウはタスクバーへ表示せず、通知領域アイコンから表示・非表示・終了を操作する。×と最小化は終了ではなく非表示とする。
- ウィンドウ位置は仮想デスクトップ座標で保存し、モニター構成変更後も一部が画面内へ戻るよう検証する。

## コマンド

- ビルド: `dotnet build src/YtecStickyNote/YtecStickyNote.csproj -c Release`
- テスト: `dotnet run --project tests/YtecStickyNote.Tests/YtecStickyNote.Tests.csproj -c Release`
- ビジュアルテスト: `dotnet run --project tests/YtecStickyNote.VisualTest/YtecStickyNote.VisualTest.csproj -c Release -- 520 620 artifacts/visual-test/520x620.png`
- 配布: `powershell -ExecutionPolicy Bypass -File scripts/package.ps1`

完了時は、見た目確認とWindowsネイティブの保存・復元確認を分けて報告する。
