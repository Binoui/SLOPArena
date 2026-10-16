"""Log Off palette (sRGB, 0..1). Names describe roles, not exact pigments.

Sunset light is warm, so the fight surface stays a clear honey-cream that reads
under it; the forest falls back into olive, umber and dusk violet. Fighter rim
colours are amber and cyan: keep large cyan and amber areas off the stump top.
"""

# Cut stump top (stump-top texture)
# Calibrated in the normal Training camera, including the shared scene lights and
# ACES grade. Pale Eevee values wash out the rings and shelf bands in that rig.
EARLYWOOD = (0.70, 0.55, 0.37)
LATEWOOD = (0.50, 0.33, 0.20)
HEARTWOOD = (0.64, 0.46, 0.28)
SAPWOOD = (0.75, 0.61, 0.43)
CAMBIUM = (0.44, 0.27, 0.15)
BARK_RIM = (0.32, 0.22, 0.15)
CHECK = (0.30, 0.18, 0.10)
WEATHERED = (0.70, 0.64, 0.56)
FRESH_SPLIT = (0.77, 0.62, 0.43)

# Bark and split walls
BARK = (0.34, 0.25, 0.19)
BARK_LIGHT = (0.47, 0.37, 0.29)
BARK_DARK = (0.17, 0.12, 0.09)
LICHEN = (0.62, 0.67, 0.52)
SPLIT_WOOD = (0.82, 0.62, 0.40)
SPLIT_DARK = (0.55, 0.37, 0.22)

# Moss
MOSS = (0.34, 0.48, 0.17)
MOSS_LIGHT = (0.52, 0.64, 0.24)
MOSS_DARK = (0.19, 0.30, 0.11)

# Bracket fungus (turkey-tail bands from the bark to the pale growing lip)
FUNGUS_BANDS = [(0.26, 0.15, 0.09), (0.46, 0.23, 0.10), (0.60, 0.44, 0.26), (0.70, 0.63, 0.48),
                (0.39, 0.32, 0.27), (0.55, 0.29, 0.13), (0.65, 0.53, 0.35), (0.78, 0.74, 0.64)]
FUNGUS_PORE = (0.68, 0.59, 0.43)
FUNGUS_PORE_DARK = (0.52, 0.40, 0.27)

# Forest floor
SOIL = (0.25, 0.18, 0.12)
SOIL_DARK = (0.15, 0.10, 0.07)
LITTER = [(0.66, 0.45, 0.18), (0.60, 0.28, 0.12), (0.44, 0.28, 0.15), (0.80, 0.60, 0.22),
          (0.52, 0.36, 0.18), (0.70, 0.36, 0.14)]
NEEDLES = (0.55, 0.40, 0.23)
TWIG = (0.34, 0.25, 0.18)

# Plants and fungi
FERN = (0.30, 0.48, 0.16)
FERN_LIGHT = (0.46, 0.62, 0.22)
FERN_DARK = (0.17, 0.30, 0.11)
GRASS = (0.42, 0.56, 0.21)
GRASS_DRY = (0.68, 0.62, 0.34)
AMANITA = (0.82, 0.15, 0.08)
AMANITA_DARK = (0.60, 0.09, 0.05)
SPOT = (0.96, 0.93, 0.86)
STEM = (0.91, 0.87, 0.77)
BOLETE = (0.52, 0.31, 0.15)
BOLETE_PORE = (0.80, 0.72, 0.42)
BONNET = (0.72, 0.58, 0.40)
PINECONE = (0.44, 0.27, 0.14)
PINECONE_TIP = (0.64, 0.46, 0.27)
ACORN = (0.56, 0.38, 0.16)
ACORN_CAP = (0.40, 0.30, 0.18)

# Trees
TRUNK = (0.36, 0.28, 0.22)
TRUNK_DARK = (0.21, 0.16, 0.12)
TRUNK_LIGHT = (0.46, 0.38, 0.30)
CANOPY = (0.17, 0.27, 0.12)
CANOPY_LIGHT = (0.30, 0.40, 0.15)
CANOPY_DARK = (0.10, 0.16, 0.08)

# Camp
STONE = (0.50, 0.49, 0.47)
STONE_DARK = (0.33, 0.32, 0.31)
ASH = (0.30, 0.29, 0.28)
CHAR = (0.10, 0.08, 0.07)
EMBER = (1.00, 0.40, 0.10)
FLAME_CORE = (1.00, 0.88, 0.50)
FLAME = (1.00, 0.56, 0.16)
FLAME_TIP = (0.96, 0.30, 0.07)
MARSHMALLOW = (0.97, 0.95, 0.90)
TENT = (0.86, 0.42, 0.18)
TENT_DARK = (0.60, 0.27, 0.12)
TENT_FLOOR = (0.24, 0.30, 0.32)
TENT_POLE = (0.20, 0.20, 0.22)
TENT_INSIDE = (1.00, 0.70, 0.40)
GUY_LINE = (0.86, 0.84, 0.76)
CHAIR_FABRIC = (0.18, 0.40, 0.33)
CHAIR_FRAME = (0.15, 0.15, 0.16)
LANTERN = (0.17, 0.25, 0.20)
LANTERN_GLOW = (1.00, 0.80, 0.46)
BRASS = (0.78, 0.60, 0.30)
MUG = (0.22, 0.42, 0.68)
MUG_RIM = (0.10, 0.10, 0.12)
BOOT = (0.46, 0.29, 0.16)
BOOT_DARK = (0.30, 0.18, 0.10)
SOLE = (0.12, 0.11, 0.10)
LACE = (0.78, 0.16, 0.12)
PACK = (0.86, 0.62, 0.18)
PACK_DARK = (0.56, 0.38, 0.12)
STRAP = (0.18, 0.18, 0.20)
AXE_HEAD = (0.30, 0.31, 0.34)
AXE_EDGE = (0.80, 0.82, 0.84)
AXE_HANDLE = (0.84, 0.64, 0.40)
AXE_PAINT = (0.76, 0.15, 0.10)
CHIP = (0.90, 0.76, 0.54)
SIGN_BOARD = (0.62, 0.45, 0.28)
SIGN_PAINT = (0.95, 0.93, 0.86)
SIGN_POST = (0.42, 0.30, 0.19)
PHONE = (0.07, 0.07, 0.08)
PHONE_EDGE = (0.30, 0.31, 0.34)

# Sky (unlit) and glow
SKY_ZENITH = (0.20, 0.22, 0.44)
SKY_HIGH = (0.42, 0.36, 0.60)
SKY_MID = (0.78, 0.52, 0.58)
SKY_LOW = (0.99, 0.66, 0.44)
SKY_HORIZON = (1.00, 0.80, 0.52)
SKY_EAST_LOW = (0.62, 0.52, 0.64)
SUN = (1.00, 0.93, 0.72)
SUN_HALO = (1.00, 0.74, 0.44)
FIREFLY = (0.86, 1.00, 0.48)
