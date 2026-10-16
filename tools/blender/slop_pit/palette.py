"""Slop Pit palette (sRGB, 0..1). Names describe roles, not exact pigments.

The fight surfaces stay neutral (warm canvas, grey concrete) under warm-white light so
fighters and their amber/cyan rims read first; orange haze, fire and lamps live in the
stands beyond the blast corridor, and the slop's sickly green stays below the kill plane.
Brand anchors follow docs/design/visual-language.md: Ink, Paper, Action Yellow, Orange.
"""

# Brand
INK = (0.106, 0.102, 0.094)
PAPER = (0.94, 0.93, 0.91)
ACTION_YELLOW = (1.0, 0.80, 0.133)
ORANGE = (0.95, 0.42, 0.10)
RED = (0.66, 0.13, 0.11)
BLUE = (0.16, 0.27, 0.62)

# Fight surfaces (lit by the stage's own lights)
APRON = (0.12, 0.11, 0.11)
SKIRT = (0.09, 0.085, 0.08)
KICK_PLATE = (0.20, 0.20, 0.21)
TOWER_DARK = (0.13, 0.13, 0.14)
TOWER_STEEL = (0.25, 0.25, 0.27)
TOWER_CONCRETE = (0.21, 0.205, 0.20)
PLYWOOD = (0.62, 0.50, 0.34)

# Venue structure (unlit night base; baked AO and fog finish it)
CONCRETE = (0.34, 0.32, 0.30)
CONCRETE_DARK = (0.20, 0.19, 0.18)
CONCRETE_WET = (0.16, 0.17, 0.15)
STEEL = (0.30, 0.30, 0.32)
STEEL_DARK = (0.14, 0.14, 0.15)
SCAFFOLD = (0.42, 0.42, 0.40)
GALV = (0.50, 0.51, 0.52)
RUST = (0.42, 0.22, 0.11)
DECK_BOARD = (0.30, 0.25, 0.19)
TRUSS = (0.36, 0.36, 0.38)
CLOTH_BLACK = (0.08, 0.075, 0.07)

# Light and glow (StageNeon: vertex alpha marks glow)
LAMP_WARM = (1.0, 0.76, 0.42)
LAMP_WHITE = (1.0, 0.93, 0.80)
LAMP_AMBER = (1.0, 0.58, 0.20)
HAZE_HOT = (1.0, 0.55, 0.18)
HAZE_DEEP = (0.55, 0.22, 0.07)
FIRE = (1.0, 0.52, 0.12)
FIRE_CORE = (1.0, 0.82, 0.40)
EXIT_RED = (1.0, 0.16, 0.10)
PHONE = (0.78, 0.86, 1.0)
NEON_YELLOW = (1.0, 0.82, 0.20)
NEON_ORANGE = (1.0, 0.45, 0.10)
BULB = (1.0, 0.80, 0.50)

# The slop (below the kill plane)
SLOP = (0.16, 0.20, 0.05)
SLOP_DARK = (0.055, 0.07, 0.02)
SLOP_FOAM = (0.42, 0.50, 0.22)
SLOP_GLOW = (0.62, 0.95, 0.18)

# Crowd: dark silhouettes against the orange backlight, a few muted fan colours, varied skin
CLOTHES = [(0.05, 0.05, 0.055), (0.08, 0.07, 0.06), (0.05, 0.06, 0.09), (0.11, 0.045, 0.04),
           (0.07, 0.075, 0.05), (0.12, 0.11, 0.10), (0.04, 0.04, 0.045), (0.09, 0.08, 0.10)]
FAN_CLOTHES = [(0.42, 0.32, 0.07), (0.38, 0.17, 0.05), (0.28, 0.06, 0.05), (0.08, 0.12, 0.27)]
SKIN = [(0.38, 0.28, 0.22), (0.30, 0.21, 0.15), (0.20, 0.14, 0.10), (0.44, 0.34, 0.27), (0.33, 0.24, 0.18)]
HAIR = [(0.03, 0.025, 0.02), (0.09, 0.055, 0.03), (0.15, 0.11, 0.06), (0.05, 0.045, 0.045)]
