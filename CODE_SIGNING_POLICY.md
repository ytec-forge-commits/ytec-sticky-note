# Code signing policy

罫彩のWindows配布物は、GitHub上の公開ソースとビルド手順からGitHub ActionsのGitHub-hosted Windows runnerで生成します。

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

## Team roles

- Committers and reviewers: [ytec-commits organization members](https://github.com/orgs/ytec-commits/people)
- Approvers: [ytec-commits organization owners](https://github.com/orgs/ytec-commits/people?query=role%3Aowner)

外部からのPull Requestは、リポジトリ管理者が内容とCI結果を確認してから取り込みます。各署名リクエストは、ytec-commits organization ownerが配布内容と検証結果を確認して承認します。

## Privacy

罫彩はネットワーク通信を行いません。入力内容と設定は実行ファイル横の `data` フォルダーへ暗号化せず保存します。詳しくは[プライバシーポリシー](PRIVACY.md)を確認してください。

## Release process

1. `main` ブランチのCIで.NETビルド、アプリテスト、Rust formatting、Rustテスト、Clippyを実行します。
2. バージョンタグからGitHub-hosted Windows runner上で署名前の実行ファイルとポータブルZIPを再ビルドし、ワークフローArtifactへ保存します。
3. SignPathのGitHub連携が利用可能な場合は、そのArtifactをSignPathへ提出します。
4. 署名済み実行ファイル、操作説明書、ライセンス、更新履歴をポータブルZIPへまとめます。
5. SHA-256を生成し、GitHub Releaseと公式紹介ページへ掲載します。

SignPath Foundationの採択前または署名サービスを利用できない場合、Releaseは未署名であることを明記し、SHA-256を掲載します。署名済みと未署名の配布物を同じ表現で公開しません。
