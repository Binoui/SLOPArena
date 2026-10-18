"""Retouch the 2026-10-18 library hero original for Steam's text/logo check.

Steam rejected the first hero upload ("appears to contain text or a logo") although nothing was
lettered on it. This removes the two marks that read as text or a logo:

- Wibou's pendant: the generator drew a white letter-like glyph on the blue drop (the model has a
  plain glossy drop). Diffusion-fill the glyph from the surrounding blue.
- Bonk's loincloth: the red jagged crown emblem, inpainted from the surrounding cloth.

    python3 tools/key_art/clean_hero.py "<originals>/Four fighters in a wide newsprint showdown-2.png" \
        "<originals>/hero_steam_clean.png"

The boxes are for that image only; a regenerated hero needs its own. Needs NumPy.
"""
import sys

import numpy as np
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
img = np.asarray(Image.open(src).convert("RGB")).astype(np.float64)
H, W, _ = img.shape


def diffuse_fill(img, mask, iterations=400):
    """Fill masked pixels by repeatedly averaging their 4-neighbours (simple membrane inpainting)."""
    out = img.copy()
    known = ~mask
    # seed holes with the mean of the known ring so the iteration converges quickly
    out[mask] = img[known].mean(axis=0)
    for _ in range(iterations):
        up = np.roll(out, 1, 0); down = np.roll(out, -1, 0)
        left = np.roll(out, 1, 1); right = np.roll(out, -1, 1)
        avg = (up + down + left + right) / 4
        out[mask] = avg[mask]
    return out


def region(box):
    l, t, r, b = box
    return (slice(t, b), slice(l, r))


# 1. pendant glyph: whitish, low-saturation pixels inside the drop
pl, pt, pr, pb = 1562, 424, 1600, 452
sub = img[region((pl, pt, pr, pb))]
mx, mn = sub.max(axis=2), sub.min(axis=2)
glyph = (mn > 150) & ((mx - mn) < 70)
# grow the mask by 2 px so the anti-aliased rim goes too
grown = glyph.copy()
for _ in range(2):
    grown = grown | np.roll(grown, 1, 0) | np.roll(grown, -1, 0) | np.roll(grown, 1, 1) | np.roll(grown, -1, 1)
img[region((pl, pt, pr, pb))] = diffuse_fill(sub, grown)
print("pendant glyph pixels:", int(glyph.sum()), "filled:", int(grown.sum()))

# 2. crown emblem: inpaint the red paint (grown 3 px) from the surrounding cloth; the box stays above
#    the katana so its colours do not bleed in
cl, ct, cr, cb = 1866, 416, 1964, 497
sub = img[region((cl, ct, cr, cb))]
R, G, B = sub[..., 0], sub[..., 1], sub[..., 2]
paint = (R > 110) & (G < 0.62 * R) & (B < 0.62 * R)
grown = paint.copy()
for _ in range(3):
    grown = grown | np.roll(grown, 1, 0) | np.roll(grown, -1, 0) | np.roll(grown, 1, 1) | np.roll(grown, -1, 1)
grown[[0, -1], :] = False
grown[:, [0, -1]] = False
filled = diffuse_fill(sub, grown, iterations=3000)
# match the clean cloth's tone (the rim of the soft red edge tints the fill); added grain read as a
# speckled crown, so the patch stays smooth like the cloth's own flat areas
clean = (~grown) & (R > 170) & (G > 150) & (B > 120)
filled[grown] += (np.median(sub[clean], axis=0) - filled[grown].mean(axis=0)) * 0.9
img[region((cl, ct, cr, cb))] = filled
print("crown paint pixels:", int(paint.sum()), "filled:", int(grown.sum()))

Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).save(dst, optimize=True)
print("wrote", dst)
