from __future__ import annotations

from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    BaseDocTemplate,
    Flowable,
    Frame,
    Image,
    KeepTogether,
    PageBreak,
    PageTemplate,
    Paragraph,
    Spacer,
    Table,
    TableStyle,
)


ROOT = Path(__file__).resolve().parents[1]
OUTPUT_DIR = ROOT / "output" / "pdf"
OUTPUT_PATH = OUTPUT_DIR / "罫彩_操作説明書.pdf"
SCREENSHOT_PATH = ROOT / "artifacts" / "visual-test" / "keisai-1.5.0-sakura-520x620.png"
ICON_PATH = ROOT / "src" / "YtecStickyNote" / "Assets" / "app-icon.png"

FONT_REGULAR = Path(r"C:\Windows\Fonts\YuGothR.ttc")
FONT_BOLD = Path(r"C:\Windows\Fonts\YuGothB.ttc")

NAVY = colors.HexColor("#0A2540")
BLUE = colors.HexColor("#155B8D")
PALE_BLUE = colors.HexColor("#EAF5FC")
YELLOW = colors.HexColor("#FFD84D")
PALE_YELLOW = colors.HexColor("#FFF8D9")
SAKURA = colors.HexColor("#FFF0F5")
PINK = colors.HexColor("#E76891")
INK = colors.HexColor("#203241")
MUTED = colors.HexColor("#526A7A")
LINE = colors.HexColor("#C9DCE8")
WHITE = colors.white


def register_fonts() -> None:
    missing = [str(path) for path in (FONT_REGULAR, FONT_BOLD) if not path.exists()]
    if missing:
        raise FileNotFoundError(f"操作説明書に必要なフォントが見つかりません: {', '.join(missing)}")

    pdfmetrics.registerFont(TTFont("YuGothic", str(FONT_REGULAR)))
    pdfmetrics.registerFont(TTFont("YuGothic-Bold", str(FONT_BOLD)))
    pdfmetrics.registerFontFamily(
        "YuGothic",
        normal="YuGothic",
        bold="YuGothic-Bold",
        italic="YuGothic",
        boldItalic="YuGothic-Bold",
    )


def create_styles() -> dict[str, ParagraphStyle]:
    sample = getSampleStyleSheet()
    base = dict(
        wordWrap="CJK",
        splitLongWords=True,
        allowWidows=0,
        allowOrphans=0,
    )
    return {
        "cover_kicker": ParagraphStyle(
            "CoverKicker",
            parent=sample["Normal"],
            **base,
            fontName="YuGothic-Bold",
            fontSize=11,
            leading=16,
            alignment=TA_CENTER,
            textColor=BLUE,
        ),
        "cover_title": ParagraphStyle(
            "CoverTitle",
            parent=sample["Title"],
            **base,
            fontName="YuGothic-Bold",
            fontSize=34,
            leading=42,
            alignment=TA_CENTER,
            textColor=NAVY,
            spaceAfter=4 * mm,
        ),
        "cover_subtitle": ParagraphStyle(
            "CoverSubtitle",
            parent=sample["Normal"],
            **base,
            fontName="YuGothic",
            fontSize=13,
            leading=22,
            alignment=TA_CENTER,
            textColor=MUTED,
        ),
        "page_title": ParagraphStyle(
            "PageTitle",
            parent=sample["Heading1"],
            **base,
            fontName="YuGothic-Bold",
            fontSize=21,
            leading=28,
            textColor=NAVY,
            spaceAfter=5 * mm,
        ),
        "section": ParagraphStyle(
            "Section",
            parent=sample["Heading2"],
            **base,
            fontName="YuGothic-Bold",
            fontSize=13,
            leading=19,
            textColor=BLUE,
            spaceBefore=3 * mm,
            spaceAfter=2 * mm,
        ),
        "body": ParagraphStyle(
            "Body",
            parent=sample["BodyText"],
            **base,
            fontName="YuGothic",
            textColor=INK,
            fontSize=9.5,
            leading=16,
            spaceAfter=2 * mm,
        ),
        "small": ParagraphStyle(
            "Small",
            parent=sample["BodyText"],
            **base,
            fontName="YuGothic",
            fontSize=8.1,
            leading=13,
            textColor=MUTED,
        ),
        "card_title": ParagraphStyle(
            "CardTitle",
            parent=sample["Heading3"],
            **base,
            fontName="YuGothic-Bold",
            fontSize=11,
            leading=16,
            textColor=NAVY,
            spaceAfter=1.5 * mm,
        ),
        "card_body": ParagraphStyle(
            "CardBody",
            parent=sample["BodyText"],
            **base,
            fontName="YuGothic",
            textColor=INK,
            fontSize=8.7,
            leading=14,
        ),
        "number": ParagraphStyle(
            "Number",
            parent=sample["Normal"],
            **base,
            fontName="YuGothic-Bold",
            fontSize=10,
            leading=14,
            alignment=TA_CENTER,
            textColor=NAVY,
        ),
        "label": ParagraphStyle(
            "Label",
            parent=sample["Normal"],
            **base,
            fontName="YuGothic-Bold",
            fontSize=8,
            leading=11,
            textColor=BLUE,
        ),
        "button": ParagraphStyle(
            "Button",
            parent=sample["Normal"],
            **base,
            fontName="YuGothic-Bold",
            fontSize=8.5,
            leading=12,
            alignment=TA_CENTER,
            textColor=NAVY,
        ),
        "toc": ParagraphStyle(
            "Toc",
            parent=sample["BodyText"],
            **base,
            fontName="YuGothic",
            textColor=INK,
            fontSize=9,
            leading=16,
        ),
    }


class LinedPaper(Flowable):
    def __init__(self, width: float, height: float) -> None:
        super().__init__()
        self.width = width
        self.height = height

    def draw(self) -> None:
        canvas = self.canv
        canvas.saveState()
        canvas.setFillColor(PALE_YELLOW)
        canvas.roundRect(0, 0, self.width, self.height, 5 * mm, fill=1, stroke=0)
        canvas.setStrokeColor(colors.HexColor("#D5DFE6"))
        canvas.setLineWidth(0.45)
        y = self.height - 12 * mm
        while y > 8 * mm:
            canvas.line(8 * mm, y, self.width - 8 * mm, y)
            y -= 8 * mm
        canvas.setStrokeColor(PINK)
        canvas.setLineWidth(1)
        canvas.line(23 * mm, 8 * mm, 23 * mm, self.height - 8 * mm)
        canvas.restoreState()


def fit_image(path: Path, max_width: float, max_height: float) -> Image:
    image = Image(str(path))
    scale = min(max_width / image.imageWidth, max_height / image.imageHeight)
    image.drawWidth = image.imageWidth * scale
    image.drawHeight = image.imageHeight * scale
    return image


def card(title: str, body: str, styles: dict[str, ParagraphStyle], background=WHITE) -> Table:
    content = [
        Paragraph(title, styles["card_title"]),
        Paragraph(body, styles["card_body"]),
    ]
    table = Table([[content]], colWidths=[80 * mm])
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), background),
                ("BOX", (0, 0), (-1, -1), 0.6, LINE),
                ("LEFTPADDING", (0, 0), (-1, -1), 4 * mm),
                ("RIGHTPADDING", (0, 0), (-1, -1), 4 * mm),
                ("TOPPADDING", (0, 0), (-1, -1), 3.5 * mm),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 3.5 * mm),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
            ]
        )
    )
    return table


def two_cards(
    left_title: str,
    left_body: str,
    right_title: str,
    right_body: str,
    styles: dict[str, ParagraphStyle],
    left_background=WHITE,
    right_background=WHITE,
) -> Table:
    table = Table(
        [[card(left_title, left_body, styles, left_background), card(right_title, right_body, styles, right_background)]],
        colWidths=[84 * mm, 84 * mm],
        hAlign="LEFT",
    )
    table.setStyle(
        TableStyle(
            [
                ("LEFTPADDING", (0, 0), (-1, -1), 0),
                ("RIGHTPADDING", (0, 0), (-1, -1), 0),
                ("TOPPADDING", (0, 0), (-1, -1), 0),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 0),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
            ]
        )
    )
    return table


def step_row(
    number: int,
    title: str,
    body: str,
    styles: dict[str, ParagraphStyle],
    text_width: float = 152 * mm,
) -> Table:
    number_cell = Table([[Paragraph(str(number), styles["number"])]], colWidths=[9 * mm], rowHeights=[9 * mm])
    number_cell.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), YELLOW),
                ("BOX", (0, 0), (-1, -1), 0, YELLOW),
                ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
                ("LEFTPADDING", (0, 0), (-1, -1), 0),
                ("RIGHTPADDING", (0, 0), (-1, -1), 0),
                ("TOPPADDING", (0, 0), (-1, -1), 0),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 0),
            ]
        )
    )
    text = [Paragraph(title, styles["card_title"]), Paragraph(body, styles["card_body"])]
    row = Table([[number_cell, text]], colWidths=[13 * mm, text_width])
    row.setStyle(
        TableStyle(
            [
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 0),
                ("RIGHTPADDING", (0, 0), (-1, -1), 0),
                ("TOPPADDING", (0, 0), (-1, -1), 1.5 * mm),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 2.5 * mm),
            ]
        )
    )
    return row


def bullet(text: str, styles: dict[str, ParagraphStyle], color=BLUE) -> Table:
    dot = Table([[""]], colWidths=[2.5 * mm], rowHeights=[2.5 * mm])
    dot.setStyle(TableStyle([("BACKGROUND", (0, 0), (-1, -1), color)]))
    row = Table([[dot, Paragraph(text, styles["body"])]], colWidths=[7 * mm, 160 * mm])
    row.setStyle(
        TableStyle(
            [
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 0),
                ("RIGHTPADDING", (0, 0), (-1, -1), 0),
                ("TOPPADDING", (0, 0), (0, 0), 4.2),
                ("TOPPADDING", (1, 0), (1, 0), 0),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 0),
            ]
        )
    )
    return row


def note_box(title: str, body: str, styles: dict[str, ParagraphStyle], warning: bool = False) -> Table:
    accent = PINK if warning else BLUE
    background = SAKURA if warning else PALE_BLUE
    table = Table(
        [[Paragraph(title, styles["card_title"]), Paragraph(body, styles["card_body"])]],
        colWidths=[35 * mm, 130 * mm],
    )
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), background),
                ("LINEBEFORE", (0, 0), (0, -1), 3, accent),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 4 * mm),
                ("RIGHTPADDING", (0, 0), (-1, -1), 4 * mm),
                ("TOPPADDING", (0, 0), (-1, -1), 3 * mm),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 3 * mm),
            ]
        )
    )
    return table


def toolbar_table(styles: dict[str, ParagraphStyle]) -> Table:
    labels = ["太字", "斜体", "下線", "取消線", "中央", "箇条書き", "フォント", "サイズ", "文字色"]
    values = ["B", "/", "U", "S", "=", "・", "Yu Gothic UI", "14", "黒"]
    widths = [14, 14, 14, 17, 17, 22, 34, 18, 20]
    table = Table(
        [
            [Paragraph(label, styles["label"]) for label in labels],
            [Paragraph(value, styles["button"]) for value in values],
        ],
        colWidths=[width * mm for width in widths],
        rowHeights=[7 * mm, 10 * mm],
    )
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), PALE_BLUE),
                ("BACKGROUND", (0, 1), (-1, 1), WHITE),
                ("GRID", (0, 0), (-1, -1), 0.5, LINE),
                ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
                ("LEFTPADDING", (0, 0), (-1, -1), 1 * mm),
                ("RIGHTPADDING", (0, 0), (-1, -1), 1 * mm),
                ("TOPPADDING", (0, 0), (-1, -1), 0),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 0),
            ]
        )
    )
    return table


def draw_page(canvas, doc) -> None:
    page = canvas.getPageNumber()
    width, height = A4
    canvas.saveState()
    canvas.setFillColor(WHITE)
    canvas.rect(0, 0, width, height, fill=1, stroke=0)
    canvas.setFillColor(YELLOW)
    canvas.rect(0, height - 5 * mm, width, 5 * mm, fill=1, stroke=0)
    canvas.setFillColor(NAVY)
    canvas.setFont("YuGothic-Bold", 7.5)
    canvas.drawString(20 * mm, 11 * mm, "罫彩 操作説明書")
    canvas.setFont("YuGothic", 7.5)
    canvas.setFillColor(MUTED)
    canvas.drawRightString(width - 20 * mm, 11 * mm, f"1.5.0  |  {page}")
    canvas.setStrokeColor(LINE)
    canvas.setLineWidth(0.4)
    canvas.line(20 * mm, 16 * mm, width - 20 * mm, 16 * mm)
    canvas.restoreState()


def build_story(styles: dict[str, ParagraphStyle]) -> list:
    story: list = []

    # 1. Cover and quick start
    story.append(Spacer(1, 11 * mm))
    cover_icon = fit_image(ICON_PATH, 27 * mm, 27 * mm)
    icon_table = Table([[cover_icon]], colWidths=[170 * mm])
    icon_table.setStyle(TableStyle([("ALIGN", (0, 0), (-1, -1), "CENTER")]))
    story.append(icon_table)
    story.append(Spacer(1, 4 * mm))
    story.append(Paragraph("WINDOWS専用フリーソフト", styles["cover_kicker"]))
    story.append(Paragraph("罫彩", styles["cover_title"]))
    story.append(Paragraph("ノートの書き心地と、デスクトップ付箋の手軽さをひとつに。", styles["cover_subtitle"]))
    story.append(Spacer(1, 8 * mm))

    quick_start = [
        Paragraph("3分で使い始める", styles["card_title"]),
        step_row(1, "ZIPを展開する", "ダウンロードしたZIPを右クリックし、［すべて展開］を選びます。ZIPの中から直接起動しないでください。", styles),
        step_row(2, "Keisai.exe を起動する", "展開したフォルダー内の Keisai.exe をダブルクリックします。インストール作業はありません。", styles),
        step_row(3, "文章を入力する", "赤い縦罫線の右側をクリックして入力します。内容や装飾は自動で保存されます。", styles),
    ]
    quick_table = Table([[quick_start]], colWidths=[169 * mm])
    quick_table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), PALE_YELLOW),
                ("BOX", (0, 0), (-1, -1), 0.8, YELLOW),
                ("LEFTPADDING", (0, 0), (-1, -1), 6 * mm),
                ("RIGHTPADDING", (0, 0), (-1, -1), 6 * mm),
                ("TOPPADDING", (0, 0), (-1, -1), 5 * mm),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 4 * mm),
            ]
        )
    )
    story.append(quick_table)
    story.append(Spacer(1, 6 * mm))
    story.append(
        note_box(
            "最初の起動時",
            "Windowsの保護画面が表示された場合は、配布ページのSHA-256とZIPの値を確認してください。本アプリはデジタル署名されていません。",
            styles,
            warning=True,
        )
    )
    story.append(Spacer(1, 6 * mm))
    story.append(Paragraph("対応環境: Windows 10 / 11（64-bit）　　発行: 2026年7月22日", styles["small"]))
    story.append(PageBreak())

    # 2. Screen overview
    story.append(Paragraph("画面の見かた", styles["page_title"]))
    screenshot = fit_image(SCREENSHOT_PATH, 88 * mm, 126 * mm)
    screenshot_table = Table([[screenshot]], colWidths=[92 * mm])
    screenshot_table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), colors.HexColor("#304757")),
                ("BOX", (0, 0), (-1, -1), 0.6, NAVY),
                ("LEFTPADDING", (0, 0), (-1, -1), 2 * mm),
                ("RIGHTPADDING", (0, 0), (-1, -1), 2 * mm),
                ("TOPPADDING", (0, 0), (-1, -1), 2 * mm),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 2 * mm),
                ("ALIGN", (0, 0), (-1, -1), "CENTER"),
            ]
        )
    )
    descriptions = [
        step_row(1, "タイトルバー", "ドラッグで好きな場所へ移動できます。位置とウィンドウサイズは自動保存されます。", styles, 55 * mm),
        step_row(2, "書式ツールバー", "フォント、サイズ、色、太字などを選択範囲や現在位置へ適用します。", styles, 55 * mm),
        step_row(3, "ノート本文", "文字の下端が罫線へ自然に合い、赤い縦罫線より右側へ入力されます。", styles, 55 * mm),
        step_row(4, "背景と自動起動", "10種類の背景から選択できます。自動起動は必要なときだけ明示的に有効化します。", styles, 55 * mm),
        step_row(5, "サイズ変更", "右下をドラッグして、付箋を見やすい大きさへ変更できます。", styles, 55 * mm),
    ]
    overview = Table([[screenshot_table, descriptions]], colWidths=[96 * mm, 72 * mm])
    overview.setStyle(
        TableStyle(
            [
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 0),
                ("RIGHTPADDING", (0, 0), (-1, -1), 0),
                ("TOPPADDING", (0, 0), (-1, -1), 0),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 0),
            ]
        )
    )
    story.append(overview)
    story.append(Spacer(1, 5 * mm))
    story.append(
        note_box(
            "閉じる・最小化",
            "［×］や最小化を押すと、アプリは終了せずタスクトレイへ隠れます。完全な終了方法は5ページをご覧ください。",
            styles,
        )
    )
    story.append(PageBreak())

    # 3. Editing
    story.append(Paragraph("文章を書く・整える", styles["page_title"]))
    story.append(Paragraph("書式ツールバー", styles["section"]))
    story.append(toolbar_table(styles))
    story.append(Spacer(1, 4 * mm))
    story.append(
        two_cards(
            "選択した文字を変更",
            "文字をドラッグして選択し、フォント・サイズ・色または装飾ボタンを選びます。",
            "これから入力する文字を変更",
            "選択範囲がない状態で書式を選ぶと、カーソル位置から入力する文字へ反映されます。",
            styles,
            PALE_BLUE,
            PALE_YELLOW,
        )
    )
    story.append(Paragraph("カーソル位置の書式がツールバーに反映", styles["section"]))
    story.append(
        Paragraph(
            "カーソルを移動したり文字を選択したりすると、その位置のフォント・サイズ・文字色・装飾状態がツールバーへ表示されます。続けて別の範囲を同じサイズや色へ変更するときも、現在の状態を確認してから操作できます。",
            styles["body"],
        )
    )
    story.append(Paragraph("フォントを選ぶ", styles["section"]))
    story.append(bullet("PCにインストールされているフォントを一覧から選べます。", styles))
    story.append(bullet("よく使うフォントには星印を付けられ、一覧の上部へ表示できます。", styles))
    story.append(bullet("別のPCに同じフォントがない場合、表示が置き換わることがあります。", styles, PINK))
    story.append(Paragraph("中央揃え", styles["section"]))
    story.append(Paragraph("中央揃えにしたい段落へカーソルを置くか、複数段落を選択して［中央］ボタンを押します。もう一度押すと解除できます。", styles["body"]))
    story.append(Paragraph("箇条書き", styles["section"]))
    story.append(bullet("項目にしたい段落へカーソルを置き、［箇条書き］ボタンを押します。", styles))
    story.append(bullet("Enterで次の項目を作成します。空の項目でEnterを押すと箇条書きを終了します。", styles))
    story.append(bullet("長い項目は2行目以降も文字の開始位置へ自然に揃います。", styles))
    story.append(bullet("同じ項目の中だけで改行したいときは Shift + Enter を押します。", styles))
    story.append(Spacer(1, 3 * mm))
    story.append(note_box("取り消し", "入力や書式変更を戻すときは Ctrl + Z、やり直すときは Ctrl + Y を使えます。", styles))
    story.append(PageBreak())

    # 4. Background and persistence
    story.append(Paragraph("背景・保存・持ち運び", styles["page_title"]))
    story.append(Paragraph("10種類の背景", styles["section"]))
    swatches = [
        ("レモン", "#FFF7B8"),
        ("さくら", "#FBE3EA"),
        ("ミント", "#DFF5E8"),
        ("空", "#E1F2FB"),
        ("アイボリー", "#F8F2DF"),
        ("ラベンダー", "#EEE5F8"),
        ("ピーチ", "#FBE7D8"),
        ("アクア", "#DDF4F4"),
        ("グレー", "#E8ECEF"),
        ("モカ", "#E8DFD6"),
    ]
    swatch_cells = []
    for name, color in swatches:
        cell = Table([[""], [Paragraph(name, styles["button"])]], colWidths=[31.5 * mm], rowHeights=[12 * mm, 7 * mm])
        cell.setStyle(
            TableStyle(
                [
                    ("BACKGROUND", (0, 0), (0, 0), colors.HexColor(color)),
                    ("BOX", (0, 0), (-1, -1), 0.5, LINE),
                    ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
                    ("TOPPADDING", (0, 0), (-1, -1), 0),
                    ("BOTTOMPADDING", (0, 0), (-1, -1), 0),
                ]
            )
        )
        swatch_cells.append(cell)
    swatch_table = Table([swatch_cells[:5], swatch_cells[5:]], colWidths=[33.5 * mm] * 5)
    swatch_table.setStyle(
        TableStyle(
            [
                ("LEFTPADDING", (0, 0), (-1, -1), 0),
                ("RIGHTPADDING", (0, 0), (-1, -1), 0),
                ("TOPPADDING", (0, 0), (-1, -1), 1.5 * mm),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 1.5 * mm),
                ("ALIGN", (0, 0), (-1, -1), "CENTER"),
            ]
        )
    )
    story.append(swatch_table)
    story.append(Paragraph("保存は自動", styles["section"]))
    story.append(
        Paragraph(
            "本文、文字装飾、背景、ウィンドウの位置と大きさは自動保存されます。保存先はアプリと同じフォルダー内の data フォルダーです。通常は保存操作を意識する必要がありません。",
            styles["body"],
        )
    )
    story.append(
        two_cards(
            "主な保存ファイル",
            "data/sticky-note.json<br/>data/sticky-note.json.bak",
            "バックアップ",
            "大切な内容は、アプリを終了してから data フォルダーごと別の場所へコピーしてください。",
            styles,
            PALE_BLUE,
            PALE_YELLOW,
        )
    )
    story.append(Paragraph("USBメモリやGoogle Driveで持ち運ぶ", styles["section"]))
    story.append(step_row(1, "罫彩を完全に終了する", "タスクトレイのアイコンを右クリックし、［終了］を選びます。", styles))
    story.append(step_row(2, "フォルダーごと移動・コピーする", "Keisai.exe だけでなく、展開した罫彩のフォルダー全体を移動します。", styles))
    story.append(step_row(3, "移動先から起動する", "移動先の Keisai.exe を起動すると、同じ data フォルダーの内容を読み込みます。", styles))
    story.append(
        note_box(
            "同時起動に注意",
            "同じGoogle Drive上の罫彩を複数PCで同時に起動しないでください。競合コピーや保存内容の上書きにつながることがあります。",
            styles,
            warning=True,
        )
    )
    story.append(PageBreak())

    # 5. Tray and autostart
    story.append(Paragraph("タスクトレイ・自動起動", styles["page_title"]))
    story.append(Paragraph("タスクトレイで常駐", styles["section"]))
    icon = fit_image(ICON_PATH, 18 * mm, 18 * mm)
    tray_table = Table(
        [[icon, [
            Paragraph("表示する", styles["card_title"]),
            Paragraph("通知領域（タスクトレイ）の罫彩アイコンをダブルクリックします。", styles["card_body"]),
            Spacer(1, 2 * mm),
            Paragraph("完全に終了する", styles["card_title"]),
            Paragraph("罫彩アイコンを右クリックし、［終了］を選びます。", styles["card_body"]),
        ]]],
        colWidths=[27 * mm, 138 * mm],
    )
    tray_table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), PALE_YELLOW),
                ("BOX", (0, 0), (-1, -1), 0.7, YELLOW),
                ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
                ("LEFTPADDING", (0, 0), (-1, -1), 5 * mm),
                ("RIGHTPADDING", (0, 0), (-1, -1), 5 * mm),
                ("TOPPADDING", (0, 0), (-1, -1), 4 * mm),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 4 * mm),
            ]
        )
    )
    story.append(tray_table)
    story.append(Paragraph("Windows起動時に自動起動", styles["section"]))
    story.append(
        Paragraph(
            "画面下部の［自動起動］へチェックを入れると、Windowsへのサインイン後に罫彩を起動する登録を行います。登録は利用者が明示的にチェックしたときだけ実行されます。解除するときはチェックを外します。",
            styles["body"],
        )
    )
    story.append(step_row(1, "罫彩を使いたい場所へ置く", "USBメモリではなく、普段Windows起動時に接続される場所を推奨します。", styles))
    story.append(step_row(2, "［自動起動］へチェックを入れる", "確認画面の内容を読み、登録を実行します。", styles))
    story.append(step_row(3, "移動したら登録し直す", "アプリのフォルダーを移動した場合は、いったん解除してから新しい場所で再登録してください。", styles))
    story.append(Paragraph("Google Drive上から自動起動する場合", styles["section"]))
    story.append(
        Paragraph(
            "自動起動用の補助処理は、Google Driveのサインインと同期準備が終わり、アプリのファイルを読み書きできる状態になるまで待機します。待機は最大10分です。PCやネットワークの状態によっては起動まで時間がかかります。",
            styles["body"],
        )
    )
    story.append(
        note_box(
            "セキュリティ製品",
            "自動起動登録はWindowsの設定を変更するため、セキュリティ製品が確認や警告を表示する場合があります。許可できない環境では自動起動を使わず、手動起動または職場の管理者が指定する方法をご利用ください。",
            styles,
            warning=True,
        )
    )
    story.append(PageBreak())

    # 6. Troubleshooting and specifications
    story.append(Paragraph("困ったときは", styles["page_title"]))
    troubleshooting = [
        ("起動しても画面が見えない", "タスクトレイの罫彩アイコンをダブルクリックしてください。別モニターを外した直後は、タスクバーから再表示してウィンドウを移動してください。"),
        ("保存した文章が見つからない", "Keisai.exe だけを別の場所へ移していないか確認してください。保存データはアプリと同じフォルダー内の data にあります。"),
        ("Google Driveから自動起動しない", "サインインと同期が完了しているか、ファイルをローカルで利用できるか確認してください。10分を超えた場合は手動で起動してください。"),
        ("フォントの見た目が変わった", "使用したフォントが現在のPCへインストールされているか確認してください。別PCにないフォントは代替表示になることがあります。"),
        ("自動起動を解除したい", "罫彩を表示して［自動起動］のチェックを外します。アプリを移動・削除する前に解除してください。"),
    ]
    for question, answer in troubleshooting:
        table = Table(
            [[Paragraph(question, styles["card_title"]), Paragraph(answer, styles["card_body"])]],
            colWidths=[52 * mm, 113 * mm],
        )
        table.setStyle(
            TableStyle(
                [
                    ("BACKGROUND", (0, 0), (0, 0), PALE_BLUE),
                    ("BACKGROUND", (1, 0), (1, 0), WHITE),
                    ("BOX", (0, 0), (-1, -1), 0.5, LINE),
                    ("INNERGRID", (0, 0), (-1, -1), 0.5, LINE),
                    ("VALIGN", (0, 0), (-1, -1), "TOP"),
                    ("LEFTPADDING", (0, 0), (-1, -1), 4 * mm),
                    ("RIGHTPADDING", (0, 0), (-1, -1), 4 * mm),
                    ("TOPPADDING", (0, 0), (-1, -1), 3 * mm),
                    ("BOTTOMPADDING", (0, 0), (-1, -1), 3 * mm),
                ]
            )
        )
        story.append(table)
        story.append(Spacer(1, 2 * mm))

    story.append(Paragraph("データとプライバシー", styles["section"]))
    story.append(
        Paragraph(
            "罫彩には外部通信、アクセス解析、広告、認証、クラウド同期はありません。入力内容は暗号化せず、アプリと同じフォルダー内へ保存します。機密情報の保管には使用せず、必要に応じてフォルダーごとバックアップしてください。",
            styles["body"],
        )
    )
    story.append(Paragraph("搭載していない機能", styles["section"]))
    story.append(Paragraph("複数付箋、印刷、PDF出力、画像添付、共有、アカウント機能は搭載していません。", styles["body"]))
    story.append(Spacer(1, 3 * mm))
    story.append(
        note_box(
            "配布・更新情報",
            "最新版、ダウンロード、SHA-256は公式ページで確認できます。<br/><link href='https://ytec.cloudfree.jp/ytb/keisai/' color='#155B8D'>https://ytec.cloudfree.jp/ytb/keisai/</link>",
            styles,
        )
    )
    story.append(Spacer(1, 6 * mm))
    story.append(Paragraph("罫彩 1.5.0　操作説明書", styles["cover_kicker"]))

    return story


def main() -> None:
    register_fonts()
    styles = create_styles()

    for required in (SCREENSHOT_PATH, ICON_PATH):
        if not required.exists():
            raise FileNotFoundError(f"操作説明書に必要な画像が見つかりません: {required}")

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    document = BaseDocTemplate(
        str(OUTPUT_PATH),
        pagesize=A4,
        leftMargin=20 * mm,
        rightMargin=20 * mm,
        topMargin=17 * mm,
        bottomMargin=21 * mm,
        title="罫彩 操作説明書",
        author="Y-TEC",
        subject="Windows専用フリーソフト 罫彩 1.5.0 の操作説明書",
        creator="Y-TEC",
    )
    frame = Frame(
        document.leftMargin,
        document.bottomMargin,
        document.width,
        document.height,
        id="manual-frame",
        leftPadding=0,
        rightPadding=0,
        topPadding=0,
        bottomPadding=0,
    )
    document.addPageTemplates([PageTemplate(id="manual", frames=[frame], onPage=draw_page)])
    document.build(build_story(styles))
    print(OUTPUT_PATH)


if __name__ == "__main__":
    main()
