"""
Generates every image the mod needs for publishing, at the exact sizes each store wants.

Kept as a script rather than a folder of finished files so the artwork can be regenerated
after any tweak, and so the sizes stay in one place: Thunderstore rejects an icon that is not
exactly 256x256, and there is no worse time to discover that than at upload.

    python -m pip install Pillow
    python assets/make_images.py

Everything is drawn from primitives - no external assets - so this runs anywhere the font is
available. Oswald ships with Windows; the fallbacks cover the rest.
"""

import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

OUT = os.path.dirname(os.path.abspath(__file__))

# A rainy-night palette: deep blue dark, one cold neon, one warm lamp.
INK        = (11, 20, 32)
INK_LIGHT  = (24, 40, 58)
NEON       = (77, 208, 225)
LAMP       = (255, 183, 77)
PAPER      = (235, 240, 245)

FONT_CANDIDATES = [
    "C:/Windows/Fonts/Oswald-Bold.ttf",
    "C:/Windows/Fonts/bahnschrift.ttf",
    "C:/Windows/Fonts/arialbd.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
]


def font(size):
    for path in FONT_CANDIDATES:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    return ImageFont.load_default()


def backdrop(w, h):
    """Night sky, a pool of lamplight, and rain falling at an angle."""
    img = Image.new("RGB", (w, h), INK)
    d = ImageDraw.Draw(img)

    # A soft vertical lift so the top is not a flat block of colour.
    for y in range(h):
        t = y / max(h - 1, 1)
        shade = tuple(int(INK[i] + (INK_LIGHT[i] - INK[i]) * (1 - t) ** 2) for i in range(3))
        d.line([(0, y), (w, y)], fill=shade)

    # Lamplight from the upper left, blurred into the dark.
    glow = Image.new("RGB", (w, h), INK)
    gd = ImageDraw.Draw(glow)
    r = int(min(w, h) * 0.55)
    gd.ellipse([-r // 2, -r // 2, r, r], fill=(40, 60, 80))
    glow = glow.filter(ImageFilter.GaussianBlur(radius=max(w, h) // 12))
    img = Image.blend(img, glow, 0.5)

    # Rain: short diagonal strokes, denser at the edges than over the type.
    d = ImageDraw.Draw(img, "RGBA")
    step = max(6, w // 90)
    length = max(8, h // 14)
    for x in range(-length, w + length, step):
        for k in range(0, h, length * 3):
            y = (k + (x * 7) % (length * 3))
            centre_bias = abs((x / w) - 0.5) * 2
            alpha = int(18 + 30 * centre_bias)
            d.line([(x, y), (x - length // 2, y + length)], fill=(150, 190, 210, alpha), width=1)

    return img


def speech_bubble(draw, box, outline, width=6):
    """A rounded speech bubble with a tail, drawn as an outline."""
    x0, y0, x1, y1 = box
    radius = int((y1 - y0) * 0.28)
    draw.rounded_rectangle(box, radius=radius, outline=outline, width=width)

    # The tail, pointing down-left, drawn as a filled triangle over the border.
    tail_x = x0 + int((x1 - x0) * 0.22)
    tail_w = int((x1 - x0) * 0.10)
    draw.polygon(
        [(tail_x, y1 - width // 2),
         (tail_x + tail_w, y1 - width // 2),
         (tail_x - tail_w // 3, y1 + int((y1 - y0) * 0.26))],
        outline=outline, fill=None,
    )
    draw.line([(tail_x, y1), (tail_x - tail_w // 3, y1 + int((y1 - y0) * 0.26))],
              fill=outline, width=width)
    draw.line([(tail_x - tail_w // 3, y1 + int((y1 - y0) * 0.26)), (tail_x + tail_w, y1)],
              fill=outline, width=width)


def glow_text(img, xy, text, fnt, fill, glow_colour, anchor="mm", glow=8):
    """Text with a neon bloom behind it, which is most of what makes it read as a sign."""
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.text(xy, text, font=fnt, fill=glow_colour + (200,), anchor=anchor)
    layer = layer.filter(ImageFilter.GaussianBlur(glow))
    img.paste(Image.alpha_composite(img.convert("RGBA"), layer).convert("RGB"), (0, 0))

    d = ImageDraw.Draw(img)
    d.text(xy, text, font=fnt, fill=fill, anchor=anchor)


def make_icon(size=256):
    """Thunderstore wants exactly 256x256. Stacked, because wide type dies at this size."""
    img = backdrop(size, size)
    d = ImageDraw.Draw(img)

    speech_bubble(d, (int(size * 0.10), int(size * 0.12), int(size * 0.90), int(size * 0.66)),
                  NEON, width=max(3, size // 64))

    glow_text(img, (size // 2, int(size * 0.30)), "LOOSE", font(int(size * 0.20)),
              PAPER, NEON, glow=size // 32)
    glow_text(img, (size // 2, int(size * 0.50)), "LIPS", font(int(size * 0.20)),
              PAPER, NEON, glow=size // 32)

    d = ImageDraw.Draw(img)
    d.text((size // 2, int(size * 0.80)), "SHADOWS OF DOUBT", font=font(int(size * 0.068)),
           fill=LAMP, anchor="mm")
    return img


def make_wide(w, h, tagline="Talk to anyone. In your own words."):
    """Banner shape: type on the left, bubble behind it, tagline under."""
    img = backdrop(w, h)
    d = ImageDraw.Draw(img)

    bubble = (int(w * 0.06), int(h * 0.18), int(w * 0.62), int(h * 0.62))
    speech_bubble(d, bubble, NEON, width=max(3, h // 90))

    title = font(int(h * 0.26))
    glow_text(img, (int(w * 0.34), int(h * 0.40)), "LOOSE LIPS", title, PAPER, NEON,
              glow=max(6, h // 40))

    d = ImageDraw.Draw(img)
    d.text((int(w * 0.06), int(h * 0.76)), tagline, font=font(int(h * 0.085)),
           fill=PAPER, anchor="lm")
    d.text((int(w * 0.06), int(h * 0.88)), "A Shadows of Doubt mod  ·  local AI dialogue with consequences",
           font=font(int(h * 0.055)), fill=LAMP, anchor="lm")
    return img


TARGETS = [
    ("icon.png",        lambda: make_icon(256),          "Thunderstore icon, must be exactly 256x256"),
    ("banner.png",      lambda: make_wide(1280, 640),    "GitHub social preview"),
    ("logo-wide.png",   lambda: make_wide(1200, 400),    "README header"),
    ("modio-logo.png",  lambda: make_wide(1280, 720),    "mod.io logo"),
]


def main():
    for name, build, why in TARGETS:
        path = os.path.join(OUT, name)
        img = build()
        img.save(path, "PNG", optimize=True)
        print(f"{name:18} {img.size[0]}x{img.size[1]:<5} {why}")


if __name__ == "__main__":
    main()
