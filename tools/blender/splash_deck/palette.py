"""Splash Deck palette (sRGB, 0..1). Names describe roles, not exact pigments.

Sunny late morning in a Mediterranean fishing harbour. The honey teak deck carries
the strongest value contrast in the frame; the bay falls back into cobalt water,
pastel plaster, terracotta, olive and haze. Fighter rim colours are amber and cyan:
keep large saturated cyan and amber areas off the deck.
"""

# Deck (deck texture): bleached teak with dark caulking
TEAK = (0.74, 0.58, 0.38)
TEAK_LIGHT = (0.84, 0.70, 0.50)
TEAK_DARK = (0.56, 0.41, 0.25)
TEAK_GREY = (0.70, 0.64, 0.55)
CAULK = (0.16, 0.12, 0.09)
DECK_WET = (0.45, 0.33, 0.22)
SALT = (0.88, 0.86, 0.80)
MARGIN_PAINT = (0.70, 0.16, 0.12)   # painted covering board at the deck edge

# Hull (hull texture): luzzu-style bands, top to bottom
GUNWALE = (0.60, 0.42, 0.24)        # varnished cap rail
SHEER_YELLOW = (0.96, 0.74, 0.18)
SHEER_RED = (0.78, 0.14, 0.10)
RUB_RAIL = (0.30, 0.20, 0.13)
HULL_BLUE = (0.10, 0.36, 0.66)
HULL_BLUE_DARK = (0.06, 0.24, 0.48)
HULL_STRIPE = (0.95, 0.94, 0.88)
HULL_GREEN = (0.12, 0.52, 0.36)
BOOT_TOP = (0.95, 0.95, 0.92)
ANTIFOUL = (0.62, 0.14, 0.10)
RUST = (0.52, 0.25, 0.12)
BRASS = (0.80, 0.62, 0.28)
PORTHOLE_GLASS = (0.10, 0.14, 0.18)
EYE_WHITE = (0.97, 0.96, 0.90)
EYE_BLACK = (0.06, 0.06, 0.07)
EYE_RED = (0.80, 0.12, 0.10)
NAME_PAINT = (0.98, 0.96, 0.90)

# Wheelhouse and fish hatch
CABIN_WHITE = (0.93, 0.92, 0.86)
CABIN_TRIM = (0.10, 0.36, 0.66)
CABIN_ROOF = (0.84, 0.86, 0.82)
ROOF_NUMBER = (0.12, 0.30, 0.56)
WINDOW_GLASS = (0.16, 0.24, 0.30)
WINDOW_SKY = (0.62, 0.78, 0.90)
WHEEL_WOOD = (0.50, 0.30, 0.15)
SIGN_BOARD = (0.95, 0.93, 0.84)
SIGN_PAINT = (0.70, 0.12, 0.10)
SIGN_PAINT_DARK = (0.10, 0.20, 0.42)
COAMING = (0.70, 0.18, 0.13)
COAMING_DARK = (0.48, 0.12, 0.09)
TARP = (0.20, 0.42, 0.40)
TARP_DARK = (0.12, 0.28, 0.27)
ROPE = (0.86, 0.78, 0.58)
ROPE_DARK = (0.56, 0.48, 0.33)
NET = (0.25, 0.52, 0.42)
NET_FLOAT = (0.96, 0.52, 0.14)
FISH = (0.70, 0.76, 0.80)
FISH_DARK = (0.32, 0.40, 0.48)
FISH_BELLY = (0.92, 0.92, 0.88)
STEEL = (0.42, 0.44, 0.46)
STEEL_DARK = (0.22, 0.23, 0.24)

# Sea (vertex colours on SLOPArena/StageWater)
SEA_DEEP = (0.05, 0.24, 0.44)
SEA_BAY = (0.07, 0.33, 0.52)
SEA_SHALLOW = (0.13, 0.55, 0.60)
SEA_REEF = (0.30, 0.66, 0.62)
SEA_FAR = (0.12, 0.34, 0.54)
FOAM = (0.96, 0.98, 0.98)

# Land
LIMESTONE = (0.70, 0.66, 0.57)
LIMESTONE_DARK = (0.48, 0.44, 0.38)
ROCK = (0.52, 0.49, 0.44)
ROCK_DARK = (0.38, 0.35, 0.31)
WET_ROCK = (0.30, 0.28, 0.25)
SAND = (0.90, 0.82, 0.62)
SAND_WET = (0.70, 0.62, 0.46)
SCRUB = (0.38, 0.38, 0.24)
SCRUB_DRY = (0.58, 0.54, 0.36)
GRASS = (0.38, 0.42, 0.22)
EARTH = (0.56, 0.43, 0.29)
OLIVE = (0.46, 0.52, 0.36)
OLIVE_DARK = (0.30, 0.36, 0.24)
OLIVE_TRUNK = (0.36, 0.32, 0.26)
CYPRESS = (0.14, 0.26, 0.16)
CYPRESS_LIGHT = (0.22, 0.36, 0.20)
PINE = (0.20, 0.34, 0.20)
PINE_LIGHT = (0.30, 0.44, 0.24)
PINE_TRUNK = (0.42, 0.30, 0.22)
MAQUIS = (0.20, 0.30, 0.15)
MAQUIS_LIGHT = (0.30, 0.40, 0.20)

# Village plaster, roofs and trims
PLASTER = [(0.94, 0.80, 0.58), (0.96, 0.70, 0.52), (0.93, 0.62, 0.55), (0.98, 0.88, 0.60),
           (0.95, 0.93, 0.86), (0.86, 0.56, 0.38), (0.97, 0.78, 0.70), (0.80, 0.84, 0.80),
           (0.90, 0.74, 0.46), (0.98, 0.84, 0.76)]
TERRACOTTA = (0.74, 0.36, 0.22)
TERRACOTTA_DARK = (0.56, 0.25, 0.16)
SHUTTER = [(0.16, 0.42, 0.30), (0.18, 0.34, 0.52), (0.40, 0.52, 0.30), (0.62, 0.20, 0.14)]
WINDOW_DARK = (0.14, 0.13, 0.13)
DOOR = (0.38, 0.24, 0.16)
STONE_QUAY = (0.72, 0.68, 0.60)
STONE_QUAY_DARK = (0.50, 0.47, 0.41)
BOLLARD = (0.18, 0.19, 0.20)
AWNING = [(0.94, 0.94, 0.90), (0.20, 0.42, 0.70)]
AWNING_RED = (0.78, 0.18, 0.14)
FLOWER = [(0.86, 0.16, 0.36), (0.94, 0.44, 0.18), (0.86, 0.10, 0.12), (0.80, 0.40, 0.70)]
LAUNDRY = [(0.95, 0.95, 0.92), (0.30, 0.52, 0.80), (0.92, 0.80, 0.30), (0.86, 0.36, 0.40)]
BELL = (0.64, 0.50, 0.24)
CHURCH = (0.96, 0.92, 0.82)
CHURCH_DOME = (0.86, 0.56, 0.34)

# Lighthouse and breakwater
LIGHTHOUSE_WHITE = (0.96, 0.95, 0.90)
LIGHTHOUSE_RED = (0.80, 0.14, 0.12)
LANTERN_GLASS = (0.60, 0.74, 0.80)
LANTERN_ROOF = (0.20, 0.22, 0.24)
LAMP = (1.00, 0.92, 0.70)
BREAKWATER = (0.66, 0.63, 0.57)
BREAKWATER_DARK = (0.44, 0.42, 0.38)
CONCRETE = (0.78, 0.76, 0.70)

# Small boats (moored)
BOAT_HULLS = [(0.10, 0.36, 0.66), (0.92, 0.92, 0.88), (0.12, 0.50, 0.36), (0.80, 0.18, 0.14),
              (0.96, 0.74, 0.18), (0.20, 0.62, 0.70)]
BOAT_TRIM = [(0.96, 0.74, 0.18), (0.80, 0.18, 0.14), (0.95, 0.94, 0.88), (0.10, 0.36, 0.66)]
SAIL = (0.97, 0.96, 0.92)
MAST = (0.86, 0.84, 0.80)
BUOY = [(0.96, 0.44, 0.10), (0.94, 0.86, 0.20), (0.92, 0.18, 0.14), (0.95, 0.94, 0.90)]

# Sky (unlit) and creatures
SKY_ZENITH = (0.22, 0.47, 0.82)
SKY_HIGH = (0.36, 0.60, 0.88)
SKY_MID = (0.56, 0.74, 0.92)
SKY_LOW = (0.76, 0.86, 0.94)
SKY_HORIZON = (0.88, 0.92, 0.94)
SKY_SUN_SIDE = (0.96, 0.95, 0.88)
SUN = (1.00, 0.98, 0.90)
SUN_HALO = (1.00, 0.95, 0.80)
CLOUD = (0.98, 0.98, 0.97)
CLOUD_SHADE = (0.80, 0.85, 0.92)
GULL = (0.95, 0.95, 0.94)
GULL_WING = (0.70, 0.73, 0.76)
GULL_TIP = (0.12, 0.12, 0.13)
HAZE = (0.78, 0.85, 0.90)
