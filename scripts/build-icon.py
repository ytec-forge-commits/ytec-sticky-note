from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


ICON_SIZES = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)]


def main() -> None:
    parser = argparse.ArgumentParser(description="Y-TEC 付箋のPNGをWindows用ICOへ変換します。")
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--preview-dir", type=Path)
    args = parser.parse_args()

    image = Image.open(args.input).convert("RGBA")
    bounding_box = image.getchannel("A").getbbox()
    if bounding_box is None:
        raise RuntimeError("アイコン画像に不透明な領域がありません。")

    subject = image.crop(bounding_box)
    padding = round(max(subject.size) * 0.06)
    canvas_side = max(subject.size) + (padding * 2)
    canvas = Image.new("RGBA", (canvas_side, canvas_side), (0, 0, 0, 0))
    canvas.alpha_composite(
        subject,
        ((canvas_side - subject.width) // 2, (canvas_side - subject.height) // 2),
    )
    master = canvas.resize((1024, 1024), Image.Resampling.LANCZOS)

    args.input.parent.mkdir(parents=True, exist_ok=True)
    master.save(args.input, optimize=True)
    master.save(args.output, format="ICO", sizes=ICON_SIZES)

    if args.preview_dir:
        args.preview_dir.mkdir(parents=True, exist_ok=True)
        for size in (16, 32, 64):
            master.resize((size, size), Image.Resampling.LANCZOS).save(
                args.preview_dir / f"app-icon-{size}.png"
            )

    with Image.open(args.output) as icon:
        actual_sizes = sorted(icon.ico.sizes())
    transparent_corners = all(
        master.getpixel(point)[3] == 0
        for point in ((0, 0), (1023, 0), (0, 1023), (1023, 1023))
    )
    opaque_green_pixels = sum(
        1
        for red, green, blue, alpha in master.get_flattened_data()
        if alpha > 220 and green > 180 and green > red * 1.6 and green > blue * 1.6
    )

    print(f"PNG: {master.size}, ICO sizes: {actual_sizes}")
    print(
        f"Transparent corners: {transparent_corners}, "
        f"opaque green pixels: {opaque_green_pixels}"
    )
    if not transparent_corners or opaque_green_pixels:
        raise RuntimeError("透明背景またはクロマキー除去の検証に失敗しました。")


if __name__ == "__main__":
    main()
