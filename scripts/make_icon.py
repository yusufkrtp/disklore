"""Generates the DiskLore app icon (ico, png, icns). Requires Pillow.

Usage: python scripts/make_icon.py
"""
import math
import os
import struct
from io import BytesIO

from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), "..", "src", "DiskLore.App", "Assets")
S = 1024  # master size

BG_TOP = (30, 41, 74)
BG_BOT = (17, 24, 46)
SLICE = (255, 176, 46)
DISK = (86, 156, 255)
WHITE = (245, 248, 255)


def master() -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    # vertical gradient tile
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        c = tuple(int(BG_TOP[i] + (BG_BOT[i] - BG_TOP[i]) * t) for i in range(3)) + (255,)
        gd.line([(0, y), (S, y)], fill=c)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=int(S * 0.22), fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    cx, cy, r = S * 0.46, S * 0.50, S * 0.33
    box = [cx - r, cy - r, cx + r, cy + r]
    # disk: pie with a slice pulled out (angles clockwise from 3 o'clock in PIL)
    d.pieslice(box, start=10, end=310, fill=DISK)
    off = S * 0.045
    a = math.radians(-20)  # slice centre direction
    ox, oy = math.cos(a) * off, math.sin(a) * off
    d.pieslice([box[0] + ox, box[1] + oy, box[2] + ox, box[3] + oy], start=-50, end=10, fill=SLICE)
    # hub
    hr = r * 0.20
    d.ellipse([cx - hr, cy - hr, cx + hr, cy + hr], fill=BG_BOT)
    hr2 = hr * 0.45
    d.ellipse([cx - hr2, cy - hr2, cx + hr2, cy + hr2], fill=WHITE)

    # note bubble (the "lore") in the lower right
    bx, by, bw, bh = S * 0.60, S * 0.60, S * 0.30, S * 0.24
    d.rounded_rectangle([bx, by, bx + bw, by + bh], radius=S * 0.05, fill=WHITE)
    d.polygon([(bx + bw * 0.22, by + bh - 2), (bx + bw * 0.10, by + bh + S * 0.07), (bx + bw * 0.48, by + bh - 2)], fill=WHITE)
    for i, f in enumerate((0.72, 0.5)):
        ly = by + bh * (0.30 + 0.30 * i)
        d.rounded_rectangle([bx + bw * 0.14, ly - S * 0.012, bx + bw * 0.14 + bw * f, ly + S * 0.012], radius=S * 0.012, fill=BG_TOP)
    return img


def write_icns(img: Image.Image, path: str) -> None:
    types = [(b"icp4", 16), (b"icp5", 32), (b"icp6", 64), (b"ic07", 128), (b"ic08", 256), (b"ic09", 512), (b"ic10", 1024)]
    chunks = b""
    for tag, size in types:
        buf = BytesIO()
        img.resize((size, size), Image.LANCZOS).save(buf, "PNG")
        data = buf.getvalue()
        chunks += tag + struct.pack(">I", len(data) + 8) + data
    with open(path, "wb") as f:
        f.write(b"icns" + struct.pack(">I", len(chunks) + 8) + chunks)


def main() -> None:
    os.makedirs(OUT, exist_ok=True)
    img = master()
    img.resize((512, 512), Image.LANCZOS).save(os.path.join(OUT, "disklore.png"))
    sizes = [16, 24, 32, 48, 64, 128, 256]
    img.save(os.path.join(OUT, "disklore.ico"), sizes=[(s, s) for s in sizes])
    write_icns(img, os.path.join(OUT, "disklore.icns"))
    # preview strip for manual checks
    print("wrote icons to", os.path.abspath(OUT))


if __name__ == "__main__":
    main()
