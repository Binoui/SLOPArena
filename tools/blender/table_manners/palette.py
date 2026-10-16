"""Table Manners palette (sRGB, 0..1). Names describe roles, not exact pigments.

Fighter rim colours are amber (local player) and cyan (opponents), so the fight
surface uses cream, raspberry and lemon; saturated cyan, sky blue and orange stay
in the room and the garden. Background values stay quieter than the tabletop.
"""

# Fight surface: wipe-clean gingham oilcloth, drawing paper, the play-dough cake
CLOTH = (0.96, 0.90, 0.87)
CLOTH_STRIPE = (0.94, 0.72, 0.76)
CLOTH_CROSS = (0.86, 0.50, 0.59)
CLOTH_BORDER = (0.84, 0.29, 0.44)
CLOTH_BORDER_DARK = (0.66, 0.20, 0.34)
CLOTH_SCALLOP = (0.99, 0.96, 0.93)
CLOTH_HEM = (0.98, 0.84, 0.30)
PAPER = (0.98, 0.98, 0.96)
PAPER_SHADE = (0.90, 0.90, 0.88)
CARD_PINK = (0.98, 0.70, 0.80)
CARD_LEMON = (0.99, 0.91, 0.52)
CARD_LILAC = (0.80, 0.72, 0.96)
CARD_MINT = (0.70, 0.92, 0.78)
PLATE = (0.99, 0.99, 0.98)
PLATE_RIM = (0.88, 0.90, 0.92)
DOUGH_PINK = (0.97, 0.50, 0.66)
DOUGH_LILAC = (0.70, 0.55, 0.93)
DOUGH_LEMON = (0.99, 0.87, 0.40)
DOUGH_WHITE = (0.99, 0.97, 0.94)
DOUGH_RED = (0.92, 0.22, 0.25)
DOUGH_GREEN = (0.42, 0.80, 0.38)
GOOGLY_WHITE = (0.99, 0.99, 0.99)
GOOGLY_BLACK = (0.05, 0.05, 0.06)
GLITTER = [(1.00, 0.85, 0.40), (1.00, 0.70, 0.88), (0.85, 0.85, 0.95), (0.80, 0.70, 1.00)]

# Crayons and craft colours
CRAYON = {
    "red": (0.88, 0.18, 0.20), "orange": (0.97, 0.52, 0.16), "yellow": (0.99, 0.85, 0.18),
    "green": (0.22, 0.68, 0.30), "blue": (0.20, 0.42, 0.86), "purple": (0.52, 0.30, 0.76),
    "pink": (0.96, 0.45, 0.68), "brown": (0.52, 0.32, 0.18), "black": (0.12, 0.12, 0.14),
    "sky": (0.40, 0.72, 0.96), "lime": (0.60, 0.86, 0.22),
}
CRAYON_WRAP_DARK = 0.55

# Toys (primary plastic)
TOY_RED = (0.90, 0.20, 0.18)
TOY_YELLOW = (0.99, 0.80, 0.16)
TOY_BLUE = (0.18, 0.45, 0.88)
TOY_GREEN = (0.25, 0.70, 0.35)
TOY_ORANGE = (0.98, 0.55, 0.15)
TOY_PURPLE = (0.55, 0.32, 0.80)
TOY_WHITE = (0.95, 0.95, 0.93)
TOY_PINK = (0.97, 0.55, 0.72)
PLUSH_BROWN = (0.66, 0.45, 0.30)
PLUSH_CREAM = (0.95, 0.88, 0.76)

# Room
FLOOR = (0.80, 0.58, 0.36)
FLOOR_LIGHT = (0.86, 0.65, 0.42)
FLOOR_DARK = (0.68, 0.47, 0.28)
FLOOR_GAP = (0.40, 0.26, 0.15)
WALL_SKY = (0.70, 0.84, 0.95)
WALL_CLOUD = (0.93, 0.96, 0.99)
WAINSCOT = (0.97, 0.95, 0.89)
WAINSCOT_SHADE = (0.88, 0.86, 0.80)
CHAIR_RAIL = (0.96, 0.58, 0.48)
SKIRTING = (0.98, 0.97, 0.94)
CEILING = (0.97, 0.97, 0.95)
GLOW_STAR = (0.86, 1.00, 0.62)
TRIM_WHITE = (0.98, 0.98, 0.96)
PINE = (0.88, 0.70, 0.46)
PINE_DARK = (0.72, 0.54, 0.33)
PAINT_WHITE = (0.96, 0.96, 0.94)
TOYBOX_RED = (0.86, 0.24, 0.22)
TOYBOX_LID = (0.99, 0.80, 0.22)
BEDDING_BLUE = (0.34, 0.56, 0.90)
BEDDING_STAR = (0.99, 0.88, 0.40)
BEDDING_GREEN = (0.52, 0.80, 0.56)
PILLOW = (0.98, 0.97, 0.95)
CURTAIN = (0.99, 0.86, 0.36)
CURTAIN_DOT = (0.99, 0.98, 0.94)
RUG_RINGS = [(0.96, 0.55, 0.50), (0.99, 0.82, 0.40), (0.62, 0.86, 0.58), (0.52, 0.76, 0.96), (0.78, 0.66, 0.95)]
RUG_FRINGE = (0.98, 0.96, 0.90)
MAT_GRASS = (0.48, 0.76, 0.40)
MAT_ROAD = (0.34, 0.35, 0.38)
MAT_LINE = (0.98, 0.98, 0.94)
TRACK_WOOD = (0.84, 0.66, 0.42)
BRICKS = [TOY_RED, TOY_YELLOW, TOY_BLUE, TOY_GREEN, TOY_WHITE, TOY_ORANGE]
GIFT = [((0.95, 0.40, 0.55), (0.99, 0.88, 0.40)), ((0.40, 0.62, 0.95), (0.98, 0.98, 0.95)),
        ((0.55, 0.82, 0.48), (0.96, 0.36, 0.40)), ((0.75, 0.58, 0.95), (0.99, 0.86, 0.36))]
BALLOONS = [(0.93, 0.20, 0.28), (0.99, 0.80, 0.20), (0.24, 0.52, 0.92), (0.97, 0.52, 0.72),
            (0.36, 0.78, 0.42), (0.62, 0.40, 0.88), (0.99, 0.58, 0.22)]
STRING = (0.95, 0.95, 0.92)
BUNTING = [(0.93, 0.25, 0.30), (0.99, 0.82, 0.22), (0.26, 0.56, 0.92), (0.38, 0.78, 0.44),
           (0.97, 0.56, 0.74), (0.62, 0.42, 0.88)]

# Garden through the window
SKY_TOP = (0.36, 0.62, 0.95)
SKY_HORIZON = (0.78, 0.89, 0.99)
SUN = (1.00, 0.96, 0.74)
CLOUD = (0.99, 0.99, 1.00)
CLOUD_SHADE = (0.86, 0.90, 0.97)
GRASS = (0.45, 0.74, 0.33)
GRASS_DARK = (0.35, 0.62, 0.27)
HILL_FAR = (0.56, 0.76, 0.56)
HILL_FARTHER = (0.66, 0.80, 0.70)
TREE = (0.30, 0.60, 0.28)
TREE_LIGHT = (0.42, 0.70, 0.32)
TRUNK = (0.48, 0.32, 0.20)
FENCE = (0.97, 0.97, 0.95)
SWING_POST = (0.86, 0.26, 0.24)
SWING_SEAT = (0.24, 0.48, 0.88)
SLIDE = (0.99, 0.80, 0.18)
SAND = (0.94, 0.84, 0.60)
HOUSE_WALLS = [(0.96, 0.88, 0.76), (0.86, 0.90, 0.96), (0.98, 0.80, 0.74), (0.90, 0.94, 0.84)]
ROOF_TILES = [(0.74, 0.30, 0.24), (0.36, 0.40, 0.50), (0.60, 0.34, 0.26)]
FLOWERS = [(0.98, 0.40, 0.50), (0.99, 0.86, 0.30), (0.96, 0.96, 0.98), (0.70, 0.50, 0.95)]
