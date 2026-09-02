# Code signing policy

罫彩は、Microsoft Storeと直接ダウンロードで署名経路を分けます。どちらも同じ基準versionのソースから作成し、配布物へ利用者データや秘密鍵を含めません。

## Microsoft Store版

- Store用MSIXは、Partner Centerで取得したPackage Identity NameとPublisherをそのままmanifestへ使用します。値を推測しません。
- Store提出用MSIXへY-TECの直接配布用自己署名を流用しません。
- 審査通過後の公開パッケージはMicrosoft Storeが署名し、Storeの更新機構を利用します。
- 保存先はWindowsのパッケージLocalStateです。読み取り専用のインストール領域へ保存しません。
- 自動起動はmanifestで宣言したWindows StartupTaskを使用し、ポータブル版のRun登録やローカルキャッシュは使用しません。

## Forge／GitHub直接配布版

SignPath Foundationの審査結果が出るまで、直接配布するポータブル版はY-TECの自己署名を使用します。

- 秘密鍵は現在のWindowsユーザーの証明書ストアで非exportableとして生成・保持します。
- `.pfx`、`.p12`、秘密鍵、パスワードをWorkspace、Git、CI Artifact、ZIP、Forge、GitHub Releaseへ保存しません。
- 署名対象は、公開ZIPへ入るY-TEC製の最終EXE／DLLだけです。Microsoftや.NETの第三者バイナリを再署名しません。
- SHA-256とRFC 3161タイムスタンプを使用し、署名者、改ざん有無、タイムスタンプを検証してからZIP化します。
- 公開するCERは公開鍵だけを含む検証補助物です。罫彩は利用者のTrusted RootやTrusted Peopleへ証明書を自動登録しません。
- 自己署名は一般の認証局による身元証明ではなく、WindowsやSmartScreenの警告を回避・保証するものではありません。この制約をForgeとGitHub Releaseへ明記します。
- 最終ZIP、操作説明書、公開CERごとにSHA-256を生成し、`SHA256SUMS.txt` として同時公開します。

## リリース工程

1. ソースをビルドし、.NET／UI／回帰テストを実行します。
2. 利用者データを含まない署名前のポータブルZIPとStore用MSIXを作成し、内容を検証します。
3. 直接配布版だけを隔離したstagingへ展開し、Y-TEC製EXE／DLLへ自己署名とタイムスタンプを付与します。
4. 全署名を検証してから最終ZIPを作成します。
5. 最終ZIP、操作説明書、公開CERのSHA-256を生成します。hash生成後に成果物を書き換えません。
6. frozen candidateの最終確認後、同じ成果物をForgeとGitHub Releaseへ公開します。
7. Store版はPartner Centerへ提出し、Microsoftの認定・署名・公開工程を利用します。

GitHub Actionsは秘密鍵を持たないため、ソース検証と未署名candidateの作成までを担当します。自己署名版は管理されたローカルRelease工程で作成します。

## SignPath Foundation

SignPath Foundationの採択後は、公開ソース、CI Artifact、承認者による署名経路へ移行できます。移行時は自己署名版とSignPath署名版を混同せず、署名プロバイダ、検証結果、配布物の対応をRelease notesへ明記します。

Free code signing application: [SignPath.io](https://about.signpath.io/) / [SignPath Foundation](https://signpath.org/)
