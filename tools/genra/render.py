"""Fast software renders of the skinned character for self-review (no GPU or Blender).

Orthographic, painter's-algorithm flat shading with one colour per body region, so
limb placement, silhouettes and fist shapes are easy to read. Views are character-frame
camera directions (from the character toward the camera).
"""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from .rig import unit


def camera_direction(yaw_degrees=0.0, pitch_degrees=20.0) -> tuple:
    """Char-frame direction from the character to a match-style camera: behind the fighter (looking
    along its facing), raised by `pitch`, orbited by `yaw` toward its right (+) or left (-)."""
    yaw, pitch = np.radians(yaw_degrees), np.radians(pitch_degrees)
    return (np.sin(yaw) * np.cos(pitch), -np.cos(yaw) * np.cos(pitch), np.sin(pitch))


VIEWS = {
    'profile': (1, 0, 0.12),      # from the character's right side
    'front': (0, 1, 0.25),        # facing the character
    'back': (0, -1, 0.25),
    'top': (0.05, -0.2, 1),
    # Review angle: behind one shoulder, where the strike stays visible (see Brief.review_views).
    'three-quarter-right': camera_direction(35, 20),
    'three-quarter-left': camera_direction(-35, 20),
    'gameplay': camera_direction(0, 20),  # the match camera's default: straight behind, 20 degrees up
}
COLORS = {'Head': (232, 150, 110), 'Torso': (120, 128, 140), 'Tail': (220, 120, 70),
          'LeftUpperArm': (90, 150, 220), 'LeftForeArm': (60, 120, 200), 'LeftHand': (255, 210, 60),
          'RightUpperArm': (220, 110, 110), 'RightForeArm': (200, 70, 70), 'RightHand': (255, 150, 40),
          'LeftLeg': (110, 190, 140), 'LeftFoot': (70, 150, 100), 'RightLeg': (180, 160, 220), 'RightFoot': (140, 110, 190),
          'Weapon': (210, 220, 235)}
BACKGROUND = (24, 28, 36)


def _font(size):
    for path in ('/usr/share/fonts/TTF/DejaVuSans.ttf', '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'):
        if Path(path).exists():
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


class Renderer:
    def __init__(self, skin, frame):
        self.skin, self.frame = skin, frame
        self.tri_colors = []
        for m in skin.meshes:
            names = [skin.region_names[i] for i in m['region']]
            base = np.array([COLORS.get(n, (150, 150, 150)) for n in names], float)
            self.tri_colors.append(base[m['triangles']].mean(axis=1))

    def camera(self, view):
        d = self.frame.dir_to_world(VIEWS[view] if isinstance(view, str) else view)
        up = self.frame.up - d * (self.frame.up @ d)
        up = unit(up) if np.linalg.norm(up) > 1e-6 else unit(self.frame.front)
        return np.cross(up, d), up, d  # screen-right, screen-up, toward camera

    def fit(self, worlds, margin=1.15) -> tuple[float, np.ndarray]:
        """(span, char-frame centre) that frames the skinned mesh over the given poses in every view."""
        pts = np.concatenate([np.concatenate(self.skin.points(w)) for w in worlds])
        char = self.frame.to_char(pts)
        lo, hi = char.min(axis=0), char.max(axis=0)
        return float(np.linalg.norm(hi - lo) * margin), (lo + hi) / 2

    def image(self, world, view='profile', size=320, span=None, center=None, label=None) -> Image.Image:
        """Render one pose. `span` is the visible width in metres and `center` a char-frame point;
        both default to fitting this pose."""
        if span is None or center is None:
            fit_span, fit_center = self.fit([world])
            span = fit_span if span is None else span
            center = fit_center if center is None else center
        right, up, toward = self.camera(view)
        center_world = self.frame.to_world(center)
        scale = 2 * size / span
        img = Image.new('RGB', (2 * size, 2 * size), BACKGROUND)
        draw = ImageDraw.Draw(img)
        light = unit(toward + 0.6 * self.frame.up + 0.3 * right)
        polys = []
        for m, pts, colors in zip(self.skin.meshes, self.skin.points(world), self.tri_colors):
            rel = pts - center_world
            sx = size + rel @ right * scale
            sy = size - rel @ up * scale
            depth = rel @ toward
            tri = m['triangles']
            a, b, c = pts[tri[:, 0]], pts[tri[:, 1]], pts[tri[:, 2]]
            normal = np.cross(b - a, c - a)
            normal /= np.linalg.norm(normal, axis=1, keepdims=True) + 1e-12
            shade = 0.35 + 0.65 * np.abs(normal @ light)
            polys.append((depth[tri].mean(1), sx[tri], sy[tri], colors * shade[:, None]))
        depth = np.concatenate([p[0] for p in polys])
        xs = np.concatenate([p[1] for p in polys])
        ys = np.concatenate([p[2] for p in polys])
        cs = np.concatenate([p[3] for p in polys]).clip(0, 255).astype(int)
        for i in np.argsort(depth):  # far to near
            draw.polygon([(xs[i, 0], ys[i, 0]), (xs[i, 1], ys[i, 1]), (xs[i, 2], ys[i, 2])], fill=tuple(cs[i]))
        img = img.resize((size, size), Image.LANCZOS)
        if label:
            ImageDraw.Draw(img).text((6, 4), label, fill=(235, 238, 242), font=_font(13))
        return img


def visibility(renderer, world, regions, view, size=256) -> float:
    """Visible fraction of the given body regions (e.g. ['Weapon'] or ['RightHand']) from a view:
    pixels they cover in the full render, over pixels they cover when drawn alone."""
    skin = renderer.skin
    span, center = renderer.fit([world])
    right, up, toward = renderer.camera(view)
    center_world = renderer.frame.to_world(center)
    scale = size / span
    wanted = {skin.region_names.index(r) for r in regions if r in skin.region_names}
    tris = []
    for m, pts in zip(skin.meshes, skin.points(world)):
        rel = pts - center_world
        sx, sy, depth = size / 2 + rel @ right * scale, size / 2 - rel @ up * scale, rel @ toward
        t = m['triangles']
        tris.append((depth[t].mean(1), sx[t], sy[t], m['region'][t[:, 0]]))
    depth = np.concatenate([x[0] for x in tris])
    xs, ys = np.concatenate([x[1] for x in tris]), np.concatenate([x[2] for x in tris])
    region = np.concatenate([x[3] for x in tris])
    order = np.argsort(depth)

    def coverage(only_wanted):
        img = Image.new('L', (size, size), 0)
        draw = ImageDraw.Draw(img)
        for i in order:
            hit = region[i] in wanted
            if only_wanted and not hit:
                continue
            draw.polygon([(xs[i, 0], ys[i, 0]), (xs[i, 1], ys[i, 1]), (xs[i, 2], ys[i, 2])], fill=255 if hit else 1)
        return int((np.asarray(img) == 255).sum())

    alone = coverage(True)
    return coverage(False) / alone if alone else 0.0


def strike_visibility(clip, joints, tick, views, skin=None) -> dict:
    """Visible fraction, per view, of the body regions carrying the strike joints (e.g. RightHand, or
    Weapon for bladeEnd) at one tick. Review information, not a check."""
    from .skin import Skin, region_of
    skin = skin or Skin(clip.rig)
    regions = sorted({region_of(clip.rig)[clip.rig.index(j)] for j in joints})
    renderer = Renderer(skin, clip.frame)
    world = clip.world()[tick]
    return {view: round(visibility(renderer, world, regions, view), 2) for view in views}


TRAILS = {'LeftHand': (255, 210, 60), 'RightHand': (255, 150, 40), 'LeftForeArm': (90, 150, 220),
          'RightForeArm': (220, 110, 110), 'bladeEnd': (235, 240, 250)}


def motion_sheet(clip, views=('front', 'three-quarter-right', 'profile'), path=None, ticks=None, size=360, every=4,
                 skin=None, marks=()) -> Image.Image:
    """Motion at a glance, one panel per view: ghosted poses every `every` ticks and the paths of the hands,
    elbows (forearm joints) and blade tip with a dot per tick (spacing shows speed). Ticks in `marks` and
    every 6th tick are numbered on the right-hand path."""
    from .skin import Skin
    skin = skin or Skin(clip.rig)
    renderer = Renderer(skin, clip.frame)
    world = clip.world()
    ticks = list(ticks if ticks is not None else range(len(world)))
    span, center = renderer.fit([world[t] for t in ticks])
    ghosts = sorted(set(ticks[::every]) | {ticks[-1]})
    names = [n for n in TRAILS if n in clip.rig.names]
    sheet = Image.new('RGB', (size * len(views), size), BACKGROUND)
    font = _font(11)
    for v, view in enumerate(views):
        panel = Image.new('RGB', (size, size), BACKGROUND)
        for k, tick in enumerate(ghosts):
            frame = renderer.image(world[tick], view, size, span, center)
            mask = Image.fromarray(((np.asarray(frame) != BACKGROUND).any(axis=2) * int(60 + 140 * (k + 1) / len(ghosts))).astype(np.uint8))
            panel.paste(frame, (0, 0), mask)
        right, up, _ = renderer.camera(view)
        origin = clip.frame.to_world(center)
        draw = ImageDraw.Draw(panel)
        for name in names:
            p = world[ticks, clip.rig.index(name), 3, :3] - origin
            xy = np.stack([size / 2 + p @ right * size / span, size / 2 - p @ up * size / span], axis=1)
            draw.line([tuple(q) for q in xy], fill=TRAILS[name], width=2)
            for tick, q in zip(ticks, xy):
                draw.ellipse([q[0] - 2, q[1] - 2, q[0] + 2, q[1] + 2], fill=TRAILS[name])
                if name == 'RightHand' and (tick in marks or tick % 6 == 0):
                    draw.text((q[0] + 4, q[1] - 6), str(tick), fill=(235, 238, 242), font=font)
        draw.text((6, 4), f'{view} t{ticks[0]}-{ticks[-1]}', fill=(235, 238, 242), font=_font(13))
        sheet.paste(panel, (v * size, 0))
    if path:
        sheet.save(path)
    return sheet


def motion_overview(clip, contact, path=None, views=('front', 'three-quarter-right', 'profile'), size=400, skin=None):
    """Two motion_sheet rows: entry to contact, then contact to the end."""
    from .skin import Skin
    skin = skin or Skin(clip.rig)
    last = len(clip) - 1
    contact = min(max(int(contact), 1), last - 1)
    rows = [motion_sheet(clip, views, ticks=range(0, contact + 1), size=size, skin=skin, marks=(contact,)),
            motion_sheet(clip, views, ticks=range(contact, last + 1), size=size, skin=skin, marks=(contact, last))]
    sheet = Image.new('RGB', (rows[0].width, rows[0].height * 2), BACKGROUND)
    for r, row in enumerate(rows):
        sheet.paste(row, (0, r * row.height))
    if path:
        sheet.save(path)
    return sheet


def preview_video(clip, path, views=('profile', 'three-quarter-right'), size=400, skin=None, contact=None, active=None,
                  slow=4) -> Path:
    """MP4 of the whole clip, views side by side at fixed framing: twice at real speed (one frame per 60 Hz
    tick), then once at 1/slow speed. Ticks in `active` [first, last] are labelled in red; needs ffmpeg."""
    import subprocess
    from .skin import Skin
    skin = skin or Skin(clip.rig)
    renderer = Renderer(skin, clip.frame)
    world = clip.world()
    span, center = renderer.fit(list(world))
    width, height = size * len(views), size + 28
    font, small = _font(16), _font(13)
    frames = []
    for tick in range(len(world)):
        frame = Image.new('RGB', (width, height), BACKGROUND)
        for i, view in enumerate(views):
            frame.paste(renderer.image(world[tick], view, size, span, center, view), (i * size, 28))
        hot = active is not None and active[0] <= tick <= active[1]
        label = f't{tick}' + (' contact' if tick == contact else ' active' if hot else '')
        ImageDraw.Draw(frame).text((8, 5), label, fill=(255, 110, 90) if hot else (235, 238, 242), font=font)
        frames.append(frame)
    path = Path(path)
    command = ['ffmpeg', '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', f'{width}x{height}',
               '-r', '60', '-i', '-', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '20', '-movflags', '+faststart',
               '-vf', 'pad=ceil(iw/2)*2:ceil(ih/2)*2', str(path)]
    with subprocess.Popen(command, stdin=subprocess.PIPE) as encoder:
        for speed, repeat, passes in (('1x', 1, 2), (f'1/{slow}x', slow, 1)):
            for _ in range(passes):
                for frame in frames:
                    marked = frame.copy()
                    ImageDraw.Draw(marked).text((width - 50, 7), speed, fill=(170, 176, 186), font=small)
                    data = marked.tobytes()
                    for _ in range(repeat):
                        encoder.stdin.write(data)
        encoder.stdin.close()
        if encoder.wait() != 0:
            raise RuntimeError('ffmpeg failed to encode the preview')
    return path


def contact_sheet(clip, ticks, views=('profile', 'front', 'three-quarter-right'), path=None, size=260, span=None,
                  center=None, skin=None):
    """Grid image: one row per view, one column per tick. Returns the PIL image (saved if path).

    Framing defaults to one fit over all shown ticks, so poses stay comparable across columns.
    """
    from .skin import Skin
    skin = skin or Skin(clip.rig)
    renderer = Renderer(skin, clip.frame)
    world = clip.world()
    if span is None or center is None:
        fit_span, fit_center = renderer.fit([world[t] for t in ticks])
        span = fit_span if span is None else span
        center = fit_center if center is None else center
    sheet = Image.new('RGB', (size * len(ticks), size * len(views)), BACKGROUND)
    for r, view in enumerate(views):
        for c, tick in enumerate(ticks):
            sheet.paste(renderer.image(world[tick], view, size, span, center, f'{view} t{tick}'), (c * size, r * size))
    if path:
        sheet.save(path)
    return sheet
