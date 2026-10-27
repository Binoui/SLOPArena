"""Write the salamander's GenRA move briefs (tools/genra/briefs/salamander-*.json) from one kit table.

    artifacts/genra-20261014/.venv/bin/python tools/blender/salamander_rig/make_briefs.py [--pack salamander-unity-moves]

His kit proposal (2026-10-27, the owner's "full character proposal" test): the Wannabe Legend, a make-believe gunslinger
whose whole act is mime (invisible holsters, an invisible gun, BANG said out loud). Ground moves start and end on his
showdown idle (reference `showdown` in the pack, matched); aerials start on the borrowed fall. Each move's clock is the
gameplay timing proposed in tmp/salamander-gunslinger/proposal/kit.md; the briefs restate it.
"""
import argparse, json, sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO / 'tools'))
from genra.brief import Brief  # noqa: E402

ap = argparse.ArgumentParser()
ap.add_argument('--pack', default='salamander-unity-moves')
args = ap.parse_args()
PACK = f'artifacts/genra-rigpacks/{args.pack}'
IMG = '../../salamander-idle-run-20261027'

# Measure the showdown idle's frame: the floor and the stance, in the character frame of its tick 0.
probe = {'name': 'probe', 'rigpack': PACK, 'character': '', 'description': '',
         'references': {'showdown': 'references/showdown.usda'}, 'entry': {'reference': 'showdown', 'tick': 0},
         'clock': {'duration_ticks': 10}}
tmp = REPO / 'tmp/salamander-gunslinger/proposal/probe-brief.json'
tmp.write_text(json.dumps(probe))
e = Brief(tmp).entry_pose
toe = min(e.pos('LeftToeBase')[2], e.pos('RightToeBase')[2])
FLOOR_UP = round(toe - 0.008, 3)
HIPS_UP = round(-FLOOR_UP, 3)
RH = [round(float(x), 3) for x in e.pos('RightHand')]
LH = [round(float(x), 3) for x in e.pos('LeftHand')]
print('showdown frame: floor up', FLOOR_UP, 'hips above floor', HIPS_UP, 'right hand', RH, 'left hand', LH)

CHAR = ("The salamander gunslinger (55 joints: a Humanoid body with Mixamo names; each hand has a thumb and THREE fingers "
        "(Thumb, Index, Middle, Ring chains), no little finger; five tail joints Tail1-Tail5 under Hips; three rigid joints under "
        "Head: Hat (his sombrero, its own mesh), Mustache (the fake mustache on his snout) and Straw (a wheat straw in the left "
        "corner of his mouth)). He is lanky: in the rig his hip joints stand 0.42 m above the floor when straight, thigh 0.176 m, "
        "shin 0.137 m, upper arm 0.158 m, forearm 0.093 m, long hands (about 0.15 m wrist to fingertip); 0.95 m to the top of his "
        "head, 1.13 m with the hat (the game draws him 1.8 times bigger). A thick tail hangs behind him from the seat of his jeans. "
        "His poncho is not in this rig (it hangs from his spine in the game and his arms come out from under it): pose the body only.")
WHO = ("Who he is. A tiger salamander (image 1): olive-black skin with yellow blotches, a smirk, a fake handlebar mustache, a "
       "straw in his mouth, a wide brown sombrero, a rust-orange poncho, faded patched jeans with his tail out through the seat "
       "patch, brown boots with star spurs. He is the Wannabe Legend: a make-believe gunslinger who never owned a gun and plays "
       "every Western cliche with total conviction. Everything is mime: invisible holsters, an invisible gun (finger guns: index "
       "and middle fingers straight out together as the barrel, thumb up as the hammer, ring finger curled), invisible lasso, and he says BANG himself. "
       "No props. His moves are cartoony, snappy and readable from behind: big clear silhouettes, quick anticipation, a held "
       "pose on the hit, then a cocky settle.")
FLOOR = (f"The floor. It is at up = {FLOOR_UP} m in this character frame (origin: the hips of his idle at tick 0). With a flat "
         f"boot the toe joint (ToeBase) sits about 0.008 m above the floor and the ankle (Foot) about 0.078 m above it. Standing "
         f"straight his hip joint is 0.42 m above the floor. Never push a boot through the floor.")
STANCE = ("His idle, where ground moves start and end (reference `showdown`, the entry pose): the showdown stance, boots planted "
          "wide, knees bent, hands hovering out at his hips over invisible holsters, facing front.")
GAME = ("How the game plays it. The match camera sits straight behind him, so the action must read from behind: arms out to "
        "the side or up, not hidden in front of the chest. The fighter does not travel in the clip unless said; the game moves "
        "him. The game crossfades in and out over about 0.1 s.")

def floor(name, tick, joint):
    return {'name': f'{name} (m)', 'kind': 'char', 'a': joint, 'axis': 'up', 'tick': tick,
            'min': FLOOR_UP - 0.004, 'max': FLOOR_UP + 0.045}

def ground(name, **kw):
    b = {'name': f'salamander-{name}', 'rigpack': PACK, 'character': CHAR,
         'references': {'showdown': 'references/showdown.usda', 'idle': 'references/idle.usda', 'fall': 'references/fall.usda',
                        'run': 'references/run.usda'},
         'entry': {'reference': 'showdown', 'tick': 0}, 'views': ['profile', 'three-quarter-right', 'gameplay'],
         'images': [f'{IMG}/salamander-model.png'],
         'image_notes': '1: the salamander model with his arms down (front, three-quarter, side). His poncho is not in the rig.'}
    b.update(kw)
    return b

def air(name, **kw):
    b = ground(name, **kw)
    b['entry'] = {'reference': 'fall', 'tick': 0}
    b['references'] = dict(b['references'])
    return b

BRIEFS = []

# --- ground.1: Pistol Whip (jab/all-rounder) --------------------------------------------------------------
BRIEFS.append(ground('g1-pistol-whip',
  notes='Salamander ground.1 (anim.salamander.g1), kit proposal 2026-10-27: Pistol Whip, his jab. Timing kept from the '
        'borrowed Monkey Punch: 35 ticks, hit 9-14, act from 18.',
  description='\n\n'.join([
    "The salamander's ground.1, Pistol Whip: his fast jab. He backhands the opponent with his finger gun, like a gunslinger "
    "pistol-whipping someone with the butt of a revolver.", WHO, GAME, FLOOR, STANCE,
    "The move (35 ticks, played once):\n- Tick 0: the showdown stance.\n- Tick 5, the draw: his right hand snaps up from the "
    "hip into a finger gun beside his right ear, elbow high and out to the side, chest turned 20-30 degrees to his right, "
    "weight onto the right boot.\n- Tick 9, contact: he whips the finger gun down and across in front of him, a backhand blow "
    "with the back of the hand leading, from his right ear to in front of his left hip; the arm nearly straight at the hit, "
    "the hand at chest height 0.35-0.5 m in front of his chest; chest turns through to 15-25 degrees to his left; weight "
    "shifts onto the front (left) boot.\n- Ticks 9-14: the hit carries through; the hand ends low in front of his left hip.\n"
    "- Tick 18: recovered enough to act: the hand comes back toward the right holster, a cocky little head tilt.\n- Tick 35: "
    "the showdown stance again, matching tick 0.\nThe left hand stays hovering at its holster. Feet stay planted (the boots "
    "may pivot a little on their soles). Fingers: the finger gun all the way from tick 3 to tick 16."]),
  clock={'duration_ticks': 35, 'contact_tick': 9, 'active': [9, 14], 'iasa_tick': 18},
  timing={'fixed': ['35 ticks played once; contact at tick 9, the hitbox is on his right hand from tick 9 to 14',
                    'the player can act from tick 18', 'tick 0 and tick 35 are his showdown idle exactly'],
          'adjustable': ['nothing else; keep the wind-up short, it is his fastest move']},
  exit={'reference': 'showdown', 'tick': 0, 'from_tick': 35}, fast_windows=[[5, 12]], strike=['RightHand'],
  keys=[0, 5, 9, 14, 18, 35], review_ticks=[0, 3, 5, 7, 9, 11, 14, 18, 26, 35],
  targets=[floor('left toe on the floor at contact', 9, 'LeftToeBase'), floor('right toe on the floor at contact', 9, 'RightToeBase'),
           {'name': 'draw: right hand up by the ear at tick 5 (m)', 'kind': 'below', 'a': 'Neck', 'b': 'RightHand', 'tick': 5, 'min': -0.08},
           {'name': 'contact: right hand in front of the chest at tick 9 (m)', 'kind': 'ahead', 'a': 'RightHand', 'b': 'Spine2', 'tick': 9, 'min': 0.28},
           {'name': 'contact: right hand crossed toward his left at tick 11 (m)', 'kind': 'char', 'a': 'RightHand', 'axis': 'right', 'tick': 11, 'max': 0.05}]))

# --- ground.3: Stick 'Em Up! (up / launcher) -------------------------------------------------------------
BRIEFS.append(ground('g3-stick-em-up',
  notes="Salamander ground.3 (anim.salamander.g3), kit proposal 2026-10-27: Stick 'Em Up!, his up normal (launcher). "
        'Timing kept from the borrowed Sweeping Kick: 44 ticks, hit 9-20, act from 29.',
  description='\n\n'.join([
    "The salamander's ground.3, Stick 'Em Up!: his launcher. He crouches and then thrusts BOTH finger guns straight up over "
    "his head, as if shouting 'Stick 'em up!' at the sky: the hands-up gag turned into an uppercut.", WHO, GAME, FLOOR, STANCE,
    "The move (44 ticks, played once):\n- Tick 0: the showdown stance.\n- Tick 5, the dip: knees bend deep (hips drop 6-9 cm), "
    "both hands pull down to his hips in finger guns, chest leaning forward a little.\n- Tick 9, contact: he springs up onto "
    "his toes (heels off the floor, legs straight), both arms thrust up and out in a wide V, finger guns pointing at the "
    "sky. His sombrero's brim is 0.58 m wide (0.29 m to each side of his head): the arms must pass OUTSIDE it, never through "
    "it, so each hand ends at least 0.34 m out to its side of the head and above the brim; chest up, chin only a little up "
    "(the hat stays on, tipping back a few degrees at most).\n- Ticks 9-20: he holds the hands-up pose "
    "(a moving hold: the hands drift up 2-3 cm, a tiny wobble on the toes).\n- Tick 29: he comes down onto his heels, arms "
    "lowering, recovered enough to act.\n- Tick 44: the showdown stance again, matching tick 0.\nFeet stay on the floor "
    "(only the heels lift). Fingers: finger guns from tick 4 to tick 26."]),
  clock={'duration_ticks': 44, 'contact_tick': 9, 'active': [9, 20], 'iasa_tick': 29},
  timing={'fixed': ['44 ticks played once; contact at tick 9, the hitbox is on both hands above his head from tick 9 to 20',
                    'the player can act from tick 29', 'tick 0 and tick 44 are his showdown idle exactly'],
          'adjustable': ['the dip may be deeper or shallower']},
  exit={'reference': 'showdown', 'tick': 0, 'from_tick': 44}, fast_windows=[[5, 10]], strike=['RightHand', 'LeftHand'],
  keys=[0, 5, 9, 20, 29, 44], review_ticks=[0, 3, 5, 7, 9, 14, 20, 25, 29, 36, 44],
  targets=[floor('left toe joint on the floor at the dip', 5, 'LeftToeBase'), floor('right toe joint on the floor at the dip', 5, 'RightToeBase'),
           {'name': 'dip: hips low at tick 5 (m)', 'kind': 'char', 'a': 'Hips', 'axis': 'up', 'tick': 5, 'max': -0.05},
           {'name': 'contact: right hand above the head at tick 9 (m)', 'kind': 'below', 'a': 'Head', 'b': 'RightHand', 'tick': 9, 'min': 0.1},
           {'name': 'contact: left hand above the head at tick 9 (m)', 'kind': 'below', 'a': 'Head', 'b': 'LeftHand', 'tick': 9, 'min': 0.1},
           {'name': 'hold: right hand still up at tick 18 (m)', 'kind': 'below', 'a': 'Head', 'b': 'RightHand', 'tick': 18, 'min': 0.08},
           {'name': 'V: right hand outside the brim at tick 9 (m)', 'kind': 'char', 'a': 'RightHand', 'axis': 'right', 'tick': 9, 'min': 0.34},
           {'name': 'V: left hand outside the brim at tick 9 (m)', 'kind': 'char', 'a': 'LeftHand', 'axis': 'right', 'tick': 9, 'max': -0.34},
           {'name': 'V: right hand outside the brim at tick 20 (m)', 'kind': 'char', 'a': 'RightHand', 'axis': 'right', 'tick': 20, 'min': 0.32},
           {'name': 'V: left hand outside the brim at tick 20 (m)', 'kind': 'char', 'a': 'LeftHand', 'axis': 'right', 'tick': 20, 'max': -0.32}]))

# --- ground.4: Lasso Crack (big) --------------------------------------------------------------------------
BRIEFS.append(ground('g4-lasso-crack',
  notes='Salamander ground.4 (anim.salamander.g4), kit proposal 2026-10-27: Lasso Crack, his big normal. New timing: 52 '
        'ticks, hit 26-31 (a long reach in front of his right hand), act from 44.',
  description='\n\n'.join([
    "The salamander's ground.4, Lasso Crack: his big, slow, long-reaching normal. He twirls an invisible lasso over his head "
    "and cracks it forward like a whip. There is no rope: the hand circles and the whip-crack reads from the arm.", WHO, GAME,
    FLOOR, STANCE,
    "The move (52 ticks, played once):\n- Tick 0: the showdown stance.\n- Ticks 4-20, the twirl: his right hand rises above "
    "his hat and circles flat above it, one and a half loops, the forearm and wrist doing the circling (the hand loosely closed "
    "as if holding a rope), elbow high; the left hand on his left hip; weight shifting with the loops; he grins up at the "
    "lasso. Tick 10 and tick 18 are the hand at the back of a loop, tick 14 at the front.\n- Tick 23, the wind: the right "
    "arm cocks back behind his right shoulder, chest turned 30-40 degrees to his right, weight on the back (right) boot.\n- "
    "Tick 26, the crack: the arm whips forward and down to point straight ahead at shoulder height, fully extended, the hand "
    "flicking open; chest turned 20 degrees to his left, weight onto the front (left) boot, which slides forward 10-15 cm.\n"
    "- Ticks 26-31: the whip holds out in front.\n- Tick 44: he pulls the invisible rope back in, hand-over-hand once, "
    "recovered enough to act.\n- Tick 52: the showdown stance again, matching tick 0."]),
  clock={'duration_ticks': 52, 'contact_tick': 26, 'active': [26, 31], 'iasa_tick': 44},
  timing={'fixed': ['52 ticks played once; contact at tick 26, the hitbox reaches out in front of his right hand from tick 26 to 31',
                    'the player can act from tick 44', 'tick 0 and tick 52 are his showdown idle exactly'],
          'adjustable': ['the twirl may be one or two loops']},
  exit={'reference': 'showdown', 'tick': 0, 'from_tick': 52}, fast_windows=[[22, 28]], strike=['RightHand'],
  keys=[0, 6, 10, 14, 18, 23, 26, 31, 44, 52], review_ticks=[0, 6, 10, 14, 18, 21, 23, 25, 26, 28, 31, 38, 44, 52],
  targets=[floor('right toe on the floor at the wind', 23, 'RightToeBase'), floor('left toe on the floor at the crack', 28, 'LeftToeBase'),
           {'name': 'twirl: right hand above the head at tick 14 (m)', 'kind': 'below', 'a': 'Head', 'b': 'RightHand', 'tick': 14, 'min': 0.1},
           {'name': 'wind: right hand behind the chest at tick 23 (m)', 'kind': 'ahead', 'a': 'RightHand', 'b': 'Spine2', 'tick': 23, 'max': 0.0},
           {'name': 'crack: right hand far in front at tick 26 (m)', 'kind': 'ahead', 'a': 'RightHand', 'b': 'Spine2', 'tick': 26, 'min': 0.3},
           {'name': 'crack: right hand at shoulder height at tick 26 (m)', 'kind': 'below', 'a': 'RightArm', 'b': 'RightHand', 'tick': 26, 'min': -0.12, 'max': 0.1}]))

# --- air.1: Double Tap ----------------------------------------------------------------------------------
BRIEFS.append(air('a1-double-tap',
  notes='Salamander air.1 (anim.salamander.a1), kit proposal 2026-10-27: Double Tap, his quick aerial. Timing kept from the '
        "borrowed Air Kick: 38 ticks, hits at 7-12 (right hand) and 18-23 (left hand), act from 33, landing lag 9.",
  description='\n\n'.join([
    "The salamander's air.1, Double Tap: two quick finger-gun jabs in the air, right then left, 'pew pew'.", WHO, GAME,
    "In the air. The entry pose is the fall he plays in the air (a borrowed pose): keep the hips where it has them and let the "
    "legs tuck under him, knees up, boots together, for the whole move. There is no floor contact.",
    "The move (38 ticks, played once):\n- Tick 0: the fall pose.\n- Tick 4: both hands pull back to his chest in finger "
    "guns, legs tucking.\n- Tick 7, first hit: the right finger gun jabs straight ahead at chest height, arm extended, chest "
    "turned 20 degrees to his left; the left hand stays pulled back at his chest.\n- Tick 12: the right hand pulls back.\n- "
    "Tick 18, second hit: the left finger gun jabs straight ahead, chest turned 20 degrees to his right; the right hand back at "
    "the chest.\n- Tick 23: the left hand pulls back, a cocky little spin of the right finger gun at his hip.\n- Tick 33: "
    "recovered.\n- Tick 38: back to the fall pose, matching tick 0."]),
  clock={'duration_ticks': 38, 'contact_tick': 7, 'active': [7, 23], 'iasa_tick': 33},
  timing={'fixed': ['38 ticks played once; hits on the right hand at ticks 7-12 and the left hand at 18-23',
                    'the player can act from tick 33', 'tick 0 and tick 38 are the fall pose exactly'],
          'adjustable': ['nothing else']},
  exit={'reference': 'fall', 'tick': 0, 'from_tick': 38}, fast_windows=[[4, 9], [14, 20]], strike=['RightHand', 'LeftHand'],
  keys=[0, 4, 7, 12, 18, 23, 33, 38], review_ticks=[0, 4, 7, 10, 12, 15, 18, 21, 23, 28, 33, 38],
  targets=[{'name': 'first jab: right hand far in front at tick 7 (m)', 'kind': 'ahead', 'a': 'RightHand', 'b': 'Spine2', 'tick': 7, 'min': 0.28},
           {'name': 'second jab: left hand far in front at tick 18 (m)', 'kind': 'ahead', 'a': 'LeftHand', 'b': 'Spine2', 'tick': 18, 'min': 0.28},
           {'name': 'first jab: left hand back at the chest at tick 7 (m)', 'kind': 'ahead', 'a': 'LeftHand', 'b': 'Spine2', 'tick': 7, 'max': 0.15},
           {'name': 'second jab: right hand back at the chest at tick 18 (m)', 'kind': 'ahead', 'a': 'RightHand', 'b': 'Spine2', 'tick': 18, 'max': 0.15}]))

# --- air.3: Tail Whip (up air) --------------------------------------------------------------------------
BRIEFS.append(air('a3-tail-whip',
  notes='Salamander air.3 (anim.salamander.a3), kit proposal 2026-10-27: Tail Whip, his up aerial. New timing: 44 ticks, hit '
        '12-20 above him, act from 36, landing lag 10.',
  description='\n\n'.join([
    "The salamander's air.3, Tail Whip: his up aerial and his one salamander move. He arches backward in the air and whips "
    "his thick tail up from behind him, over the top of his head, like a scorpion sting.", WHO, GAME,
    "In the air. The entry pose is the fall he plays in the air (a borrowed pose). There is no floor contact.",
    "The move (44 ticks, played once):\n- Tick 0: the fall pose.\n- Tick 6, the coil: he curls forward, knees up, chin "
    "down, the tail swinging down and forward under him (Tail1-Tail5 bending forward).\n- Tick 12, the whip: he arches back "
    "hard, chest and head tipped back, arms flung out to the sides, and the tail swings up behind and over his head, the tail "
    "tip (Tail5) above his hat and a little in front of it.\n- Ticks 12-20: the tail holds over his head and curls its tip "
    "(a moving hold).\n- Tick 26: the tail swings back down behind him, he straightens.\n- Tick 36: recovered.\n- Tick 44: "
    "back to the fall pose, matching tick 0.\nThe tail bends along its whole length, each joint turning 25-50 degrees, the tip "
    "trailing the root by 1-3 ticks: it is a whip, not a stick."]),
  clock={'duration_ticks': 44, 'contact_tick': 12, 'active': [12, 20], 'iasa_tick': 36},
  timing={'fixed': ['44 ticks played once; hit above him from tick 12 to 20', 'the player can act from tick 36',
                    'tick 0 and tick 44 are the fall pose exactly'],
          'adjustable': ['the arch may be smaller if the tail reaches over the head anyway']},
  exit={'reference': 'fall', 'tick': 0, 'from_tick': 44}, fast_windows=[[6, 14]], strike=['Tail5'],
  keys=[0, 6, 12, 20, 26, 36, 44], review_ticks=[0, 4, 6, 9, 12, 16, 20, 26, 31, 36, 44],
  targets=[{'name': 'whip: tail tip above the head at tick 12 (m)', 'kind': 'below', 'a': 'Head', 'b': 'Tail5', 'tick': 12, 'min': 0.0},
           {'name': 'hold: tail tip above the head at tick 18 (m)', 'kind': 'below', 'a': 'Head', 'b': 'Tail5', 'tick': 18, 'min': 0.0},
           {'name': 'coil: tail tip below the hips at tick 6 (m)', 'kind': 'below', 'a': 'Hips', 'b': 'Tail5', 'tick': 6, 'max': -0.1}]))

# --- air.4: Spur Stomp (big air) ------------------------------------------------------------------------
BRIEFS.append(air('a4-spur-stomp',
  notes='Salamander air.4 (anim.salamander.a4), kit proposal 2026-10-27: Spur Stomp, his big aerial. New timing: 50 ticks, '
        'hit 12-26 under his boots, act from 44, landing lag 12.',
  description='\n\n'.join([
    "The salamander's air.4, Spur Stomp: his big aerial. He pulls both knees up to his chest, then stamps both spurred boots "
    "straight down with all his weight, like stomping on a rattlesnake.", WHO, GAME,
    "In the air. The entry pose is the fall he plays in the air (a borrowed pose). There is no floor contact.",
    "The move (50 ticks, played once):\n- Tick 0: the fall pose.\n- Tick 8, the coil: knees pulled high to his chest, "
    "boots together under his seat, arms raised out to the sides and up, elbows bent, hands in fists, hat tipped back.\n- "
    "Tick 12, the stomp: both legs drive straight down, fully extended, boots together and flat, toes down a little; arms "
    "thrown up overhead; body stretched long.\n- Ticks 12-26: he holds the stomp (a moving hold, the legs drifting 2-3 cm, "
    "the arms windmilling slightly).\n- Tick 34: legs come back up, knees bent.\n- Tick 44: recovered.\n- Tick 50: back "
    "to the fall pose, matching tick 0."]),
  clock={'duration_ticks': 50, 'contact_tick': 12, 'active': [12, 26], 'iasa_tick': 44},
  timing={'fixed': ['50 ticks played once; the hitbox is under his boots from tick 12 to 26', 'the player can act from tick 44',
                    'tick 0 and tick 50 are the fall pose exactly'],
          'adjustable': ['nothing else']},
  exit={'reference': 'fall', 'tick': 0, 'from_tick': 50}, fast_windows=[[8, 14]], strike=['LeftFoot', 'RightFoot'],
  keys=[0, 8, 12, 26, 34, 44, 50], review_ticks=[0, 4, 8, 10, 12, 16, 20, 26, 30, 34, 44, 50],
  targets=[{'name': 'coil: knees up at tick 8 (m)', 'kind': 'below', 'a': 'Hips', 'b': 'LeftLeg', 'tick': 8, 'min': -0.06},
           {'name': 'stomp: left boot far below the hips at tick 12 (m)', 'kind': 'below', 'a': 'Hips', 'b': 'LeftFoot', 'tick': 12, 'max': -0.28},
           {'name': 'stomp: right boot far below the hips at tick 12 (m)', 'kind': 'below', 'a': 'Hips', 'b': 'RightFoot', 'tick': 12, 'max': -0.28},
           {'name': 'stomp: boots together at tick 14 (m)', 'kind': 'distance', 'a': 'LeftFoot', 'b': 'RightFoot', 'tick': 14, 'max': 0.16}]))

# --- ground.A: Finger Guns, the shot ---------------------------------------------------------------------
QIMG = [f'{IMG}/salamander-model.png', f'{IMG}/q-a-bang.png']
QNOTE = ('1: the salamander model with his arms down (front, three-quarter, side). 2: the concept the owner picked for his Q '
         '(generated art): the quick-draw BANG, right arm out in a finger gun, left hand on the hip.')
BRIEFS.append(ground('ga-finger-gun-shot',
  notes='Salamander ground.A (anim.salamander.ga), kit proposal 2026-10-27: Finger Guns, one shot. The engine capability '
        'slop.ability.finger-guns.v1 fires at tick 4 and recovers for 10 ticks: 14 ticks a shot, tapped up to four a second. '
        'The owner approved the shape: tap to shoot, six shots then a reload gag, no refill over time.',
  images=QIMG, image_notes=QNOTE,
  description='\n\n'.join([
    "The salamander's Q, Finger Guns: one shot. The player taps it up to six times in a row, about four shots a second, so "
    "the clip plays again and again: it must start from his idle and from its own end pose.", WHO, GAME, FLOOR, STANCE,
    "The shot (14 ticks, played once per shot; image 2 is the shot pose):\n- Tick 0: the showdown stance.\n- Tick 3, the "
    "draw: the right hand snaps up from the hip into a finger gun, arm straight out in front at shoulder height, pointing "
    "dead ahead; thumb up; left hand onto his left hip (knuckles on the hip, elbow out). Chest square to the front, chin "
    "down, squinting along the arm.\n- Tick 4, BANG: the thumb drops onto the index finger like a hammer (Thumb joints "
    "fold down), and the recoil kicks the hand and forearm up 20-30 degrees; his head jolts back a little: he is saying "
    "BANG.\n- Tick 8: the recoil peaks and the hand starts back down, the thumb cocking back up.\n- Tick 14: aimed again "
    "at shoulder height, thumb up, ready for the next shot: the pose of tick 3. Do not return to the idle stance; the game "
    "crossfades to the idle when he stops shooting.\nFeet stay planted; the knees soften on each recoil."]),
  clock={'duration_ticks': 14, 'contact_tick': 4, 'active': [4, 4], 'iasa_tick': 14},
  timing={'fixed': ['14 ticks: the bullet leaves at tick 4, recovery is 10 ticks', 'it replays on every tap, so tick 14 is the aim pose of tick 3'],
          'adjustable': ['the recoil size']},
  fast_windows=[[0, 6]], strike=['RightHandIndex3'],
  keys=[0, 3, 4, 8, 14], review_ticks=[0, 2, 3, 4, 5, 6, 8, 10, 12, 14],
  targets=[floor('left toe on the floor at the shot', 4, 'LeftToeBase'), floor('right toe on the floor at the shot', 4, 'RightToeBase'),
           {'name': 'aim: right hand far in front at tick 3 (m)', 'kind': 'ahead', 'a': 'RightHand', 'b': 'Spine2', 'tick': 3, 'min': 0.3},
           {'name': 'aim: right hand at shoulder height at tick 3 (m)', 'kind': 'below', 'a': 'RightArm', 'b': 'RightHand', 'tick': 3, 'min': -0.1, 'max': 0.08},
           {'name': 'recoil: hand kicks up at tick 8 (m)', 'kind': 'below', 'a': 'RightArm', 'b': 'RightHand', 'tick': 8, 'min': 0.02},
           {'name': 'ready again: right hand far in front at tick 14 (m)', 'kind': 'ahead', 'a': 'RightHand', 'b': 'Spine2', 'tick': 14, 'min': 0.3},
           {'name': 'left hand on the left hip at tick 4 (m)', 'kind': 'char', 'a': 'LeftHand', 'axis': 'right', 'tick': 4, 'max': -0.1}]))

# --- ground.A: Finger Guns, the reload gag ---------------------------------------------------------------
BRIEFS.append(ground('ga-finger-gun-reload',
  notes='Salamander ground.A reload (anim.salamander.finger-guns-reload), kit proposal 2026-10-27: after the sixth shot the '
        'capability plays this for its reloadTicks (90), committed. The owner approved: blow on the fingertip, twirl, holster.',
  images=QIMG, image_notes=QNOTE,
  description='\n\n'.join([
    "The salamander's Q reload gag: after the sixth shot his invisible gun is 'empty', and he does the full gunslinger "
    "routine before he can shoot again. It is his comedy beat, so it must read clearly from behind the camera. He is "
    "committed: nothing can interrupt it except a hit.", WHO, GAME, FLOOR, STANCE,
    "The routine (90 ticks, played once):\n- Tick 0: the end of a shot: right arm out in front in a finger gun at shoulder "
    "height (the pose of the shot clip's tick 14), left hand on his left hip.\n- Tick 14, blow the smoke: he brings the "
    "finger gun up beside his snout, fingertip pointing up, and leans his head in to blow on the fingertip (head turned to "
    "the hand, snout 5-8 cm from the fingertip).\n- Tick 34, the twirl: the right arm drops out to his side at hip height and "
    "the finger gun spins: the wrist circles quickly two times between ticks 26 and 42, the index finger leading, like a "
    "revolver spinning on a trigger finger.\n- Tick 52, holster: he slides the finger gun down into the invisible holster at "
    "his right hip (the hand pushed down beside the thigh) and pats it twice with the flat hand (ticks 56 and 62).\n- Tick 72: "
    "both hands come up to hover over the holsters again; a satisfied nod and a straw chew (joint Straw dips 10-15 degrees).\n"
    "- Tick 90: the showdown stance, matching his idle at tick 0.\nFeet stay planted; weight shifts a little with each beat."]),
  clock={'duration_ticks': 90},
  timing={'fixed': ['90 ticks played once, the capability reloadTicks; he is committed for all of it',
                    'tick 90 is his showdown idle exactly'],
          'adjustable': ['the beats inside, as long as blow, twirl and holster all read']},
  exit={'reference': 'showdown', 'tick': 0, 'from_tick': 90},
  keys=[0, 14, 26, 34, 42, 52, 62, 72, 90], review_ticks=[0, 7, 14, 22, 26, 30, 34, 38, 42, 52, 56, 62, 72, 80, 90],
  targets=[floor('left toe on the floor at the blow', 14, 'LeftToeBase'), floor('right toe on the floor at the twirl', 34, 'RightToeBase'),
           {'name': 'blow: fingertip near the snout at tick 14 (m)', 'kind': 'distance', 'a': 'RightHandIndex3', 'b': 'Mustache', 'tick': 14, 'max': 0.12},
           {'name': 'twirl: right hand out at the hip at tick 34 (m)', 'kind': 'char', 'a': 'RightHand', 'axis': 'right', 'tick': 34, 'min': 0.12},
           {'name': 'holster: right hand down at the thigh at tick 52 (m)', 'kind': 'below', 'a': 'Hips', 'b': 'RightHand', 'tick': 52, 'max': 0.0},
           {'name': 'twirl: the hand circles (m)', 'kind': 'range', 'a': 'RightHandIndex3', 'axis': 'front', 'window': [26, 42], 'min': 0.05}]))

# --- ground.E: Sombrero Copter (recovery) ----------------------------------------------------------------
BRIEFS.append(ground('ge-sombrero-copter',
  notes='Salamander ground.E (anim.salamander.ge), kit proposal 2026-10-27: Sombrero Copter, his recovery, on '
        'slop.ability.aimed-recovery.v1 (as Bashie Ring Bounce): 16 ticks of startup, then he rises; the stage lasts 50. The '
        "owner liked the idea: 'funny, need to make it work animation wise'. The hat stays on its joint and spins there.",
  description='\n\n'.join([
    "The salamander's E, Sombrero Copter: his recovery. He grabs the brim of his sombrero with both hands and spins it on his "
    "head like a helicopter rotor, and it lifts him into the air, legs dangling and pedalling. Pure cartoon logic.", WHO, GAME,
    FLOOR, STANCE,
    "The move (50 ticks, played once; the game lifts him from tick 16):\n- Tick 0: the showdown stance.\n- Tick 6: both "
    "hands reach up and grab the hat brim at the sides of his head, elbows high and out.\n- Tick 12: he gives the hat a hard "
    "twist: joint Hat starts spinning about the up axis (its own vertical) and keeps spinning to the end, fast: at least a "
    "full turn every 8 ticks from tick 16, and the hands stay on the brim at the sides, elbows up, as if hanging from it. He "
    "rises onto his toes, crouching a little first (ticks 10-13) and stretching up at tick 16.\n- Ticks 16-50: lifting off: "
    "his whole body hangs from the hat, arms straight up holding the brim (hands either side of the head, at or above the "
    "brim), legs dangling and pedalling in the air (knees lifting in turn, boots below him), tail hanging and swaying, a "
    "slight swing of the whole body under the hat. Tick 30 and tick 44 are the legs at opposite pedal positions.\n- Tick 50: "
    "still hanging and spinning (the game moves on to his fall).\nThe feet leave the floor from tick 16 (the game lifts "
    "him; in the clip the boots simply hang below him)."]),
  clock={'duration_ticks': 50},
  timing={'fixed': ['50 ticks played once; 16 ticks of startup on the ground, then the game launches him up (about 15 m/s)'],
          'adjustable': ['the startup wind-up beats']},
  keys=[0, 6, 12, 16, 30, 44, 50], review_ticks=[0, 4, 6, 10, 12, 14, 16, 20, 25, 30, 37, 44, 50],
  targets=[floor('left toe on the floor at the twist', 12, 'LeftToeBase'),
           {'name': 'hands on the brim at tick 12: right hand near the head (m)', 'kind': 'distance', 'a': 'RightHand', 'b': 'Hat', 'tick': 12, 'max': 0.2},
           {'name': 'hands on the brim at tick 30: left hand near the head (m)', 'kind': 'distance', 'a': 'LeftHand', 'b': 'Hat', 'tick': 30, 'max': 0.22},
           {'name': 'hanging: hands above the head at tick 30 (m)', 'kind': 'below', 'a': 'Head', 'b': 'RightHand', 'tick': 30, 'min': -0.03},
           {'name': 'pedalling: left boot moves (m)', 'kind': 'range', 'a': 'LeftFoot', 'axis': 'up', 'window': [16, 50], 'min': 0.05}]))

# --- ground.R: Tumbleweed ---------------------------------------------------------------------------------
BRIEFS.append(ground('gr-tumbleweed',
  notes="Salamander ground.R (anim.salamander.gr), kit proposal 2026-10-27: Tumbleweed, the owner's idea: he curls into a "
        'ball, rolls in the aimed direction (a forwardLunge in the timeline), pops out and shoots backward along his path (a '
        'projectile with a 180 degree yaw offset). 64 ticks: tuck to 8, roll 8-40, pop at 40, back shot at 48.',
  description='\n\n'.join([
    "The salamander's R, Tumbleweed: he tucks into a ball and rolls like a tumbleweed in the wind across the stage, pops out "
    "at the end and, without looking, fires a finger gun back over his shoulder along the way he came.", WHO, GAME, FLOOR,
    STANCE,
    "The move (64 ticks, played once; the game rolls him forward from tick 8 to tick 40):\n- Tick 0: the showdown "
    "stance.\n- Tick 8, the tuck: he dives into a ball: knees pulled to his chest, arms wrapped round his shins, chin down, "
    "hat pressed on, tail wrapped round him; his body already tipping forward.\n- Ticks 8-40, the roll: he tumbles forward "
    "head over heels, staying a tight ball: the body turns about his right-left axis, forward, one full turn every 12 ticks "
    "(ticks 8, 20, 32 are the same upright ball; 14, 26, 38 upside down), the ball's lowest point always on the floor: the ball is about 0.45 m across, so his hip joint sits about 0.2 m above the floor during the roll (up about -0.19), much lower than when he stands.\n- "
    "Tick 44, the pop: he springs open into a low crouch, boots planted, facing front, arms flung out.\n- Tick 48, the back "
    "shot: still facing front he twists his chest to his right and fires his right finger gun backward over his right "
    "shoulder, arm straight back behind him, not looking (chin forward, eyes ahead).\n- Tick 54: the hand comes back, blows "
    "the fingertip once.\n- Tick 64: the showdown stance."]),
  clock={'duration_ticks': 64, 'contact_tick': 48, 'active': [48, 48], 'iasa_tick': 58},
  timing={'fixed': ['64 ticks played once; the game rolls him forward from tick 8 to 40', 'the back shot leaves at tick 48',
                    'the player can act from tick 58', 'tick 64 is his showdown idle'],
          'adjustable': ['the roll speed (one turn every 10-14 ticks)']},
  exit={'reference': 'showdown', 'tick': 0, 'from_tick': 64}, fast_windows=[[4, 44]], strike=['RightHand'],
  keys=[0, 8, 14, 20, 26, 32, 38, 44, 48, 54, 64], review_ticks=[0, 4, 8, 11, 14, 17, 20, 26, 32, 38, 41, 44, 48, 54, 64],
  targets=[{'name': 'tuck: hands near the shins at tick 8 (m)', 'kind': 'distance', 'a': 'RightHand', 'b': 'RightLeg', 'tick': 8, 'max': 0.16},
           {'name': 'roll: upside down at tick 14, head below the hips (m)', 'kind': 'below', 'a': 'Hips', 'b': 'Head', 'tick': 14, 'max': 0.0},
           {'name': 'roll: upright ball at tick 20, head above the hips (m)', 'kind': 'below', 'a': 'Hips', 'b': 'Head', 'tick': 20, 'min': 0.05},
           {'name': 'roll: the ball sits on the floor, hips low at tick 14 (m)', 'kind': 'char', 'a': 'Hips', 'axis': 'up', 'tick': 14, 'min': FLOOR_UP + 0.15, 'max': FLOOR_UP + 0.24},
           {'name': 'roll: the ball sits on the floor, hips low at tick 20 (m)', 'kind': 'char', 'a': 'Hips', 'axis': 'up', 'tick': 20, 'min': FLOOR_UP + 0.15, 'max': FLOOR_UP + 0.24},
           {'name': 'roll: the ball sits on the floor, hips low at tick 32 (m)', 'kind': 'char', 'a': 'Hips', 'axis': 'up', 'tick': 32, 'min': FLOOR_UP + 0.15, 'max': FLOOR_UP + 0.24},
           floor('pop: left toe on the floor at tick 44', 44, 'LeftToeBase'),
           {'name': 'back shot: right hand behind him at tick 48 (m)', 'kind': 'ahead', 'a': 'RightHand', 'b': 'Spine2', 'tick': 48, 'max': -0.2}]))

# --- ground.F: Big Iron, the charge (aim loop) ------------------------------------------------------------
BRIEFS.append(ground('gf-big-iron-aim',
  notes='Salamander ground.F aim loop (anim.salamander.gf-loop), kit proposal 2026-10-27: Big Iron on '
        'slop.ability.charged-projectile.v1 (as FightGuy Ki Shot): hold to charge, release a big slow shot. My pick from the '
        "F pitches (owner: 'not sure' about HIGH NOON); the owner had not chosen an F.",
  description='\n\n'.join([
    "The salamander's F, Big Iron: the charge. He clasps both hands into one big two-handed finger gun and aims it, "
    "trembling with the effort of holding the 'big iron'. It loops while the player holds the button.", WHO, GAME, FLOOR,
    STANCE,
    "The aim (30 ticks, a loop):\n- The stance: boots wider than his idle, knees bent, weight low and back; both arms "
    "straight out in front at eye level, hands together, both index fingers pointing ahead side by side, thumbs up; chin "
    "down sighting along the arms; elbows locked.\n- The tremble: the arms and the chest shake in small quick jitters "
    "(1-2 cm, a new direction every 3-4 ticks), as if the gun weighs a ton; the knees sag 1-2 cm and push back up once per "
    "loop (ticks 0, 15, 30); the mustache quivers (joint Mustache rocks 3-6 degrees).\n- Tick 30: the pose of tick 0.\nThe "
    "entry pose only sets the frame (his idle); the game crossfades into the aim."]),
  clock={'duration_ticks': 30}, loop={'from_tick': 0}, entry={'reference': 'showdown', 'tick': 0, 'match': False},
  timing={'fixed': ['it loops while held; the game crossfades into it from the idle'], 'adjustable': ['the loop length']},
  keys=[0, 8, 15, 23, 30], review_ticks=[0, 4, 8, 12, 15, 19, 23, 27, 30],
  targets=[floor('left toe on the floor at tick 0', 0, 'LeftToeBase'), floor('right toe on the floor at tick 0', 0, 'RightToeBase'),
           {'name': 'two-handed: hands together at tick 0 (m)', 'kind': 'distance', 'a': 'LeftHand', 'b': 'RightHand', 'tick': 0, 'max': 0.06},
           {'name': 'two-handed: hands far in front at tick 0 (m)', 'kind': 'ahead', 'a': 'RightHand', 'b': 'Spine2', 'tick': 0, 'min': 0.28},
           {'name': 'arms at eye level at tick 0 (m)', 'kind': 'below', 'a': 'RightArm', 'b': 'RightHand', 'tick': 0, 'min': -0.06},
           {'name': 'tremble: hands jitter (m)', 'kind': 'range', 'a': 'RightHand', 'axis': 'up', 'window': [0, 30], 'min': 0.008, 'max': 0.04}]))

# --- ground.F: Big Iron, the blast ------------------------------------------------------------------------
BRIEFS.append(ground('gf-big-iron-blast',
  notes='Salamander ground.F release (anim.salamander.gf), kit proposal 2026-10-27: the charged-projectile capability fires '
        'at tick 8 of a 45-tick release. The gag: the blast throws his arms up, knocks him back a step and peels his fake '
        'mustache off one side; he presses it back on (the same gag as his idle).',
  description='\n\n'.join([
    "The salamander's F, Big Iron: the blast. He releases the charged two-handed shot, and the recoil nearly knocks him over "
    "and half peels off his fake mustache.", WHO, GAME, FLOOR, STANCE,
    "The release (45 ticks, played once):\n- Tick 0: the aim pose of the charge loop: boots wide, knees bent, both arms "
    "straight out in front at eye level, hands clasped into one two-handed finger gun.\n- Tick 8, BOOM: the huge recoil: "
    "both arms fly up and back over his head, his chest arches back, the hat tips back, his right boot hops back 15-20 cm "
    "(he is shoved back a step) and the tail kicks up; joint Mustache flips 40-60 degrees so the mustache dangles from one "
    "end.\n- Tick 16: he stumbles, windmilling one arm for balance, knees bent.\n- Tick 26: steady again, he notices the "
    "mustache, his LEFT index finger presses it back onto his snout (fingertip at joint Mustache), joint Mustache snapping "
    "back to rest by tick 30.\n- Tick 36: a cocky shrug, hands back over the holsters.\n- Tick 45: the showdown stance."]),
  clock={'duration_ticks': 45, 'contact_tick': 8, 'active': [8, 8], 'iasa_tick': 45},
  entry={'reference': 'showdown', 'tick': 0, 'match': False},
  timing={'fixed': ['45 ticks played once; the shot leaves at tick 8', 'tick 45 is his showdown idle'],
          'adjustable': ['the stumble beats']},
  exit={'reference': 'showdown', 'tick': 0, 'from_tick': 45}, fast_windows=[[6, 12]], strike=['RightHand'],
  keys=[0, 8, 16, 26, 30, 36, 45], review_ticks=[0, 4, 7, 8, 10, 12, 16, 21, 26, 30, 36, 45],
  targets=[{'name': 'aim: hands together in front at tick 0 (m)', 'kind': 'distance', 'a': 'LeftHand', 'b': 'RightHand', 'tick': 0, 'max': 0.06},
           {'name': 'boom: hands above the head at tick 8 (m)', 'kind': 'below', 'a': 'Head', 'b': 'RightHand', 'tick': 10, 'min': 0.05},
           {'name': 'boom: right boot steps back (m)', 'kind': 'range', 'a': 'RightFoot', 'axis': 'front', 'window': [6, 16], 'min': 0.1},
           {'name': 'press: left fingertip at the mustache at tick 26 (m)', 'kind': 'distance', 'a': 'LeftHandIndex3', 'b': 'Mustache', 'tick': 26, 'max': 0.05},
           floor('settled: left toe on the floor at tick 36', 36, 'LeftToeBase')]))

out = REPO / 'tools/genra/briefs'
for b in BRIEFS:
    (out / f"{b['name']}.json").write_text(json.dumps(b, indent=2, ensure_ascii=False) + '\n')
    print('wrote', b['name'], 'clock', b['clock'])
