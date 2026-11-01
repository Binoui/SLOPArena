"""Write Nyx's GenRA briefs (tools/genra/briefs/nyx-*.json) and her proposed move timing
(tmp/nyx-model/genra/timing.json) from one kit table.

    artifacts/genra-20261014/.venv/bin/python tools/blender/nyx_rig/make_briefs.py [--only ID,...]

Nyx is one skeleton with two bodies (ADR-0051). Each form has its own rig packs, built with
`unity_rigpack.py --meshes`: `nyx-unity-real` (body; the cloth dress is left out) and `nyx-unity-fake` (body and
backpack), plus `nyx-unity-fakepole` with the hand-held pole for the two ring-light swings. Idles and runs start free
in the borrowed idle's frame. Ground moves start and end on her own new idle, carried as `realstance`/`fakestance` in
the `*moves` packs once the idles are written to Unity; until those packs exist their briefs are skipped. Aerials and
recoveries start free in the borrowed fall's frame, on a pose the brief describes.

No move data exists yet (2026-10-29). Each move's game timing is proposed from the roster's equivalent moves and
recorded in timing.json for the engine phases; attacks are authored at twice their game length (owner, 2026-10-28:
"we can speed them up easily unity side"), so every authored tick is the game tick times the move's `scale`.
"""
import argparse
import json
import sys
from pathlib import Path

import numpy as np

REPO = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO / 'tools'))
from genra.brief import Brief  # noqa: E402
from genra.skin import Skin  # noqa: E402

ap = argparse.ArgumentParser()
ap.add_argument('--only', default='', help='comma-separated move ids (default all)')
args = ap.parse_args()
ONLY = {m for m in args.only.split(',') if m}
OUT = REPO / 'tmp/nyx-model/genra'
PROBE = OUT / 'probe'
PROBE.mkdir(parents=True, exist_ok=True)
SHEETS = '../../../tmp/nyx-model/anim-sheets/sheets'
RENDERS = '../../../tmp/nyx-model/renders'
STANCE_ASSET = 'Assets/Art/Experiments/GenRA/nyx/nyx-{form}-idle.anim'
PACKS = {'real': 'nyx-unity-real', 'fake': 'nyx-unity-fake', 'fakepole': 'nyx-unity-fakepole'}
MOVE_PACKS = {k: v + 'moves' for k, v in PACKS.items()}
SHEET_MOVES = {m['id']: m for m in json.loads((REPO / 'tmp/nyx-model/anim-sheets/moves.json').read_text())['moves']}


def pack_path(name):
    return REPO / 'artifacts/genra-rigpacks' / name


def measure(pack, ref, tick=0):
    """The frame of `ref` at `tick` on `pack`'s bodies: floor (lowest skinned vertex), toe and ankle heights above it."""
    probe = PROBE / f'{pack}-{ref}.json'
    probe.write_text(json.dumps({'name': 'probe', 'rigpack': f'artifacts/genra-rigpacks/{pack}', 'character': '',
                                 'description': '', 'references': {ref: f'references/{ref}.usda'},
                                 'entry': {'reference': ref, 'tick': tick}, 'clock': {'duration_ticks': 10}}))
    b = Brief(probe)
    e = b.entry_pose
    world = e.world()
    hips_y = world[b.rig.index('Hips')][3, 1]
    skin = Skin(b.rig)
    body = [p for p, m in zip(skin.points(world), skin.meshes) if not m['prop']]
    floor = float(min(p[:, 1].min() for p in body) - hips_y)
    out = {'floor': round(floor, 3)}
    for j in ('LeftToeBase', 'RightToeBase', 'LeftFoot', 'RightFoot', 'Hips', 'Head', 'LeftArm', 'RightArm'):
        out[j] = [round(float(x), 3) for x in e.pos(j)]
    out['toe'] = round(min(out['LeftToeBase'][2], out['RightToeBase'][2]) - floor, 3)
    out['ankle'] = round(min(out['LeftFoot'][2], out['RightFoot'][2]) - floor, 3)
    leg = sum(float(np.linalg.norm(e.pos(a) - e.pos(c))) for a, c in (('LeftUpLeg', 'LeftLeg'), ('LeftLeg', 'LeftFoot')))
    out['hipsStraight'] = round(leg + out['ankle'], 3)
    props = [p for p, m in zip(skin.points(world), skin.meshes) if m['prop']]
    if props:  # the backpack: its top is the ring light, above her left shoulder
        top = props[0][np.argmax(props[0][:, 1])]
        shoulder = world[b.rig.index('LeftArm')][3, :3]
        out['ringAboveShoulder'] = round(float(top[1] - shoulder[1]), 3)
    return out


def limbs(pack):
    e = Brief(PROBE / f'{pack}-idle.json').entry_pose
    d = lambda a, c: round(float(np.linalg.norm(e.pos(a) - e.pos(c))), 3)
    return {'thigh': d('LeftUpLeg', 'LeftLeg'), 'shin': d('LeftLeg', 'LeftFoot'), 'upperArm': d('LeftArm', 'LeftForeArm'),
            'forearm': d('LeftForeArm', 'LeftHand'), 'hand': d('LeftHand', 'LeftHandMiddle3'), 'shoulders': d('LeftArm', 'RightArm')}


FRAMES = {form: {'idle': measure(pack, 'idle')} for form, pack in PACKS.items()}
LIMBS = limbs(PACKS['real'])
STANCE = {}
for form in ('real', 'fake'):
    pack = MOVE_PACKS[form]
    if (pack_path(pack) / 'references' / f'{form}stance.usda').exists():
        STANCE[form] = measure(pack, f'{form}stance')
print('frames', json.dumps({f: v['idle']['floor'] for f, v in FRAMES.items()}), 'stance', json.dumps({f: v['floor'] for f, v in STANCE.items()}))
RING = FRAMES['fake']['idle'].get('ringAboveShoulder', 0.45)

# ----- shared text -------------------------------------------------------------------------------------------------
BODY = (f"Nyx (Mixamo joints in a Humanoid body: Hips, Spine, Spine1, Spine2, Neck, Head, LeftShoulder/LeftArm/LeftForeArm/"
        f"LeftHand with five full fingers, the same on the right, LeftUpLeg/LeftLeg/LeftFoot/LeftToeBase and the right leg; no "
        f"tail). An adult woman, about 1.70 m tall in the rig (the game draws her 1.04 times bigger). Measured on the rig: "
        f"thigh {LIMBS['thigh']} m, shin {LIMBS['shin']} m, upper arm {LIMBS['upperArm']} m, forearm {LIMBS['forearm']} m, hand "
        f"{LIMBS['hand']} m from wrist to middle fingertip, shoulders {LIMBS['shoulders']} m apart. She wears tall platform boots: "
        f"with a flat boot the ankle joint (Foot) sits about {FRAMES['real']['idle']['ankle']} m above the floor and the toe joint "
        f"about {FRAMES['real']['idle']['toe']} m above it; standing straight her hip joint is about "
        f"{FRAMES['real']['idle']['hipsStraight']} m above the floor. Her waist-length straight black hair is rigid on her head and "
        f"hangs down her back: hands swung behind her head or back pass through it, so keep them in front of, beside or above it.")
CHAR = {
    'real': BODY + (" Real form: her long black dress is not in this rig. It is simulated cloth hanging from her hips to her ankles, "
                    "so pose the body only, and keep her knees and boots where a long skirt can still hang over them: no high "
                    "knees, no wide stances."),
    'fake': BODY + (f" Fake form: a backpack sits on her upper back, rigid on joint Nyx_Backpack under Spine2, with a pole rising "
                    f"above her LEFT shoulder that holds a big ring light, its top about {RING} m above that shoulder. Arms must "
                    f"not pass through the backpack, the pole or the ring."),
}
CHAR['fakepole'] = CHAR['fake'] + (
    " For this move the rig also carries the pole she pulls off the backpack, rigid in her LEFT hand (joint Nyx_PoleHeld under "
    "LeftHand): bladeHilt is the lower end of the pole just past her hand, bladeEnd is its far end at the ring light, about "
    "0.6 m beyond the hand. The backpack's own copy of the pole still shows in the toolkit renders; ignore it. The game hides "
    "one of the two.")

WHO = {
    'real': ("Who she is. Nyx in her real form (image 1): a deadpan trad-goth shadow witch, a stark white face, black lips, "
             "waist-length straight black hair with blunt bangs, a long black dress with puffed shoulders, silver chains and an "
             "ankh, studded bangles, black platform boots. She stands straight and stiff, bored and unimpressed. Her body barely "
             "moves: her shadows do the hitting (claws, hands, tendrils, added later as effects, not in this clip). Her moves are "
             "small, stiff, deliberate gestures of one hand or both: precise and economical, with a slight anticipation, a crisp "
             "gesture, a held beat and a clean return. Barely moving means small amplitudes, not a statue: the chest breathes, the "
             "head settles a degree or two, the free hand's fingers relax, so nothing parks dead still, but nothing moves big. "
             "Her feet stay planted and her torso upright."),
    'fake': ("Who she is. Nyxxie, Nyx's fake form (image 1): the same woman as a parody goth e-girl influencer, with a cat-ear "
             "headband, huge lashes, fishnets, a pink-and-black pleated skirt, pink-buckled platform boots, a backpack with a "
             "ring light on a pole above her left shoulder, and her phone. She never stops moving: loose and bouncy, a show-off "
             "who performs for her phone and films herself, and she fights like a brawler. Weight shifts, hip pops, bouncing "
             "knees, a toss of the head; overlap and follow-through everywhere."),
}
PHONE = ("The phone. She always holds one smartphone in her RIGHT hand, but the rig has no phone mesh yet: keep the right hand "
         "closed around an invisible phone the whole clip (fingers curled loosely as around a slim 7 x 15 cm slab, thumb on its "
         "face), whatever the arm does. It never changes hands. Her LEFT hand is the free one.")
GAME = ("How the game plays it. The match camera sits straight behind her and a little above, so the action must read from "
        "behind: gestures out to the side, up or well ahead of her, not hidden in front of her chest. The fighter does not "
        "travel in the clip; the game moves her. The game crossfades in and out over about 0.1 s.")
SLOW = ("This clip is authored slow, at twice the move's game length; the game speeds it up to the move's own ticks. Give "
        "every beat its full development: the anticipation, the gesture or hit carried through the hips and shoulders, the "
        "follow-through, the settle and the overlap. Never rush a beat.")
NORMAL = ("A forward hit lands on her centre line, well ahead of her chest, not beside her shoulder. After the hit the "
          "ending lag is plain: she regains her balance and the arms take the shortest calm path back to the stance. No "
          "flourish, no gesture after the hit.")
FLOW = ("Flow. Generated moves read stiff by default, and for this job the flow check's warnings count as failures. Stagger "
        "arrivals (the hips first, then the chest, the arms one or two ticks later, the head last), keep every hold drifting "
        "one way by a few degrees, and let the free hand, the fingers and the head trail the main action by one to three ticks "
        "(Clip.overlap). Recover in waves.")
AIR = {
    'real': ("In the air. The entry reference is a borrowed fall that only sets the frame; tick 0 is free. Her airborne pose, "
             "where this clip starts and ends: upright and straight, legs together and hanging, boots together with the toes "
             "pointing a little down, arms straight down at her sides a hand's width from her hips, chin level. There is no floor. "
             "The first and last ticks are the same airborne pose."),
    'fake': ("In the air. The entry reference is a borrowed fall that only sets the frame; tick 0 is free. Her airborne pose, "
             "where this clip starts and ends: knees bent and lifted a little, one higher than the other, the phone hand up near "
             "her right shoulder, the left arm out for balance, a bounce in her body. There is no floor. The first and last "
             "ticks are the same airborne pose."),
}


def floor_text(m, origin):
    return (f"The floor. It is at up = {m['floor']} m in this character frame (origin: {origin}). With a flat boot the toe "
            f"joint (ToeBase) sits about {m['toe']} m above the floor and the ankle (Foot) about {m['ankle']} m above it. Never "
            f"push a boot through the floor.")


STANCE_TEXT = {
    'real': ("Her idle, where ground moves start and end (reference `realstance`, the entry and exit pose): standing perfectly "
             "upright, heels together, arms straight down at her sides, chin level, a flat dead stare."),
    'fake': ("Her idle, where ground moves start and end (reference `fakestance`, the entry and exit pose): her weight on one "
             "hip, the phone at chest height in her right hand, bored."),
}


# ----- targets -----------------------------------------------------------------------------------------------------
def T(name, kind, a, tick=None, lo=None, hi=None, b=None, axis=None, window=None):
    t = {'name': name, 'kind': kind, 'a': a}
    for k, v in (('b', b), ('axis', axis), ('tick', tick), ('window', window), ('min', lo), ('max', hi)):
        if v is not None:
            t[k] = v
    return t


def floor_t(m, joint, tick, what=''):
    return T(f'{joint} on the floor{what} at tick {tick} (m)', 'char', joint, tick, round(m['floor'] + m['toe'] - 0.02, 3),
             round(m['floor'] + m['toe'] + 0.03, 3), axis='up')


def height(name, joint, ref, tick, lo=None, hi=None):
    return T(name, 'below', ref, tick, lo, hi, b=joint)


def ahead(name, joint, ref, tick, lo=None, hi=None):
    return T(name, 'ahead', joint, tick, lo, hi, b=ref)


def side(name, joint, tick, lo=None, hi=None):
    return T(name, 'char', joint, tick, lo, hi, axis='right')


def dist(name, a, b, tick, lo=None, hi=None):
    return T(name, 'distance', a, tick, lo, hi, b=b)


def travel(name, joint, axis, window, lo=None, hi=None):
    return T(name, 'range', joint, None, lo, hi, axis=axis, window=window)


# ----- the kit table -----------------------------------------------------------------------------------------------
# game: total ticks, contact tick, active [first, last], IASA, landing lag (air), and what the timing is modelled on.
# scale: authored ticks per game tick. beats: (authored tick, text). keys: authored key ticks.
MOVES = []


def move(id, **kw):
    kw['id'] = id
    MOVES.append(kw)


A = lambda t, s=2: int(round(t * s))  # noqa: E731

# ---------- real form ----------
move('real-idle', form='real', slot='idle', semantic='anim.nyx.idle', kind='idle', frame='idle', loop=True, scale=1,
     game={'total': 720, 'model': "Bashie's slowed idle (12 s loop); the owner's calm-idle rule"},
     what="Nyx's real-form idle, 'unimpressed': the clip she plays whenever she stands in a fight.",
     beats=[(0, "Rest: standing perfectly upright, heels together (boots touching or nearly), arms straight down at her "
                "sides with the hands a little away from her thighs, fingers relaxed and slightly curled, chin level, a flat "
                "dead stare straight ahead. She breathes slowly: the chest rises and falls 1-2 cm about every 4 s."),
            (240, "The one slow gesture begins: her right forearm rises slowly from the elbow, the upper arm staying close to "
                  "her side."),
            (330, "Her right hand is up at chest height, about 0.2 m in front of her chest, palm toward her, fingers loosely "
                  "curled: she examines her black nails with total boredom, head tipped down 5-8 degrees toward the hand."),
            (450, "Still examining, she turns the hand slowly (the wrist rolls 20-30 degrees) and uncurls the fingers a "
                  "little, as if checking the polish; the head follows the hand by a degree or two."),
            (540, "The hand has lowered back to her side, the head back to level."),
            (720, "Rest again, the same pose as tick 0 (the loop closes).")],
     notes_extra=("Keep it calm: the roster's hand-made idles turn their body joints 2-11 degrees a second and move the hands "
                  "0.05-0.08 body lengths a second. Outside the one gesture only breathing and a slow drift. She never "
                  "fidgets: no weight shifts, no head turns, no foot movement."),
     keys=[0, 240, 330, 450, 540, 720], grounding=True, planted=['LeftFoot', 'LeftToeBase', 'RightFoot', 'RightToeBase'],
     snap=3,
     targets=lambda m: [floor_t(m, 'LeftToeBase', 0), floor_t(m, 'RightToeBase', 0),
                        dist('heels together (m)', 'LeftFoot', 'RightFoot', 0, None, 0.2),
                        T('stands tall at tick 0 (m)', 'char', 'Hips', 0, round(m['floor'] + m['hipsStraight'] - 0.04, 3), axis='up'),
                        side('right hand at her side at tick 0 (m)', 'RightHand', 0, 0.1, 0.32),
                        side('left hand at her side at tick 0 (m)', 'LeftHand', 0, -0.32, -0.1),
                        height('nail check: right hand at chest height (m)', 'RightHand', 'Spine2', 390, -0.15, 0.08),
                        ahead('nail check: right hand in front of the chest (m)', 'RightHand', 'Spine2', 390, 0.1),
                        travel('chest breathes (m)', 'Spine2', 'up', [0, 720], 0.004, 0.025)])

move('real-run', form='real', slot='run', semantic='anim.nyx.run', kind='run', frame='idle', loop=True, scale=1,
     game={'total': 16, 'model': "a short fast cycle for the creepy glide (two steps); the roster's runs are 20-26 ticks"},
     what="Nyx's real-form run, 'the creepy glide'.",
     beats=[(0, "Contact: the right boot touches down ahead (about 0.18 m in front of her hips), the left boot behind; a "
                "short, quick stride."),
            (4, "Down: weight on the right leg, knee slightly bent; the torso perfectly upright and level."),
            (8, "Passing and the other contact: the left leg has passed under her and the left boot touches down ahead, the "
                "right behind (mirror of tick 0, but not an exact copy: a slightly longer step on one side reads uncanny)."),
            (12, "Down on the left leg."),
            (16, "Back to the pose of tick 0 (the loop closes).")],
     notes_extra=("She runs like a creepy doll: fast little steps under the dress and a perfectly still, upright torso. The "
                  "joke is that nothing above the waist moves: the arms hang rigid and straight at her sides (hands a hand's "
                  "width from her hips), the chest does not counter-rotate, the head stays perfectly level and expressionless, "
                  "and the hips glide with almost no bob (under 1 cm). Only the legs work, fast. The life check's warnings "
                  "about a rigid body are the point of this clip: do not add arm swing, chest twist or head bob to clear them. "
                  "The legs themselves must still read as real steps: the knees straighten at push-off, the toes push off, "
                  "the heel lands."),
     keys=[0, 4, 8, 12, 16], grounding=True,
     targets=lambda m: [floor_t(m, 'RightToeBase', 0), floor_t(m, 'LeftToeBase', 8),
                        travel('legs step: left boot travels front to back (m)', 'LeftFoot', 'front', [0, 16], 0.25, 0.5),
                        travel('glide: hips barely bob (m)', 'Hips', 'up', [0, 16], None, 0.012),
                        travel('glide: head stays level (m)', 'Head', 'up', [0, 16], None, 0.015),
                        travel('rigid arms: right hand barely swings (m)', 'RightHand', 'front', [0, 16], None, 0.04),
                        travel('rigid arms: left hand barely swings (m)', 'LeftHand', 'front', [0, 16], None, 0.04)])

move('real-g1', form='real', slot='ground.1', semantic='anim.nyx.g1', kind='normal', frame='stance', scale=2,
     game={'total': 30, 'contact': 8, 'active': [8, 13], 'iasa': 24, 'model': 'FightGuy Straight Punch 29/6-11/26, Manki Monkey Punch 35/9'},
     what="Nyx's real-form ground.1, Shadow Flick: her jab. She flicks two fingers and a shadow claw swipes in front of her.",
     beats=[(0, "The stance."),
            (8, "The lift begins: her right forearm rises from the elbow, the upper arm staying near her side."),
            (13, "Loaded: the right hand at chest height, about 0.15 m in front of her right breast, the wrist bent back a "
                 "little, index and middle fingers curled against the thumb as if about to flick a crumb. Her chin lifts a "
                 "degree, her eyes on the target. Nothing else moves."),
            (16, "The flick (contact): the two fingers snap open and the forearm flicks forward and in, so the hand ends "
                 "about 0.3 m in front of her chest on her centre line at chest height, fingers extended, palm down. A small "
                 "crisp gesture: the elbow straightens only part way."),
            (26, "The flicked hand hangs there through the active window, the fingers relaxing and the hand sinking 2-3 cm."),
            (36, "The hand drops back toward her side by the shortest path."),
            (48, "Back at her side; she can act again."),
            (60, "The stance, matching tick 0.")],
     keys=[0, 8, 13, 16, 26, 36, 48, 60], fast=[[13, 18]], strike=['RightHand'],
     planted=['LeftFoot', 'LeftToeBase', 'RightFoot', 'RightToeBase'],
     targets=lambda m: [floor_t(m, 'LeftToeBase', 16), floor_t(m, 'RightToeBase', 16),
                        height('loaded: right hand at chest height (m)', 'RightHand', 'Spine2', 13, -0.15, 0.08),
                        ahead('flick: right hand well ahead of the chest (m)', 'RightHand', 'Spine2', 16, 0.24),
                        side('flick: right hand on her centre line (m)', 'RightHand', 16, -0.06, 0.1),
                        travel('upright: the chest barely moves (m)', 'Spine2', 'front', [0, 60], None, 0.04)])

move('real-g3', form='real', slot='ground.2', semantic='anim.nyx.g2', kind='normal', frame='stance', scale=2,
     game={'total': 44, 'contact': 14, 'active': [14, 24], 'iasa': 36, 'model': 'Manki Sweeping Kick 44/9-20/29, Bashie Shell Flip 42'},
     what=("Nyx's real-form ground.2, Grasping Hands: her anti-air. Two tall shadow hands burst up from the floor beside her "
           "and close above her head like a trap; her own hands summon them."),
     beats=[(0, "The stance."),
            (10, "The lift begins: both forearms rise slowly from the elbows."),
            (20, "Both palms turned up at hip height, elbows bent about 90 degrees at her sides, hands 0.2-0.25 m out to each "
                 "side, fingers spread and slightly clawed: she is lifting something heavy from the floor."),
            (28, "The rise (contact): both palms lift sharply to shoulder height, out to the sides (hands 0.3 m out from her "
                 "shoulders), fingers clawing; her chin lifts a little. The shadow hands burst up on each side of her."),
            (40, "The hands keep rising, still out to the sides, to just above her head height; fingers clawed."),
            (46, "Close: both hands swing in together above her head, palms facing, and the fingers snap closed as the "
                 "hands meet (hands 0.1-0.2 m above her head, close together), like jaws shutting."),
            (48, "End of the hit: hands closed together above her head, a moving hold."),
            (60, "The arms lower by the shortest path, out to the sides."),
            (72, "Arms back at her sides; she can act again."),
            (88, "The stance, matching tick 0.")],
     keys=[0, 10, 20, 28, 40, 46, 60, 72, 88], fast=[[24, 30], [42, 48]], strike=['RightHand', 'LeftHand'],
     planted=['LeftFoot', 'LeftToeBase', 'RightFoot', 'RightToeBase'],
     targets=lambda m: [floor_t(m, 'LeftToeBase', 28), floor_t(m, 'RightToeBase', 46),
                        height('lift: right hand at hip height (m)', 'RightHand', 'Hips', 20, -0.08, 0.15),
                        height('rise: right hand at shoulder height (m)', 'RightHand', 'RightArm', 28, -0.1, 0.12),
                        height('rise: left hand at shoulder height (m)', 'LeftHand', 'LeftArm', 28, -0.1, 0.12),
                        side('rise: right hand out to the side (m)', 'RightHand', 28, 0.3),
                        side('rise: left hand out to the side (m)', 'LeftHand', 28, None, -0.3),
                        height('close: right hand above her head (m)', 'RightHand', 'Head', 46, 0.12),
                        dist('close: hands together (m)', 'LeftHand', 'RightHand', 47, None, 0.2)])

move('real-g4', form='real', slot='ground.3', semantic='anim.nyx.g3', kind='normal', frame='stance', scale=2,
     game={'total': 64, 'contact': 24, 'active': [24, 31], 'iasa': 56,
           'model': 'FightGuy Double Kick 69/12/64, Wibou Heavy Down Slash 74/41/63; slow startup for the long reach'},
     what=("Nyx's real-form ground.3, Shadow Lash: her slow, long-reaching big move. Her right arm sweeps forward and down and "
           "a long shadow tendril lashes along the floor in front of her."),
     beats=[(0, "The stance."),
            (14, "The wind-up begins: her shoulders turn a little to her LEFT."),
            (34, "Wound up: chest turned 15-20 degrees to her left, her right arm drawn back across her body at waist height, "
                 "the right hand in front of her left hip, palm down, fingers together. Head stays facing ahead; eyes on the "
                 "target. The feet do not move."),
            (42, "Still drawing in, a moving hold: the hand slides 3-4 cm further back."),
            (48, "The lash (contact): the right arm sweeps forward and down, the hand passing out in front of her to about "
                 "0.45 m ahead of her hips on her centre line, at hip height, arm almost straight, palm down, fingers spread. "
                 "The chest turns back past square to 5-10 degrees right."),
            (62, "Held at full reach while the tendril is out: the arm drifts 2-3 cm lower."),
            (80, "The arm returns toward her side by the shortest path; the chest squares."),
            (112, "Arm at her side; she can act again."),
            (128, "The stance, matching tick 0.")],
     keys=[0, 14, 34, 42, 48, 62, 80, 112, 128], fast=[[42, 50]], strike=['RightHand'],
     planted=['LeftFoot', 'LeftToeBase', 'RightFoot', 'RightToeBase'],
     targets=lambda m: [floor_t(m, 'LeftToeBase', 48), floor_t(m, 'RightToeBase', 48),
                        side('wind-up: right hand across in front of her left hip (m)', 'RightHand', 34, None, 0.02),
                        height('wind-up: right hand at waist height (m)', 'RightHand', 'Hips', 34, -0.05, 0.2),
                        ahead('lash: right hand far ahead (m)', 'RightHand', 'Hips', 48, 0.38),
                        side('lash: right hand on her centre line (m)', 'RightHand', 48, -0.08, 0.12),
                        height('lash: right hand low, at hip height (m)', 'RightHand', 'Hips', 48, -0.15, 0.12)])

move('real-a1', form='real', slot='air.1', semantic='anim.nyx.a1', kind='normal', frame='air', scale=2,
     game={'total': 40, 'contact': 8, 'active': [8, 18], 'iasa': 34, 'landingLag': 9,
           'model': 'Bashie Air Whirlpool Spin 40/10-29/36 ll9, Manki Air Kick 38 ll9'},
     what="Nyx's real-form air.1, Shadow Ring: her neutral aerial. A flat ring of shadow spins around her waist.",
     beats=[(0, "Her airborne pose."),
            (10, "The arms begin to open away from her body, palms down."),
            (16, "Spread (contact): both arms opened slightly away from her body, hands about 0.35 m out from her hips at "
                 "waist height, palms down, fingers spread, as if smoothing a tablecloth. Her chin lifts a degree."),
            (36, "Held while the ring spins: the hands drift up 2 cm and out 2 cm."),
            (52, "The arms come back toward her sides by the shortest path."),
            (68, "Arms at her sides; she can act again."),
            (80, "Her airborne pose, matching tick 0.")],
     keys=[0, 10, 16, 36, 52, 68, 80], fast=[[10, 18]], strike=['RightHand', 'LeftHand'],
     targets=lambda m: [side('spread: right hand out (m)', 'RightHand', 16, 0.3),
                        side('spread: left hand out (m)', 'LeftHand', 16, None, -0.3),
                        height('spread: right hand at waist height (m)', 'RightHand', 'Hips', 16, -0.05, 0.2),
                        dist('legs together (m)', 'LeftFoot', 'RightFoot', 16, None, 0.2),
                        dist('legs together at the end (m)', 'LeftFoot', 'RightFoot', 80, None, 0.2)])

move('real-a3', form='real', slot='air.2', semantic='anim.nyx.a2', kind='normal', frame='air', scale=2,
     game={'total': 42, 'contact': 10, 'active': [10, 17], 'iasa': 36, 'landingLag': 9,
           'model': 'Bashie Air Shell Flip 42, FightGuy air High Kick 51/16 ll9'},
     what="Nyx's real-form air.2, Up Claw: her up aerial. She raises her right hand and a big shadow claw rakes upward above her.",
     beats=[(0, "Her airborne pose."),
            (8, "The right arm starts up in front of her, the elbow leading; the fingers begin to curl."),
            (20, "Reach (contact): her right arm straight up over her head, the hand a little in front of her head on her "
                 "centre line, fingers curled into a claw, palm forward. Head stays level, bored."),
            (34, "Follow-through: the arm stays up while the hand rakes back over her head, the wrist flexing and the claw "
                 "closing, the hand ending above and slightly behind her head."),
            (52, "The arm lowers by the front, by the shortest path."),
            (72, "Arm at her side; she can act again."),
            (84, "Her airborne pose, matching tick 0.")],
     keys=[0, 8, 20, 34, 52, 72, 84], fast=[[12, 22]], strike=['RightHand'],
     targets=lambda m: [height('reach: right hand well above her head (m)', 'RightHand', 'Head', 20, 0.25),
                        side('reach: right hand over her centre line (m)', 'RightHand', 20, -0.06, 0.16),
                        ahead('reach: hand a little in front of her head (m)', 'RightHand', 'Head', 20, -0.02, 0.2),
                        ahead('follow-through: hand back over her head (m)', 'RightHand', 'Head', 34, None, 0.02),
                        dist('legs together (m)', 'LeftFoot', 'RightFoot', 20, None, 0.2)])

move('real-a4', form='real', slot='air.3', semantic='anim.nyx.a3', kind='normal', frame='air', scale=2,
     game={'total': 56, 'contact': 20, 'active': [20, 27], 'iasa': 50, 'landingLag': 12,
           'model': 'FightGuy Air Smash 62/23/57 ll12, Bonk air BONK 66/28 ll24'},
     what="Nyx's real-form air.3, Shadow Fist: her spike. She drives her clasped hands down and a shadow fist slams down below her.",
     beats=[(0, "Her airborne pose."),
            (12, "Both arms start rising in front of her."),
            (30, "Raised: both hands together above her head, fingers interlaced, elbows a little bent, the body stretched "
                 "long. Chin level."),
            (36, "A moving hold at the top: the hands rise 2 cm more, the chest arches back a degree."),
            (40, "The slam (contact): the clasped hands are driven down in front of her to her waist, about 0.15 m in front "
                 "of her belt, arms nearly straight; the chest folds forward 5 degrees, the legs stay together."),
            (54, "Held low while the fist strikes: the hands sink 2-3 cm."),
            (72, "The hands part and return to her sides by the shortest path."),
            (100, "Arms at her sides; she can act again."),
            (112, "Her airborne pose, matching tick 0.")],
     keys=[0, 12, 30, 36, 40, 54, 72, 100, 112], fast=[[36, 42]], strike=['RightHand', 'LeftHand'],
     targets=lambda m: [height('raise: hands above her head (m)', 'RightHand', 'Head', 30, 0.15),
                        dist('raise: hands clasped (m)', 'LeftHand', 'RightHand', 30, None, 0.09),
                        dist('slam: hands clasped (m)', 'LeftHand', 'RightHand', 41, None, 0.09),
                        height('slam: hands at her waist (m)', 'RightHand', 'Hips', 41, -0.08, 0.18),
                        ahead('slam: hands in front of her (m)', 'RightHand', 'Hips', 41, 0.1),
                        dist('legs together (m)', 'LeftFoot', 'RightFoot', 41, None, 0.2)])

move('real-q', form='real', slot='ground.A', semantic='anim.nyx.ga', kind='special', frame='stance', scale=2,
     game={'total': 40, 'contact': 16, 'active': [16, 16], 'iasa': 32, 'model': 'Bonk Pot-Lid Throw 41/projectile 19/35',
           'what': 'the crow projectile leaves at the contact tick'},
     what=("Nyx's real-form Q, Hex Crow: a crow made of shadow forms on her wrist and flies off toward the target. Her part "
           "is a lazy point."),
     beats=[(0, "The stance."),
            (8, "The right arm starts to rise in front of her, unhurried."),
            (22, "Summon: her right arm raised forward to shoulder height, nearly straight, the hand about 0.45 m in front of "
                 "her right shoulder, index and middle fingers pointing ahead together, the other fingers curled, the thumb "
                 "tucked. The crow forms on her wrist (an effect). Her head tilts 3-4 degrees, bored."),
            (30, "She holds the point; the wrist lifts a hair."),
            (32, "Release (the crow takes off): a tiny flick of the two fingers forward, 2-3 cm."),
            (44, "She keeps pointing a moment, unbothered."),
            (64, "The hand has lowered to her side; she can act again."),
            (80, "The stance, matching tick 0.")],
     keys=[0, 8, 22, 30, 32, 44, 64, 80], fast=[[30, 34]], strike=['RightHand'],
     planted=['LeftFoot', 'LeftToeBase', 'RightFoot', 'RightToeBase'],
     targets=lambda m: [floor_t(m, 'LeftToeBase', 32), floor_t(m, 'RightToeBase', 32),
                        height('summon: right hand at shoulder height (m)', 'RightHand', 'RightArm', 24, -0.08, 0.08),
                        ahead('summon: right hand pointing far ahead (m)', 'RightHand', 'RightArm', 24, 0.35),
                        ahead('release: still pointing (m)', 'RightHand', 'RightArm', 34, 0.35)])

move('real-e', form='real', slot='ground.E', semantic='anim.nyx.ge', kind='recovery', frame='air', scale=1,
     game={'total': 50, 'model': "slop.ability.thrust.v1 recovery; salamander Sombrero Copter 50",
           'phases': 'cross by 8, hidden in the bats 10-34 while the game moves the swarm, re-forms 34-40, settles to 50'},
     what=("Nyx's real-form E, Bat Scatter: her recovery. She crosses her arms like a vampire in a coffin, bursts into bats "
           "that fly in the stick's direction, and re-forms. The bats hide her body in the middle (an effect)."),
     beats=[(0, "Her airborne pose."),
            (8, "Cross: arms crossed over her chest like a vampire in a coffin, each hand on the opposite shoulder, the right "
                "forearm in front of the left, legs together and straight, chin slightly down, eyes closed."),
            (34, "Hidden in the bats from tick 10 to 34: hold the cross, drifting slowly (a 5 degree forward tilt of the whole "
                 "body, the chin sinking a little)."),
            (40, "Re-form: the arms uncross outward, the hands opening away from her body, the body straightening."),
            (50, "Settle: upright, arms at her sides, her airborne pose again.")],
     keys=[0, 8, 34, 40, 50], fast=[[34, 40]],
     targets=lambda m: [dist('cross: right hand on her left shoulder (m)', 'RightHand', 'LeftArm', 8, None, 0.14),
                        dist('cross: left hand on her right shoulder (m)', 'LeftHand', 'RightArm', 8, None, 0.14),
                        dist('cross held: right hand on her left shoulder (m)', 'RightHand', 'LeftArm', 34, None, 0.14),
                        side('settle: right hand at her side (m)', 'RightHand', 50, 0.1)])

move('real-r', form='real', slot='ground.R', semantic='anim.nyx.gr', kind='special', frame='stance', scale=1,
     game={'total': 84, 'contact': 24, 'active': [24, 48], 'iasa': 72,
           'model': 'slop.ability.tether.v1 atTarget (ADR-0051): warning to 24, catch, drag until 48; Bashie Cannonball 70',
           'phases': 'summon 0-12, warning 12-24, catch 24, drag 24-48 (the game reels the target), come here 48-64'},
     what=("Nyx's real-form R, Grasp: after a warning a giant shadow hand rises under the target and drags them to her feet. "
           "She only gestures: reach, close the fist, pull it in, and a slow 'come here' with one finger."),
     beats=[(0, "The stance."),
            (12, "Summon: her right arm extended forward at shoulder height, palm down, fingers spread wide, the hand about "
                 "0.5 m ahead on her centre line. Her chin lifts slightly."),
            (22, "Waiting through the warning, a moving hold: the fingers curl very slowly, the hand sinks 2 cm."),
            (24, "Catch: she closes her fist sharply."),
            (40, "Drag: she pulls the fist back toward her chest, the elbow going back past her side; the chest turns 5-8 "
                 "degrees to her right with the pull."),
            (48, "The fist at the middle of her chest, about 0.15 m in front of it, elbow back and down."),
            (56, "Come here: the index finger uncurls and curls back slowly, once, the fist still at her chest."),
            (64, "The finger curls a second time, slower."),
            (84, "The arm lowers to her side, the stance again, matching tick 0.")],
     keys=[0, 12, 22, 24, 40, 48, 56, 64, 84], fast=[[22, 26]], strike=['RightHand'],
     planted=['LeftFoot', 'LeftToeBase', 'RightFoot', 'RightToeBase'],
     targets=lambda m: [floor_t(m, 'LeftToeBase', 24), floor_t(m, 'RightToeBase', 48),
                        ahead('summon: right hand far ahead (m)', 'RightHand', 'Spine2', 12, 0.42),
                        height('summon: right hand at shoulder height (m)', 'RightHand', 'RightArm', 12, -0.1, 0.08),
                        side('summon: hand toward her centre line (m)', 'RightHand', 12, -0.05, 0.16),
                        dist('drag: fist at her chest (m)', 'RightHand', 'Spine2', 48, None, 0.32),
                        ahead('drag: elbow back (m)', 'RightForeArm', 'Spine2', 48, None, -0.02)])

move('swap-golive', form='fake', sheet_form='real', slot='ground.F', semantic='anim.nyx.gf', kind='special', frame='stance',
     scale=2, entry_form='real', exit_form='fake',
     game={'total': 48, 'contact': 24, 'active': [24, 28], 'iasa': 40,
           'model': 'ADR-0051 swapForm with a towardOwner hitbox on the same tick; Bashie Wave Pool 24',
           'what': 'the form swaps and the pull hitbox spawns at the contact tick, inside the flash'},
     meshes_shown=lambda total, swap: {'Nyx_Real': [[0, swap - 1]], 'Nyx_Real_Tights': [[0, swap - 1]], 'Nyx_Real_Dress': [[0, swap - 1]],
                                       'Nyx_Fake': [[swap, total]], 'Nyx_Backpack': [[swap, total]],
                                       'Nyx_BackpackPole': [[swap, total]], 'Nyx_Phone': [[swap, total]]},
     what=("Nyx's F from the real form, Going Live: the swap to her fake form. The real goth rolls her eyes, pulls her phone, "
           "raises it, and a ring-light flash covers her; out of the flash steps Nyxxie, posing and beckoning enemies in."),
     beats=[(0, "The real form's stance (reference `realstance`): upright, heels together, arms down."),
            (4, "Sigh: the head tips back 5-8 degrees and rolls a little, an eye-roll, the shoulders dropping."),
            (14, "Her right hand crosses in front of her waist to her left wrist and pulls a phone out of her left sleeve."),
            (22, "The phone comes out; the right hand starts up."),
            (38, "Lift: the right hand holds the phone at face height, about 0.3 m in front of her face, screen toward her, "
                 "elbow down. The left arm hangs. A flat stare at the screen."),
            (46, "Still holding, the hand rising 2 cm: the flash is about to burst."),
            (48, "Swap flash (contact): the game swaps her body in this one frame. From here she is Nyxxie, and the pose "
                 "jumps into a big performer's pose within 3-4 ticks (the flash hides the jump): her right hand thrown up "
                 "high holding the phone above and to the right of her head, the left arm reaching forward at chest height "
                 "with the palm up, fingers curling in a 'come here' that pulls people in, weight popped onto one hip, "
                 "knees loose, a big grin."),
            (56, "The pose holds while the pull hits: the left fingers curl in once."),
            (64, "The left fingers curl in a second time; the body bounces once in the knees."),
            (80, "She relaxes toward her fake idle; she can act again."),
            (96, "Nyxxie's idle (reference `fakestance`), matching the exit pose.")],
     notes_extra=("The sheet's last pose has the hands the wrong way round: follow this text. The phone is in her right hand "
                  "and the left hand beckons. The rig shows her fake body for the whole clip (one skeleton, two bodies); the "
                  "game draws the real form before tick 48 and the fake form from tick 48. The ticks before 48 are the real "
                  "form: stiff and minimal, as she always is. The ticks after 48 are Nyxxie: loose and bouncy."),
     keys=[0, 4, 14, 22, 38, 46, 48, 52, 56, 64, 80, 96], fast=[[46, 52]], strike=['LeftHand'],
     targets=lambda m: [floor_t(m, 'LeftToeBase', 38), floor_t(m, 'RightToeBase', 56),
                        height('lift: phone hand at face height (m)', 'RightHand', 'Head', 38, -0.14, 0.05),
                        ahead('lift: phone in front of her face (m)', 'RightHand', 'Head', 38, 0.18),
                        height('pose: phone hand thrown up high (m)', 'RightHand', 'Head', 54, 0.08),
                        ahead('pose: left hand reaching forward (m)', 'LeftHand', 'Spine2', 54, 0.28),
                        height('pose: left hand at chest height (m)', 'LeftHand', 'Spine2', 54, -0.18, 0.1)])

# ---------- fake form ----------
move('fake-idle', form='fake', slot='idle', semantic='anim.nyx.fake.idle', kind='idle', frame='idle', loop=True, scale=1,
     game={'total': 720, 'model': "Bashie's slowed idle (12 s loop); the owner's calm-idle rule"},
     what="Nyxxie's idle, 'doomscrolling': the clip she plays whenever she stands in a fight.",
     beats=[(0, "Rest: her weight on her right hip (the right leg straight and the hip popped out to her right, the left knee "
                "relaxed and a little bent, the left boot a step out), the phone at chest height in her right hand about "
                "0.25 m in front of her chest, screen toward her, her head tipped down 10-15 degrees at it, bored. The left "
                "arm hangs loose, the hand by her left thigh. Her right thumb scrolls with a small flick every 1.5-2 s "
                "(Thumb joints bending 15-25 degrees and back)."),
            (150, "Still scrolling, a slow breath; the head tilts 3 degrees to one side."),
            (300, "The one slow gesture begins: she lifts the phone up for a quick selfie."),
            (380, "Selfie: the right arm raised up and forward, the phone about 0.2 m above her head and 0.35 m in front of "
                  "it, screen toward her face; she tilts her head 10 degrees toward the right shoulder and pouts up at it. "
                  "Her left hand comes up beside her cheek in a peace sign (index and middle fingers up, the others curled)."),
            (440, "Still posing, drifting: the head tilts a little further, the peace sign tips."),
            (520, "Back to scrolling at chest height, the left arm hanging again."),
            (720, "Rest again, the same pose as tick 0 (the loop closes).")],
     notes_extra=("Keep it calm: the roster's hand-made idles turn their body joints 2-11 degrees a second and move the hands "
                  "0.05-0.08 body lengths a second. Outside the one gesture only breathing, a slow sway of the popped hip "
                  "(1-2 cm) and the thumb scrolling. No steps, no weight change from hip to hip."),
     keys=[0, 150, 300, 380, 440, 520, 720], grounding=True, planted=['RightFoot', 'RightToeBase'], snap=4,
     targets=lambda m: [floor_t(m, 'LeftToeBase', 0), floor_t(m, 'RightToeBase', 0),
                        height('phone at chest height at tick 0 (m)', 'RightHand', 'Spine2', 0, -0.18, 0.08),
                        ahead('phone in front of her chest at tick 0 (m)', 'RightHand', 'Spine2', 0, 0.12),
                        side('hip popped to her right at tick 0 (m)', 'Hips', 0, 0.01),
                        height('selfie: phone above her head (m)', 'RightHand', 'Head', 400, 0.05),
                        height('peace sign up by her cheek (m)', 'LeftHand', 'Spine2', 400, 0.05),
                        travel('chest breathes (m)', 'Spine2', 'up', [0, 720], 0.004, 0.03)])

move('fake-run', form='fake', slot='run', semantic='anim.nyx.fake.run', kind='run', frame='idle', loop=True, scale=1,
     game={'total': 20, 'model': "a sprint cycle (two steps), the roster's runs are 20-26 ticks"},
     what="Nyxxie's run, 'filming herself': she sprints while filming herself with the phone held out in front.",
     beats=[(0, "Contact: a sprint stride, the right boot landing ahead (about 0.3 m in front of her hips), the left behind "
                "pushing; her right arm stretched out in front holding the phone toward her own face (about 0.4 m ahead of "
                "her face, a little above it), her left arm pumping back."),
            (3, "Down: the weight lands on the front platform boot, knee bent; the phone stays aimed at her face."),
            (6, "Passing: the left leg swings through under her; the backpack and ring light bounce with her (her upper "
                "spine bobs)."),
            (8, "Up: pushing off the right toes, both boots off the floor for a moment, still filming, grinning; the left "
                "arm swings forward."),
            (10, "Contact on the left boot (mirror of tick 0, not an exact copy: the phone arm stays out on the right)."),
            (20, "Back to the pose of tick 0 (the loop closes).")],
     notes_extra=("The phone arm stays out in front the whole loop, bobbing with her and steadying the shot (it moves less "
                  "than the body: a small counter-motion). The left arm pumps hard; the hips and chest twist against each "
                  "other; the head stays aimed at the phone."),
     keys=[0, 3, 6, 8, 10, 13, 16, 18, 20],
     targets=lambda m: [floor_t(m, 'RightToeBase', 1, ' (landing)'), floor_t(m, 'LeftToeBase', 11, ' (landing)'),
                        ahead('filming: phone far ahead of her face (m)', 'RightHand', 'Head', 0, 0.28),
                        ahead('filming: phone ahead at tick 10 (m)', 'RightHand', 'Head', 10, 0.28),
                        height('filming: phone about face height (m)', 'RightHand', 'Head', 5, -0.12, 0.15),
                        travel('left arm pumps (m)', 'LeftHand', 'front', [0, 20], 0.25),
                        travel('stride: left boot travels (m)', 'LeftFoot', 'front', [0, 20], 0.45)])

move('fake-g1', form='fake', slot='ground.1', semantic='anim.nyx.fake.g1', kind='normal', frame='stance', scale=2,
     game={'total': 44, 'contact': 8, 'active': [8, 30], 'iasa': 38, 'hits': [8, 16, 26],
           'model': 'three hitboxes linked by fixedHitstunTicks (ADR-0051); FightGuy Floating Kick 48/8,14; Bashie Whirlpool 40'},
     what=("Nyxxie's ground.1, Phone Slaps: a three-hit slap string. A backhand with the phone hand, a forehand with the open "
           "left hand, then an overhand smack down with the phone."),
     beats=[(0, "The stance."),
            (6, "Load the backhand: the phone hand swings across to in front of her left shoulder, the back of the hand "
                "facing ahead; the chest turns 20 degrees to her left; a bounce down in the knees."),
            (16, "Hit 1, backhand (contact): the phone hand whips out forward and to the right, the back of the hand leading, "
                 "arm nearly straight at chest height, crossing her centre line about 0.35 m ahead of her chest; the chest "
                 "turns through to 15 degrees right; her left boot slides 8-10 cm forward. The left hand is drawing back "
                 "beside her left shoulder, palm open, for the forehand."),
            (22, "The phone hand keeps going out to her right; the left hand cocked back."),
            (32, "Hit 2, forehand (contact): the open left palm swings forward and across, palm leading, at face height, "
                 "about 0.35 m ahead of her chest on her centre line; the chest turns back to her left; the phone hand pulls "
                 "back and up toward her right shoulder."),
            (44, "Wind-up for the smack: the phone hand high above her right shoulder, elbow up and back, the chest turned "
                 "right, up on the balls of her feet (this is the sheet's pose 4, which is the wind-up, not the hit)."),
            (52, "Hit 3, smack (contact): the phone hand chops down in front of her on her centre line, arm extending down "
                 "and forward, the phone ending about 0.3 m ahead of her chest at belly height; the chest folds 10-15 degrees "
                 "forward, the knees dip."),
            (60, "The hit carries through: the hand sinks a little lower, the body settling."),
            (76, "Recovered: the arms back to the stance by the shortest path (the stance already holds the phone at chest "
                 "height); she can act again."),
            (88, "The stance, matching tick 0.")],
     keys=[0, 6, 16, 22, 32, 44, 52, 60, 76, 88], fast=[[10, 18], [26, 34], [46, 54]], strike=['RightHand', 'LeftHand'],
     targets=lambda m: [floor_t(m, 'RightToeBase', 16), floor_t(m, 'LeftToeBase', 32), floor_t(m, 'RightToeBase', 52),
                        ahead('backhand: phone hand well ahead (m)', 'RightHand', 'Spine2', 16, 0.28),
                        side('backhand: on her centre line (m)', 'RightHand', 16, -0.08, 0.12),
                        ahead('forehand: left hand well ahead (m)', 'LeftHand', 'Spine2', 32, 0.28),
                        side('forehand: on her centre line (m)', 'LeftHand', 32, -0.12, 0.08),
                        height('smack wind-up: phone hand above her head (m)', 'RightHand', 'Head', 44, 0.02),
                        ahead('smack: phone hand ahead (m)', 'RightHand', 'Spine2', 52, 0.22),
                        side('smack: on her centre line (m)', 'RightHand', 52, -0.08, 0.12),
                        height('smack: phone hand low, below her chest (m)', 'RightHand', 'Spine2', 52, -0.45, -0.05)])

move('fake-g3', form='fakepole', slot='ground.2', semantic='anim.nyx.fake.g2', kind='normal', frame='stance', scale=2,
     game={'total': 48, 'contact': 16, 'active': [16, 24], 'iasa': 40,
           'model': 'Wibou Up Slash 63/14/26, FightGuy High Kick 33/8; a weapon launcher along the pole'},
     meshes_shown=lambda total, _: {'Nyx_PoleHeld': [[16, 72]], 'Nyx_BackpackPole': [[0, 15], [73, total]]},
     what=("Nyxxie's ground.2, Ring Light Uppercut: her launcher. She pulls the ring-light pole off her backpack with her "
           "left hand and swings it up in a big arc from low in front of her to straight overhead."),
     beats=[(0, "The stance."),
            (8, "Her left hand reaches up over her left shoulder toward the pole on her backpack; the chest turns a little "
                "left."),
            (16, "Grab: the left hand closes on the pole just above her left shoulder (from here the pole is in her left "
                 "hand)."),
            (26, "Low: she has swung the pole down and forward in front of her, the left hand low in front of her hips, the "
                 "ring light low ahead of her, about 0.4 m above the floor and 0.5 m ahead, on her centre line; knees bent "
                 "deep (hips down 8-10 cm), the chest leaning forward, the phone hand pulled in at her chest."),
            (32, "The swing (contact): she drives up from her knees and the pole sweeps up in front of her, the ring light "
                 "leading, passing ahead of her chest on her centre line."),
            (40, "Still swinging: the ring passes above her face, ahead of her."),
            (48, "Overhead: the pole straight up over her head, her left arm extended up, the ring light at the top of the "
                 "arc above her; she rises tall on her boots, a cocky grin up at it. The phone hand is up near her face, "
                 "filming."),
            (56, "Held overhead a moment, drifting."),
            (72, "Return: she slides the pole back onto her backpack over her left shoulder, the left hand behind her left "
                 "shoulder."),
            (80, "The hand comes off the pole; she can act again."),
            (96, "The stance, matching tick 0.")],
     notes_extra=("The arc is the hitbox: from tick 26 to 48 the ring light (bladeEnd) must sweep one clean, wide arc in the "
                  "vertical plane through her centre line, from low in front up to straight overhead, never parked, never "
                  "beside her. Her body throws it: the knees drive up, the chest straightens and the arm follows. The "
                  "sheet shows both hands on the pole; she swings it with her left hand alone, and her right hand keeps the "
                  "phone (it may steady the pole with its knuckles at the low pose)."),
     keys=[0, 8, 16, 26, 32, 40, 48, 56, 72, 80, 96], fast=[[26, 44]], strike=['bladeEnd'],
     targets=lambda m: [floor_t(m, 'LeftToeBase', 26), floor_t(m, 'RightToeBase', 26),
                        height('grab: left hand up at her left shoulder (m)', 'LeftHand', 'LeftArm', 16, 0.02),
                        T('low: hips dip (m)', 'char', 'Hips', 26, None, -0.06, axis='up'),
                        ahead('low: ring light ahead of her (m)', 'bladeEnd', 'Hips', 26, 0.3),
                        T('low: ring light low (m)', 'char', 'bladeEnd', 26, None, round(m['floor'] + 0.6, 3), axis='up'),
                        ahead('swing: ring light ahead of her chest (m)', 'bladeEnd', 'Spine2', 32, 0.35),
                        side('swing: ring light on her centre line (m)', 'bladeEnd', 34, -0.15, 0.15),
                        height('overhead: ring light high above her head (m)', 'bladeEnd', 'Head', 48, 0.45),
                        side('overhead: ring light over her centre line (m)', 'bladeEnd', 48, -0.2, 0.15),
                        travel('the arc: ring light sweeps up (m)', 'bladeEnd', 'up', [26, 48], 1.0)])

move('fake-g4', form='fake', slot='ground.3', semantic='anim.nyx.fake.g3', kind='normal', frame='stance', scale=2,
     game={'total': 52, 'contact': 16, 'active': [16, 22], 'iasa': 46,
           'model': 'FightGuy Double Kick 69/12/64, Manki Head Slam 40/10/35'},
     what="Nyxxie's ground.3, Platform Stomp-Kick: her big one. A front push-kick with the huge platform boot.",
     beats=[(0, "The stance."),
            (10, "Weight shifts onto her left leg; the right knee starts to rise; the phone hand comes up to film."),
            (24, "Chamber: the right knee raised high to her chest, the huge platform boot cocked under her, the thigh above "
                 "level; she leans back a little; arms out for balance, the phone hand up by her face filming the kick."),
            (28, "A moving hold: the knee rises 2-3 cm more, she sinks a little on the left knee."),
            (32, "Kick (contact): the right leg drives straight forward, sole first, at chest height, about 0.6 m ahead of her "
                 "hips on her centre line, the leg almost straight; she leans back 15-20 degrees to counter it; arms flung "
                 "out to the sides."),
            (44, "Held out through the hit, the boot drifting up 2-3 cm."),
            (52, "The leg folds back, knee high again."),
            (60, "Stamp: the boot stamps down to the floor ahead of her with a dip in both knees."),
            (92, "Recovered by the shortest path back to the stance; she can act again."),
            (104, "The stance, matching tick 0.")],
     keys=[0, 10, 24, 28, 32, 44, 52, 60, 92, 104], fast=[[26, 34]], strike=['RightFoot'],
     targets=lambda m: [floor_t(m, 'LeftToeBase', 32, ' (standing leg)'), floor_t(m, 'RightToeBase', 62, ' (stamp)'),
                        height('chamber: right knee high (m)', 'RightLeg', 'Hips', 24, -0.04),
                        ahead('kick: boot far ahead (m)', 'RightFoot', 'Hips', 32, 0.5),
                        height('kick: boot at chest height (m)', 'RightFoot', 'Hips', 32, 0.12),
                        side('kick: boot on her centre line (m)', 'RightFoot', 32, -0.12, 0.12),
                        ahead('kick: leaning back (m)', 'Head', 'Hips', 32, None, -0.02)])

move('fake-a1', form='fake', slot='air.1', semantic='anim.nyx.fake.a1', kind='normal', frame='air', scale=2,
     game={'total': 36, 'contact': 7, 'active': [7, 16], 'iasa': 31, 'landingLag': 9,
           'model': 'Manki Air Kick 38/7 ll9, Wibou Air Slash 27/6 ll9'},
     what="Nyxxie's air.1, Spinning Slap: her neutral aerial. She spins a full turn with the phone hand out, slapping around her.",
     beats=[(0, "Her airborne pose."),
            (8, "Wind: the chest and hips turn 40 degrees to her RIGHT, the phone arm pulled back out to her right, knees "
                "tucking."),
            (14, "Spin (contact): she whips round to her LEFT (counter-clockwise seen from above), the right arm extended at "
                 "shoulder height, the phone hand sweeping across in front of her on her centre line, about 0.5 m ahead."),
            (18, "A quarter turn on: the body facing her left, the phone arm trailing."),
            (22, "Half a turn: her back to the front, the arm out behind her."),
            (26, "Three quarters: facing her right."),
            (32, "Finish: the full turn completed, facing front again, the arms opening out, hair flying."),
            (44, "Unwinding, arms opening and settling."),
            (62, "Recovered: the airborne pose returning; she can act again."),
            (72, "Her airborne pose, matching tick 0.")],
     notes_extra="Key the spin at least every quarter turn (ticks 14, 18, 22, 26, 32) so the hips turn the long way round, a full 360 degrees.",
     keys=[0, 8, 14, 18, 22, 26, 32, 44, 62, 72], fast=[[8, 34]], strike=['RightHand'],
     targets=lambda m: [ahead('spin: phone hand ahead at the hit (m)', 'RightHand', 'Spine2', 14, 0.3),
                        side('spin: on her centre line (m)', 'RightHand', 14, -0.15, 0.15),
                        ahead('half turn: phone hand behind her (m)', 'RightHand', 'Hips', 22, None, -0.2),
                        travel('the phone hand circles (m)', 'RightHand', 'right', [8, 32], 0.6),
                        height('knees tucked (m)', 'LeftLeg', 'Hips', 14, -0.2)])

move('fake-a3', form='fakepole', slot='air.2', semantic='anim.nyx.fake.a2', kind='normal', frame='air', scale=2,
     game={'total': 44, 'contact': 12, 'active': [12, 22], 'iasa': 38, 'landingLag': 10,
           'model': 'salamander Tail Whip 44/12 ll10, Bonk air Sunrise 54/20'},
     meshes_shown=lambda total, _: {'Nyx_PoleHeld': [[14, 66]], 'Nyx_BackpackPole': [[0, 13], [67, total]]},
     what=("Nyxxie's air.2, Ring Light Overhead: her up aerial. She grabs the ring-light pole off her backpack and swings it in "
           "an arc over her head from front to back."),
     beats=[(0, "Her airborne pose."),
            (8, "The left hand reaches up over her left shoulder to the pole on her backpack."),
            (14, "Grab: the left hand closes on the pole above her shoulder (from here it is in her hand)."),
            (20, "She brings the pole forward and down in front of her, the ring light ahead of her at chest height; knees "
                 "tuck."),
            (24, "Arc start (contact): the ring light ahead of her face, on her centre line, starting to rise."),
            (34, "The ring light straight above her head, the left arm extended up, on her centre line."),
            (44, "Arc end: the ring light behind her head, the arm up and back; she arches a little, legs kicking out "
                 "in front for balance."),
            (52, "The pole comes back up toward her shoulder."),
            (66, "Return: the pole slid back onto the backpack over her left shoulder."),
            (76, "The hand off the pole; she can act again."),
            (88, "Her airborne pose, matching tick 0.")],
     notes_extra=("The arc is the hitbox: from tick 24 to 44 the ring light (bladeEnd) sweeps one clean arc over her head "
                  "from front to back, in the vertical plane through her centre line, never parked. Her chest and shoulders "
                  "throw it. The right hand keeps the phone up, filming."),
     keys=[0, 8, 14, 20, 24, 34, 44, 52, 66, 76, 88], fast=[[20, 46]], strike=['bladeEnd'],
     targets=lambda m: [height('grab: left hand up at her left shoulder (m)', 'LeftHand', 'LeftArm', 14, 0.02),
                        ahead('arc start: ring light ahead of her (m)', 'bladeEnd', 'Head', 24, 0.35),
                        height('overhead: ring light high above her head (m)', 'bladeEnd', 'Head', 34, 0.45),
                        side('overhead: over her centre line (m)', 'bladeEnd', 34, -0.2, 0.15),
                        ahead('arc end: ring light behind her head (m)', 'bladeEnd', 'Head', 44, None, -0.15),
                        travel('the arc sweeps front to back (m)', 'bladeEnd', 'front', [22, 44], 0.8)])

move('fake-a4', form='fake', slot='air.3', semantic='anim.nyx.fake.a3', kind='normal', frame='air', scale=2,
     game={'total': 48, 'contact': 12, 'active': [12, 24], 'iasa': 42, 'landingLag': 12,
           'model': 'salamander Spur Stomp 50/12-25 ll12, Manki Air Smash 62 ll12'},
     what="Nyxxie's air.3, Double Dropkick: her spike. Both platform boots drive straight down.",
     beats=[(0, "Her airborne pose."),
            (14, "Tuck: knees pulled up to her chest, boots together under her seat, arms rising out to the sides, the phone "
                 "hand up."),
            (20, "A moving hold, tucking tighter: the knees rise 2-3 cm more."),
            (24, "Stomp (contact): both legs driven straight down, locked, boots together, soles flat and down; arms thrown "
                 "up and out for show; the body stretched long."),
            (40, "Held while the hit lasts, the legs drifting down 2 cm, the arms opening."),
            (48, "End of the hit."),
            (64, "The legs bend back up, arms out for balance."),
            (84, "Recovered; she can act again."),
            (96, "Her airborne pose, matching tick 0.")],
     keys=[0, 14, 20, 24, 40, 48, 64, 84, 96], fast=[[20, 26]], strike=['LeftFoot', 'RightFoot'],
     targets=lambda m: [height('tuck: knees up (m)', 'LeftLeg', 'Hips', 18, -0.05),
                        height('stomp: left boot far below the hips (m)', 'LeftFoot', 'Hips', 24, None, -0.6),
                        height('stomp: right boot far below the hips (m)', 'RightFoot', 'Hips', 24, None, -0.6),
                        dist('stomp: boots together (m)', 'LeftFoot', 'RightFoot', 28, None, 0.18)])

move('fake-q', form='fake', slot='ground.A', semantic='anim.nyx.fake.ga', kind='special', frame='stance', scale=2,
     game={'total': 36, 'contact': 10, 'active': [10, 14], 'iasa': 30,
           'model': 'startupAimCorrection snap turn, then a short cone (ADR-0051); Wibou Shuriken Toss 24/8/16'},
     what=("Nyxxie's Q, Ring Light: she snaps to face the target and flicks her ring light on: a short flash cone dazzles "
           "everyone facing her, while she pulls an exaggerated duck pout."),
     beats=[(0, "The stance."),
            (6, "Snap: a quick whip of the hips and chest to square up to the front, the feet jumping into a wide stance "
                "(boots about 0.55 m apart, knees bent); the phone comes up in her right hand toward face height."),
            (12, "Set: wide stance, the phone held up and to the right of her face, screen toward her; her left hand rising "
                 "toward the ring light above her left shoulder."),
            (20, "Flash (contact): her left hand flicks up and taps the ring light, the hand at the pole about "
                 f"{round(RING * 0.7, 2)} m above her left shoulder; her head tilts back 10 degrees and toward the phone in a "
                 "duck pout; a little pop up in the knees."),
            (28, "End of the flash, holding the pose."),
            (48, "Still holding, drifting: the pout eases, the left hand slides down the pole a little."),
            (60, "The arms come back to the stance; she can act again."),
            (72, "The stance, matching tick 0.")],
     keys=[0, 6, 12, 20, 28, 48, 60, 72], fast=[[2, 10], [16, 22]], strike=['LeftHand'],
     targets=lambda m: [floor_t(m, 'LeftToeBase', 12), floor_t(m, 'RightToeBase', 20),
                        dist('wide stance (m)', 'LeftFoot', 'RightFoot', 20, 0.42),
                        height('flash: left hand up at the ring light (m)', 'LeftHand', 'LeftArm', 20, round(RING * 0.45, 3)),
                        side('flash: left hand above her left shoulder (m)', 'LeftHand', 20, None, -0.05),
                        height('flash: phone up at face height (m)', 'RightHand', 'Head', 20, -0.15, 0.12)])

move('fake-e', form='fake', slot='ground.E', semantic='anim.nyx.fake.ge', kind='recovery', frame='air', scale=1,
     game={'total': 50, 'model': 'slop.ability.aimed-recovery.v1 (ADR-0051); salamander Sombrero Copter 50 (16 startup)',
           'phases': 'startup 0-12 (grab the straps), the game lifts her from 12'},
     what=("Nyxxie's E, Ring Light Rise: her recovery. The ring light spins like a helicopter rotor above her and lifts her by "
           "the backpack (the spin is an effect); she hangs from the straps, legs dangling and kicking, filming herself."),
     beats=[(0, "Her airborne pose."),
            (8, "Grab: both hands grab the backpack's shoulder straps at the front of her shoulders (the right hand still "
                "holding the phone), knees pulling up, a little crouch of the whole body."),
            (12, "Lift-off: the shoulders hike up 3-4 cm as if the backpack pulls her up by the straps, the legs dropping to "
                 "hang below her."),
            (24, "Hanging and rising: the left hand on its strap, the right hand lifting the phone out in front of her face "
                 "to film herself; the legs dangle and kick in turn (the left knee lifts)."),
            (36, "The right knee lifts while the left drops; she grins at the phone."),
            (50, "Still hanging and rising, the left knee lifting again (the game moves on to her fall).")],
     keys=[0, 8, 12, 24, 36, 50],
     targets=lambda m: [dist('grab: left hand on the strap at her shoulder (m)', 'LeftHand', 'LeftArm', 8, None, 0.22),
                        dist('grab: right hand on the strap at her shoulder (m)', 'RightHand', 'RightArm', 8, None, 0.22),
                        dist('hanging: left hand stays on the strap (m)', 'LeftHand', 'LeftArm', 36, None, 0.22),
                        ahead('filming: phone ahead of her face (m)', 'RightHand', 'Head', 36, 0.22),
                        travel('legs kick (m)', 'LeftFoot', 'up', [12, 50], 0.08)])

move('fake-r', form='fake', slot='ground.R', semantic='anim.nyx.fake.gr', kind='special', frame='stance', scale=1,
     game={'total': 76, 'contact': 10, 'active': [10, 14], 'iasa': 68,
           'model': 'slop.ability.command-grab.v1 (ADR-0051): lunge, capture, a hold with the victim Grabbed, an authored launch',
           'phases': 'lunge 0-10, capture 10-14, hook 14-22, selfie 22-50 (flash at 44), shove and release at 52, recovery to 76'},
     what=("Nyxxie's R, Selfie Grab: a command grab. She lunges, hooks the opponent around the neck cheek to cheek, takes a "
           "selfie with her tongue out, and the flash blasts them away as she shoves them off with her hip."),
     beats=[(0, "The stance."),
            (6, "Lunge: a quick step forward onto her left boot (25-30 cm), the left arm reaching out ahead at chest height, "
                "fingers open; the phone hand up."),
            (10, "Capture (the grab connects): the left arm fully out ahead on her centre line, about 0.5 m in front of her "
                 "chest."),
            (20, "Hook: the left arm hooks around the (invisible) opponent's neck and pulls them close, cheek to cheek at her "
                 "left: the left elbow bent at shoulder height, the forearm about 0.3 m ahead and to her left of her chest; "
                 "her head leans left toward theirs."),
            (30, "Selfie: the phone held up at arm's length in front of both faces (the right hand above her head height, "
                 "about 0.45 m ahead, a little left of her centre); she leans her head on theirs and sticks her tongue out."),
            (44, "The flash pops from the phone: a little bounce in her knees, the head tilting further."),
            (50, "Shove: she bumps the opponent away forward with her left hip (the hips shift 5-6 cm to her left and turn "
                 "left), pushing out with the left arm, still looking at the phone."),
            (52, "Release: the left arm extended forward-left, the opponent gone."),
            (68, "Recovered by the shortest path; she can act again."),
            (76, "The stance, matching tick 0.")],
     notes_extra="The sheet's last pose moves the phone to her left hand: follow this text, the phone stays in her right hand.",
     keys=[0, 6, 10, 20, 30, 44, 50, 52, 68, 76], fast=[[2, 12], [46, 54]], strike=['LeftHand'],
     targets=lambda m: [floor_t(m, 'LeftToeBase', 10, ' (lunge)'),
                        ahead('capture: left hand far ahead (m)', 'LeftHand', 'Spine2', 10, 0.42),
                        side('capture: on her centre line (m)', 'LeftHand', 10, -0.15, 0.08),
                        height('hook: left arm at shoulder height (m)', 'LeftHand', 'LeftArm', 20, -0.15, 0.12),
                        height('selfie: phone up high (m)', 'RightHand', 'Head', 32, 0.0),
                        ahead('selfie: phone held out ahead (m)', 'RightHand', 'Head', 32, 0.3),
                        ahead('shove: left arm pushing out ahead (m)', 'LeftHand', 'Spine2', 52, 0.35),
                        side('shove: hip bump to her left (m)', 'Hips', 50, None, -0.03)])

move('swap-logoff', form='real', sheet_form='fake', slot='ground.F', semantic='anim.nyx.fake.gf', kind='special',
     frame='stance', scale=2, entry_form='fake', exit_form='real',
     game={'total': 48, 'contact': 20, 'active': [20, 24], 'iasa': 40,
           'model': 'ADR-0051 swapForm with an awayFromOwner hitbox on the same tick; Bashie Wave Pool 24',
           'what': 'the form swaps and the push hitbox spawns at the contact tick, inside the shard burst'},
     meshes_shown=lambda total, swap: {'Nyx_Fake': [[0, swap - 1]], 'Nyx_Backpack': [[0, swap - 1]],
                                       'Nyx_BackpackPole': [[0, swap - 1]], 'Nyx_Phone': [[0, 9]],
                                       'Nyx_Real': [[swap, total]], 'Nyx_Real_Tights': [[swap, total]],
                                       'Nyx_Real_Dress': [[swap, total]]},
     what=("Nyxxie's F, Logging Off: the swap back to the real form. Her phone slips out of her hand, her grin dies, a burst "
           "of shadow shards covers her, and the real goth stands there pushing a ring of shards out along the floor."),
     beats=[(0, "Nyxxie's stance (reference `fakestance`)."),
            (8, "Drop: her fingers open and the phone slips out of her right hand (the hand stays open from here); her hand "
                "drops after it a little too late."),
            (22, "Her face falls: the shoulders slump, the head drops 10 degrees, the arms hang, the hips settle off the "
                 "pop to stand on both feet; the energy drains out of her."),
            (34, "Deflated, still sinking slowly: the knees soften, the chest caves a little."),
            (40, "Swap burst (contact): the game swaps her body in this one frame. From here she is the real form, and within "
                 "3-4 ticks (the shards hide the jump) she stands bolt upright, heels together, arms opened slightly away "
                 "from her body, palms out and down (hands 0.3 m out from her hips), fingers spread: she pushes the "
                 "shard ring out."),
            (56, "The push holds, the hands drifting out 2 cm, stiff and upright."),
            (72, "The arms return to her sides by the shortest path."),
            (80, "She can act again."),
            (96, "The real form's idle (reference `realstance`), the bored stare, matching the exit pose.")],
     notes_extra=("The rig shows her real body for the whole clip (one skeleton, two bodies); the game draws the fake form "
                  "before tick 40 and the real form from tick 40. Before 40 she is Nyxxie, loose, then deflating; after "
                  "40 she is the real form: stiff, minimal, upright. Her right hand is open from tick 8 (no phone)."),
     keys=[0, 8, 22, 34, 40, 44, 56, 72, 80, 96], fast=[[38, 46]], strike=['RightHand', 'LeftHand'],
     targets=lambda m: [floor_t(m, 'LeftToeBase', 22), floor_t(m, 'RightToeBase', 44),
                        height('slump: head dropped (m)', 'Head', 'Spine2', 30, None, 0.3),
                        side('push: right hand out from her side (m)', 'RightHand', 46, 0.28),
                        side('push: left hand out from her side (m)', 'LeftHand', 46, None, -0.28),
                        height('push: hands low, at hip height (m)', 'RightHand', 'Hips', 46, -0.15, 0.15)])


# ----- briefs ------------------------------------------------------------------------------------------------------
def sheet_images(m):
    """The brief's images and their notes: her model render (both for a swap), then the move's pose sheet."""
    s = SHEET_MOVES[m['id']]
    forms = ['real', 'fake'] if m['id'].startswith('swap') else [m['form'].replace('pole', '')]
    images = [f"{RENDERS}/{f}/q3.png" for f in forms] + [f"{SHEETS}/{m['id']}.png"]
    notes = [f"{n + 1}: Nyx's {f} form model (three-quarter render)." for n, f in enumerate(forms)]
    poses = '; '.join(f"pose {i + 1} '{p[0]}': {p[1]}" for i, p in enumerate(s['poses']))
    notes.append(f"{len(forms) + 1}: the pose sheet for this move (generated art on her model; she faces LEFT in it, so "
                 f"'toward the left' on the sheet is straight ahead of her in this frame). Its poses read left to right: "
                 f"{poses}. Sheet note: {s.get('note', '')} Follow this brief's text and ticks where the sheet differs.")
    return images, ' '.join(notes)


def write_brief(m, packs_ok):
    form = m['form']
    s = m['scale']
    total = A(m['game']['total'], s)
    frame = m['frame']
    pack = (MOVE_PACKS if frame == 'stance' else PACKS)[form]
    if frame == 'stance':
        entry_form, exit_form = m.get('entry_form', form.replace('pole', '')), m.get('exit_form', form.replace('pole', ''))
        if not {entry_form, exit_form} <= packs_ok or not (pack_path(pack) / 'rig.usda').exists():
            return None
        refs = {'realstance': 'references/realstance.usda', 'fakestance': 'references/fakestance.usda',
                'idle': 'references/idle.usda', 'fall': 'references/fall.usda', 'run': 'references/run.usda'}
        entry = {'reference': f'{entry_form}stance', 'tick': 0}
        exit_ = {'reference': f'{exit_form}stance', 'tick': 0, 'from_tick': total}
        meas = STANCE[entry_form]
        place = [floor_text(meas, f"the hips of her idle at tick 0"), STANCE_TEXT[entry_form]]
    elif frame == 'idle':
        refs = {'idle': 'references/idle.usda', 'run': 'references/run.usda', 'fall': 'references/fall.usda'}
        entry = {'reference': 'idle', 'tick': 0, 'match': False}
        exit_ = None
        meas = FRAMES[form]['idle']
        place = [floor_text(meas, "the hips of a borrowed stance at tick 0"),
                 "The entry reference is another fighter's idle borrowed onto her body; it only sets the frame and the floor. "
                 "Tick 0 is free."]
    else:
        refs = {'fall': 'references/fall.usda', 'idle': 'references/idle.usda'}
        entry = {'reference': 'fall', 'tick': 0, 'match': False}
        exit_ = None
        meas = {}
        place = [AIR[form.replace('pole', '')]]
    sheet_form = m.get('sheet_form', form).replace('pole', '')
    look_form = form.replace('pole', '')
    clip_text = (f"The clip ({total} ticks, {'a loop: the last tick equals the first' if m.get('loop') else 'played once'}):\n"
                 + '\n'.join(f"- Tick {t}: {txt}" for t, txt in m['beats']))
    swap = m['id'].startswith('swap')
    parts = [m['what']] + ([WHO['real'], WHO['fake']] if swap else [WHO[look_form]])
    if look_form == 'fake' or swap:
        parts.append(PHONE)
    parts += [GAME]
    if m['kind'] in ('normal', 'special') and s > 1:
        parts.append(SLOW)
    if m['kind'] == 'normal':
        parts.append(NORMAL)
    if m['kind'] in ('normal', 'special', 'recovery'):
        parts.append(FLOW)
    parts += place + [clip_text]
    if m.get('notes_extra'):
        parts.append(m['notes_extra'])
    g = m['game']
    clock = {'duration_ticks': total}
    if 'contact' in g:
        clock.update(contact_tick=A(g['contact'], s), active=[A(g['active'][0], s), A(g['active'][1], s)], iasa_tick=A(g['iasa'], s))
    fixed = [f"{total} ticks {'looping' if m.get('loop') else 'played once'}"]
    if 'contact' in g:
        fixed.append(f"contact at tick {clock['contact_tick']}, active ticks {clock['active'][0]}-{clock['active'][1]}; the "
                     f"player can act from tick {clock['iasa_tick']}")
    if s > 1:
        fixed.append(f"authored at {s}x the proposed game length ({g['total']} ticks); the game plays it faster, so every "
                     f"beat keeps its proportion")
    if exit_:
        fixed.append(f"tick 0 is her {entry['reference']} pose and tick {total} her {exit_['reference']} pose exactly")
    brief = {'name': f"nyx-{m['id']}", 'rigpack': f'artifacts/genra-rigpacks/{pack}', 'character': CHAR[form],
             'notes': (f"Nyx {sheet_form if m['id'].startswith('swap') else look_form} form {m['slot']} "
                       f"({m['semantic']}), from her pose sheet {m['id']} (2026-10-29). Proposed game timing: "
                       f"{json.dumps(g)}; authored at {s}x. Candidate only: nothing binds before the owner approves."),
             'description': '\n\n'.join(parts), 'references': refs, 'entry': entry, 'clock': clock,
             'timing': {'fixed': fixed, 'adjustable': [
                 'the beats inside the clip, as long as the sheet poses read' if m['kind'] in ('idle', 'run') else
                 'small pose details (angles, reach) that keep the sheet pose and the timing']},
             'views': ['gameplay', 'profile', 'three-quarter-left' if m.get('strike') and all(j.startswith('Left') for j in m['strike'])
                       else 'three-quarter-right'],
             'images': sheet_images(m)[0], 'image_notes': sheet_images(m)[1],
             'keys': m['keys'], 'review_ticks': sorted(set(m['keys']) | {(a + b) // 2 for a, b in zip(m['keys'], m['keys'][1:])})}
    if m.get('loop'):
        brief['loop'] = {'from_tick': 0}
    if exit_:
        brief['exit'] = exit_
    if m.get('fast'):
        brief['fast_windows'] = m['fast']
    if m.get('strike'):
        brief['strike'] = m['strike']
    if m.get('planted'):
        brief['planted'] = m['planted']
    if m.get('grounding'):
        brief['grounding'] = 'reference-sole'
    if m.get('snap'):
        brief['snap_degrees_per_tick'] = m['snap']
    if m.get('meshes_shown'):
        brief['meshes_shown'] = m['meshes_shown'](total, clock.get('contact_tick'))
    brief['targets'] = m['targets'](meas)
    path = REPO / 'tools/genra/briefs' / f"{brief['name']}.json"
    path.write_text(json.dumps(brief, indent=2, ensure_ascii=False) + '\n')
    return path


timing = {'note': ("Nyx's proposed gameplay timing, 2026-10-29, from the roster's equivalent moves (character.json). Game "
                   "ticks at 60 Hz: total = stage durationTicks, contact = first hitbox (or projectile / swap) tick, active = "
                   "first and last hitbox ticks, iasa, landingLag for aerials. 'authored' is the GenRA clip's clock: attacks "
                   "are authored at scale 2 (twice the game length) and the game plays them over the game total. Engine "
                   "phases read their move data from here; nothing is bound yet."),
          'moves': {}}
packs_ok = set(STANCE)
written, skipped = [], []
for m in MOVES:
    s = m['scale']
    g = {k: v for k, v in m['game'].items()}
    entry = {'form': m.get('sheet_form', m['form']).replace('pole', ''), 'slot': m['slot'], 'semanticId': m['semantic'],
             'name': SHEET_MOVES[m['id']]['name'], 'kind': m['kind'], 'game': g, 'scale': s,
             'authored': {'total': A(g['total'], s)}}
    if 'contact' in g:
        entry['authored'].update(contact=A(g['contact'], s), active=[A(g['active'][0], s), A(g['active'][1], s)], iasa=A(g['iasa'], s))
    if 'hits' in g:
        entry['authored']['hits'] = [A(h, s) for h in g['hits']]
    timing['moves'][m['id']] = entry
    if ONLY and m['id'] not in ONLY:
        continue
    path = write_brief(m, packs_ok)
    (written if path else skipped).append(m['id'])
(OUT / 'timing.json').write_text(json.dumps(timing, indent=2) + '\n')
print('wrote', len(written), 'briefs:', ' '.join(written))
if skipped:
    print('skipped until the stance packs exist:', ' '.join(skipped))
