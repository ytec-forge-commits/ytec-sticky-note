# Third-party notices

罫彩 1.6.0 は、以下の第三者ソフトウェアを実行時または生成時に利用します。直接配布ZIPとStore用MSIXには、この文書に加えて `third-party-licenses/`（MSIXでは `legal/third-party-licenses/`）を同梱し、実際に使用した.NETのライセンス本文・NOTICE・版別インベントリを保存します。

## 配布物に含まれる実行時コンポーネント

| コンポーネント | 版 | 取得元 | ライセンス | 配布範囲・通知 |
| --- | --- | --- | --- | --- |
| Microsoft .NET Runtime / WPF | 10.0.10 | [dotnet/runtime v10.0.10](https://github.com/dotnet/runtime/tree/v10.0.10) | MIT Licenseおよび同梱コンポーネント固有条件 | 自己完結型の罫彩本体へ再配布。`dotnet-10.0.10/LICENSE.txt` と `ThirdPartyNotices.txt` を同梱。 |

## ビルド時だけ使用するコンポーネント

| コンポーネント | 版 | 取得元 | ライセンス | 用途 |
| --- | --- | --- | --- | --- |
| ReportLab | 5.0.1 | [PyPI: reportlab](https://pypi.org/project/reportlab/5.0.1/) | BSD License | 操作説明書PDFの生成。アプリ・配布ZIPにはPythonパッケージ自体を同梱しない。 |
| Pillow | 12.1.1 | [PyPI: pillow](https://pypi.org/project/pillow/12.1.1/) | MIT-CMU (HPND系) | Windowsアイコン生成。アプリ・配布ZIPにはPythonパッケージ自体を同梱しない。 |

## 操作説明書PDFのYu Gothic

操作説明書はWindowsに付属する `YuGothR.ttc` と `YuGothB.ttc` の使用文字だけをPDFへサブセット埋め込みしています。元フォントファイルはソース、アプリ、ZIP、MSIXへ同梱していません。生成時に両フォントのOpenType `OS/2.fsType` が `0x0008`（Editable Embedding）であることを確認しました。

Microsoftの[Font redistribution FAQ](https://learn.microsoft.com/en-us/typography/fonts/font-faq)は、OpenType/TrueTypeの埋め込み制限に従うアプリケーションによるWindows付属フォントの文書・PDF埋め込みを認めています。埋め込み可否の判定規則は[OpenType OS/2 fsType仕様](https://learn.microsoft.com/en-us/typography/opentype/spec/os2#fstype)に従います。これはフォントファイル自体をアプリへ再配布する許可ではありません。

## 罫彩本体

Y-TECが作成した罫彩本体のソースコードはApache License 2.0で提供します。詳しくは `LICENSE.txt` と `NOTICE` を確認してください。
