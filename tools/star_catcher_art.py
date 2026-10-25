"""Star Catcher's newer graphics, drawn as crisp pixel art and written into its
designer assets (example_star_catcher/gfx and chr):

  - nebulae for the sky, so that the raster ripple shows (background palettes 1-2);
  - four more mutants: the diver, the turret, the weaver, the giant meteor;
  - the 3D stage: the ship seen from behind, its objects at three sizes, and the
    perspective floor (chr/floor.chr, pattern table 1, gfx/stage3d.screen.json).

    python tools/star_catcher_art.py

The script owns tiles $80-$FF of chr/tiles.chr and the assets' items whose names it
lists; running it again replaces them, and leaves everything else as it is.
"""

import json
import math
import os
import random

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'example_star_catcher')
FIRST, LAST = 0x80, 0xFF

# ---------------------------------------------------------------------------
# Drawing: regions of shapes, shaded with an outline, a lit rim and a mid tone.


class Picture:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.region = [[None] * w for _ in range(h)]
        self.tones = {}            # region -> (outline, mid, light) colour numbers (1-3)
        self.paint = {}            # (x, y) -> colour, on top

    def fill(self, name, tones, inside):
        self.tones[name] = tones
        for y in range(self.h):
            for x in range(self.w):
                if inside(x + 0.5, y + 0.5):
                    self.region[y][x] = name

    def ellipse(self, name, tones, cx, cy, rx, ry):
        self.fill(name, tones, lambda x, y: ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1)

    def polygon(self, name, tones, pts):
        def inside(x, y):
            c = False
            for i in range(len(pts)):
                (x1, y1), (x2, y2) = pts[i], pts[(i + 1) % len(pts)]
                if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
                    c = not c
            return c
        self.fill(name, tones, inside)

    def dot(self, x, y, colour):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.paint[(x, y)] = colour

    def pixels(self):
        def rid(x, y):
            return self.region[y][x] if 0 <= x < self.w and 0 <= y < self.h else None
        out = [[0] * self.w for _ in range(self.h)]
        for y in range(self.h):
            for x in range(self.w):
                r = rid(x, y)
                if r is None:
                    continue
                dark, mid, light = self.tones[r]
                if any(rid(x + dx, y + dy) is None for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                    c = dark
                elif rid(x - 1, y) != r or rid(x, y - 1) != r:
                    c = dark
                elif rid(x - 2, y) is None or rid(x, y - 2) is None or rid(x - 1, y - 1) is None:
                    c = light
                else:
                    c = mid
                out[y][x] = c
        for (x, y), c in self.paint.items():
            out[y][x] = c
        return out


def scaled(draw, size):
    """A picture drawn by draw(p, s) where s is the size's scale (1 for 32 pixels)."""
    p = Picture(size, size)
    draw(p, size / 32)
    return p


# ---------------------------------------------------------------------------
# Tiles

class Tiles:
    """The script's tiles of a CHR file, deduplicated (sprites also by mirror)."""

    def __init__(self, data, first, last):
        self.data = bytearray(data)
        self.first, self.last = first, last
        self.next = first
        self.known = {}
        for i in range(first, last + 1):
            self.data[i * 16:i * 16 + 16] = bytes(16)

    def add(self, px, flips=True):
        key = tuple(px)
        if key in self.known:
            return self.known[key], False, False
        if flips:
            rows = [px[i * 8:i * 8 + 8] for i in range(8)]
            for fh, fv, v in ((True, False, [p for r in rows for p in reversed(r)]),
                              (False, True, [p for r in reversed(rows) for p in r]),
                              (True, True, [p for r in reversed(rows) for p in reversed(r)])):
                if tuple(v) in self.known:
                    return self.known[tuple(v)], fh, fv
        if self.next > self.last:
            raise SystemExit('out of tiles: more than %d' % (self.last - self.first + 1))
        i = self.next
        self.next += 1
        self.known[key] = i
        for y in range(8):
            lo = hi = 0
            for x in range(8):
                c = px[y * 8 + x]
                lo |= (c & 1) << (7 - x)
                hi |= ((c >> 1) & 1) << (7 - x)
            self.data[i * 16 + y] = lo
            self.data[i * 16 + 8 + y] = hi
        return i, False, False


def metasprite(name, picture, tiles, palette, origin='center'):
    px = picture.pixels()
    items = []
    for ty in range(0, picture.h, 8):
        for tx in range(0, picture.w, 8):
            cell = [px[ty + y][tx + x] if ty + y < picture.h and tx + x < picture.w else 0
                    for y in range(8) for x in range(8)]
            if any(cell):
                index, fh, fv = tiles.add(cell)
                items.append({'x': tx, 'y': ty, 'tile': index, 'palette': palette,
                              'flipH': fh, 'flipV': fv, 'behind': False})
    ox, oy = (picture.w // 2, picture.h // 2) if origin == 'center' else (0, 0)
    return {'name': name, 'originX': ox, 'originY': oy, 'tiles': items}


# ---------------------------------------------------------------------------
# The vertical stages' new mutants (palettes: 0 ship white-blue-red, 2 meteors
# dark-rust-orange, 3 burst orange-peach-white).

def diver(p, frame):
    """A dart that dives at the ship: white hull, blue fins, red jet."""
    w = (2, 1, 1)        # outline blue, body white, light white
    p.polygon('fins', (2, 2, 1), [(1, 4), (15, 4), (12, 9), (4, 9)])
    p.polygon('hull', w, [(8, 15.5), (11, 4), (8, 0.5), (5, 4)])
    p.dot(8, 13, 2)
    p.dot(8, 12, 2)
    for y in range(0, 2 + frame * 2):
        p.dot(7, y, 3)
        p.dot(8, y, 3)


def turret(p, frame):
    """A drifting mine with a gun that tracks the ship."""
    rust = (1, 2, 3)
    p.ellipse('shell', rust, 8, 8, 7, 7)
    p.ellipse('core', rust, 8, 8, 3, 3)
    for a in range(0, 360, 45):
        r = math.radians(a + frame * 22.5)
        p.dot(round(8 + 6.5 * math.cos(r)), round(8 + 6.5 * math.sin(r)), 3)
    p.dot(8, 8, 1)


def weaver(p, frame):
    """A ray that weaves down the sky, its wings beating."""
    o = (1, 2, 3)
    span = 7.5 - frame * 1.5
    p.polygon('wings', o, [(8 - span, 6), (8, 2), (8 + span, 6), (8, 11)])
    p.ellipse('body', o, 8, 8, 2.5, 6)
    p.dot(7, 6, 3)
    p.dot(9, 6, 3)
    p.dot(8, 14, 1)


def big_rock(p, frame):
    """A giant meteor: it breaks into two small ones."""
    rng = random.Random(7 + frame)
    o = (1, 2, 3)
    pts = []
    for i in range(10):
        a = i / 10 * 2 * math.pi
        r = 7.2 - rng.random() * 1.6
        pts.append((8 + r * math.cos(a), 8 + r * math.sin(a)))
    p.polygon('rock', o, pts)
    for x, y in ((6, 6), (10, 9), (7, 11)):
        p.dot(x, y, 1)
        p.dot(x + 1, y, 1)


# ---------------------------------------------------------------------------
# The 3D stage's sprites

def ship_back(p):
    """The ship from behind: wings, the canopy, two red engines."""
    p.polygon('wings', (2, 1, 1), [(0.5, 13), (12, 4), (23.5, 13), (12, 15.5)])
    p.polygon('body', (2, 1, 1), [(8, 15.5), (9.5, 2), (12, 0.5), (14.5, 2), (16, 15.5)])
    p.ellipse('canopy', (2, 2, 1), 12, 6, 2, 3)
    for x in (9, 15):
        p.dot(x, 13, 3)
        p.dot(x, 14, 3)


def rock3d(p, s):
    rng = random.Random(3)
    pts = []
    for i in range(12):
        a = i / 12 * 2 * math.pi
        r = (14 - rng.random() * 3) * s
        pts.append((16 * s + r * math.cos(a), 16 * s + r * math.sin(a)))
    p.polygon('rock', (1, 2, 3), pts)
    for x, y in ((11, 12), (19, 18), (13, 21)):
        p.ellipse('crater %d' % x, (1, 1, 2), x * s, y * s, max(1, 2.5 * s), max(1, 2 * s))


def saucer3d(p, s):
    p.ellipse('rim', (1, 2, 3), 16 * s, 19 * s, 15 * s, 6 * s)
    p.ellipse('dome', (1, 3, 3), 16 * s, 13 * s, 7 * s, 6 * s)
    if s >= 0.5:
        for x in (8, 13, 19, 24):
            p.dot(round(x * s), round(20 * s), 3)


def fighter3d(p, s):
    p.polygon('wings', (2, 1, 1), [(1 * s, 20 * s), (16 * s, 10 * s), (31 * s, 20 * s), (16 * s, 24 * s)])
    p.ellipse('body', (2, 1, 1), 16 * s, 16 * s, 4 * s, 9 * s)
    p.ellipse('eye', (2, 3, 3), 16 * s, 15 * s, max(1, 2 * s), max(1, 2 * s))


def pylon3d(p, s):
    p.polygon('mast', (2, 1, 1), [(13 * s, 31.5 * s), (14.5 * s, 1 * s), (17.5 * s, 1 * s), (19 * s, 31.5 * s)])
    p.ellipse('lamp', (2, 3, 3), 16 * s, 3 * s, max(1.5, 3 * s), max(1.5, 3 * s))


# ---------------------------------------------------------------------------
# Backgrounds: nebulae for the sky; the 3D stage's sky and floor

def nebula(w, h, seed):
    """A soft cloud: colour 1 at its edge, 2 inside, 3 in its heart, dithered."""
    rng = random.Random(seed)
    blobs = [(rng.uniform(0.3, 0.7) * w, rng.uniform(0.3, 0.7) * h, rng.uniform(0.25, 0.45) * w, rng.uniform(0.25, 0.4) * h)
             for _ in range(3)]
    out = [[0] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            d = min(math.hypot((x - cx) / rx, (y - cy) / ry) for cx, cy, rx, ry in blobs)
            n = math.sin(x * 0.9 + y * 0.6) * 0.06 + math.sin(x * 0.31 - y * 0.47) * 0.08
            v = d + n
            if v < 0.45:
                out[y][x] = 3 if (x + y) % 2 or v < 0.3 else 2
            elif v < 0.75:
                out[y][x] = 2 if (x + y) % 2 or v < 0.62 else 1
            elif v < 1.0 and (x * 3 + y) % 4 == 0:
                out[y][x] = 1
            elif v < 1.05 and (x + y * 2) % 7 == 0:
                out[y][x] = 1
    return out


HORIZON = 96


def stage3d_screen():
    """The 3D stage's background: stars, a glowing horizon, and the floor's bands,
    thicker the nearer they are, coloured 1, 2, 3 in turn (the NMI turns the three
    colours round to move them), with lines running to the vanishing point."""
    px = [[(0, 0)] * 256 for _ in range(240)]
    rng = random.Random(42)
    for _ in range(60):
        x, y = rng.randrange(256), rng.randrange(8, HORIZON - 10)
        px[y][x] = (0, 1 if rng.random() < 0.7 else 3)
    for y in range(HORIZON - 8, HORIZON + 8):           # the horizon's glow
        for x in range(256):
            t = abs(y - (HORIZON + 0.5))
            if t < 1.5:
                px[y][x] = (1, 3)
            elif t < 4 and (x + y) % 2:
                px[y][x] = (1, 2)
            elif t < 7 and (x * 3 + y) % 5 == 0:
                px[y][x] = (1, 1)
    for y in range(HORIZON + 8, 240):
        depth = 1200 / (y - HORIZON + 6)
        band = int(depth / 6)
        for x in range(256):
            px[y][x] = (2, 1 + band % 3)
        for i in (-3, -2, -1, 0, 1, 2, 3):              # the lines to the vanishing point
            xl = 128 + i * 72 * (y - HORIZON) / (240 - HORIZON)
            for dx in (0, 1) if y > 170 else (0,):
                xi = round(xl) + dx
                if 0 <= xi < 256:
                    px[y][xi] = (2, 0)
    return px


def screen_tiles(px, tiles):
    """Tiles (no flipping for the background) and 16x16 attributes of a 256x240 picture."""
    cells, attrs = [], []
    for by in range(15):
        for bx in range(16):
            ps = [px[y][x][0] for y in range(by * 16, by * 16 + 16) for x in range(bx * 16, bx * 16 + 16) if px[y][x][1]]
            attrs.append(max(set(ps), key=ps.count) if ps else 0)
    for ty in range(30):
        for tx in range(32):
            cells.append(tiles.add([px[ty * 8 + y][tx * 8 + x][1] for y in range(8) for x in range(8)], flips=False)[0])
    return cells, attrs


# ---------------------------------------------------------------------------

GENERATED = ['diver', 'diver 2', 'turret', 'turret 2', 'weaver', 'weaver 2', 'giant meteor', 'giant meteor 2',
             'ship back'] + ['%s %d' % (k, n) for k in ('rock3d', 'saucer3d', 'fighter3d', 'pylon3d') for n in (8, 16, 32)] \
            + ['shot3d', 'bullet3d']
ANIMS = ['diver', 'turret', 'weaver', 'giant meteor']


def main():
    gfx = os.path.join(ROOT, 'gfx')
    chr_path = os.path.join(ROOT, 'chr', 'tiles.chr')
    data = open(chr_path, 'rb').read()[:4096]           # pattern table 0; table 1 is floor.chr
    tiles = Tiles(data, FIRST, LAST)

    # Sprites
    asset = json.load(open(os.path.join(gfx, 'objects.msprite.json')))
    asset['metasprites'] = [m for m in asset['metasprites'] if m['name'] not in GENERATED]
    asset['animations'] = [a for a in asset['animations'] if a['name'] not in ANIMS]
    new = []
    for name, draw, palette in (('diver', diver, 0), ('turret', turret, 2), ('weaver', weaver, 3), ('giant meteor', big_rock, 2)):
        for frame, suffix in ((0, ''), (1, ' 2')):
            p = Picture(16, 16)
            draw(p, frame)
            new.append(metasprite(name + suffix, p, tiles, palette, origin='topleft'))
        asset['animations'].append({'name': name, 'loop': True,
                                    'frames': [{'metasprite': name, 'duration': 8}, {'metasprite': name + ' 2', 'duration': 8}]})
    p = Picture(24, 16)
    ship_back(p)
    new.append(metasprite('ship back', p, tiles, 0))
    for name, draw, palette in (('rock3d', rock3d, 2), ('saucer3d', saucer3d, 3), ('fighter3d', fighter3d, 0), ('pylon3d', pylon3d, 0)):
        for size in (8, 16, 32):
            new.append(metasprite('%s %d' % (name, size), scaled(draw, size), tiles, palette))
    shot = Picture(8, 8)
    shot.ellipse('shot', (1, 2, 3), 4, 4, 2.5, 2.5)
    new.append(metasprite('shot3d', shot, tiles, 1))
    bullet = Picture(8, 8)
    bullet.ellipse('bullet', (1, 2, 3), 4, 4, 3, 3)
    new.append(metasprite('bullet3d', bullet, tiles, 3))
    asset['metasprites'] += new
    sprite_tiles = tiles.next - FIRST

    # Nebulae: metatiles of the sky map, in background palettes 1 and 2
    sky = json.load(open(os.path.join(gfx, 'play.map.json')))
    sky['metatiles'] = [m for m in sky['metatiles'] if not m['name'].startswith('nebula')]
    base = len(sky['metatiles'])
    clouds = []
    # Two cloud shapes, each in both nebula palettes: the palette is the metatile's,
    # so the second colour costs no tile.
    for w, h, seed in ((48, 32, 11), (32, 32, 12)):
        px = nebula(w, h, seed)
        blocks = []
        for by in range(0, h, 16):
            for bx in range(0, w, 16):
                t = []
                for ty in (by, by + 8):
                    for tx in (bx, bx + 8):
                        t.append(tiles.add([px[ty + y][tx + x] for y in range(8) for x in range(8)], flips=False)[0])
                blocks.append(t)
        for palette in (1, 2):
            grid, i = [], 0
            for by in range(0, h, 16):
                row = []
                for bx in range(0, w, 16):
                    sky['metatiles'].append({'name': 'nebula %d' % len(sky['metatiles']), 'tiles': blocks[i], 'palette': palette})
                    row.append(len(sky['metatiles']) - 1)
                    i += 1
                grid.append(row)
            clouds.append(grid)
    cells = sky['cells']
    w = sky['width']
    cells[:] = [c if c < base else 0 for c in cells]
    for grid, (x0, y0) in zip(clouds * 2, ((1, 2), (11, 6), (4, 11), (12, 15), (2, 19), (9, 22), (5, 26), (11, 27))):
        for dy, row in enumerate(grid):
            for dx, m in enumerate(row):
                y = (y0 + dy) % sky['height']
                if x0 + dx < w:
                    cells[y * w + x0 + dx] = m
    json.dump(sky, open(os.path.join(gfx, 'play.map.json'), 'w', newline='\n'), indent=2)

    pal = json.load(open(os.path.join(gfx, 'game.pal.json')))
    pal['palettes'][1] = {'name': 'violet nebula', 'colors': [0x0F, 0x02, 0x13, 0x24]}
    pal['palettes'][2] = {'name': 'teal nebula', 'colors': [0x0F, 0x0C, 0x1C, 0x2C]}
    json.dump(pal, open(os.path.join(gfx, 'game.pal.json'), 'w', newline='\n'), indent=2)

    json.dump(asset, open(os.path.join(gfx, 'objects.msprite.json'), 'w', newline='\n'), indent=2)
    open(chr_path, 'wb').write(bytes(tiles.data))

    # The 3D stage: its own pattern table (floor.chr), screen and palettes.
    floor = Tiles(bytes(4096), 0, 255)
    floor.add([0] * 64, flips=False)
    cells3d, attrs3d = screen_tiles(stage3d_screen(), floor)
    open(os.path.join(ROOT, 'chr', 'floor.chr'), 'wb').write(bytes(floor.data))
    json.dump({'version': 1, 'system': 'nes', 'chr': '../chr/floor.chr', 'palettes': 'stage3d.pal.json', 'prefix': 'scr_',
               'name': 'stage3d', 'compression': 'none', 'tiles': cells3d, 'attributes': attrs3d},
              open(os.path.join(gfx, 'stage3d.screen.json'), 'w', newline='\n'), indent=2)
    p3 = dict(pal)
    p3 = {'version': 1, 'system': 'nes', 'name': 'stage3d', 'prefix': 's3d_', 'palettes': [
        {'name': 'sky', 'colors': [0x0F, 0x30, 0x16, 0x28]},
        {'name': 'horizon', 'colors': [0x0F, 0x06, 0x16, 0x27]},
        {'name': 'floor', 'colors': [0x0F, 0x01, 0x11, 0x21]},
        {'name': 'unused', 'colors': [0x0F, 0x01, 0x11, 0x21]}] + pal['palettes'][4:]}
    json.dump(p3, open(os.path.join(gfx, 'stage3d.pal.json'), 'w', newline='\n'), indent=2)
    print('sprite tiles %d, all tiles $%02X-$%02X; floor tiles %d' % (sprite_tiles, FIRST, tiles.next - 1, floor.next))


if __name__ == '__main__':
    main()
