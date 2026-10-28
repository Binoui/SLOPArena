"""Write the salamander's proposed kit into his package's character.json (all ten slots, capabilities, presentation ids).

    python3 tools/blender/salamander_rig/apply_kit.py [--out PATH] [--check]

Rerunnable: the slots are rebuilt from the table below every time; everything else in character.json (movement, body
volumes, presentation sizes, borrowed reaction clips) is kept. Timings match the GenRA briefs written by
make_briefs.py, so each clip fits its move. Without --out it overwrites the package source in place; --check prints the
slot summary only. The kit and its reasons: tmp/salamander-gunslinger/proposal/kit.md.
"""
import argparse, copy, json
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
SRC = REPO / 'client/Unity/Assets/CharacterPackages/salamander/character.json'
A = 'anim.salamander.'

def aim_correction(end):
    return {'kind': 'startupAimCorrection', 'tick': 0, 'unit': 'ticks', 'endTick': end, 'acquisitionRange': 4,
            'acquisitionHalfAngleDegrees': 180, 'maxYawDegrees': 90, 'maxPitchDegrees': 0, 'yawDegreesPerSecond': 720,
            'pitchDegreesPerSecond': 0}

def hitbox(tick, ticks, damage, angle, bkb, kbg, stun, *, bone=None, end_bone=None, shape='sphere', radius=0.5,
           off=(0, 0, 0), end=(0, 0, 0), group=0):
    return {'kind': 'spawnHitbox', 'tick': tick, 'unit': 'meters', 'hitbox': {
        'shape': shape, 'radius': radius, 'offsetX': off[0], 'offsetY': off[1], 'offsetZ': off[2],
        'endOffsetX': end[0], 'endOffsetY': end[1], 'endOffsetZ': end[2], 'startBoneId': bone, 'endBoneId': end_bone,
        'damage': damage, 'angle': angle, 'baseKnockback': bkb, 'knockbackGrowth': kbg, 'stunTicks': stun,
        'durationTicks': ticks, 'interruptible': True, 'hitGroup': group, 'knockbackDirection': 'awayFromOwner'}}

def projectile(**kw):
    p = {'launchOffsetX': 0, 'launchOffsetY': 0, 'launchOffsetZ': 0, 'speed': 20, 'gravity': 0, 'radius': 0.2, 'damage': 4,
         'angle': 15, 'baseKnockback': 5, 'knockbackGrowth': 12, 'stunTicks': 10, 'maxFlightTicks': 30, 'yawOffsetDegrees': 0}
    p.update(kw)
    return p

def stage(duration, anims, ops, iasa=0, landing=0, attack_range=0):
    return {'durationTicks': duration, 'iasaTicks': iasa, 'landingLagTicks': landing, 'autoCancelBeforeTicks': 0,
            'autoCancelAfterTicks': 0, 'attackRange': attack_range, 'warpRange': 0, 'useTargetLock': False,
            'rotateTowardTarget': False, 'trackingStrength': 0, 'animationIds': anims, 'operations': ops}

def slot(sid, name, desc, stages, *, behavior='meleeCombo', aim='none', cooldown=0, recovery=False, slide=False, **extra):
    s = {'id': sid, 'name': name, 'description': desc, 'iconId': 'icon.' + sid.split('.')[1].lower(), 'behavior': behavior,
         'aimMode': aim, 'aimMovement': 'fixed', 'cooldownTicks': cooldown, 'isRecoveryMove': recovery,
         'preserveMomentumOnStart': False, 'allowSlideCarry': slide, 'timeline': {'stages': stages}}
    s.update(extra)
    return s

def cap(cid, params, tick=0):
    return {'kind': 'startCapability', 'tick': tick, 'unit': 'ticks', 'capabilityId': cid, 'capabilityVersion': '1',
            'parameters': params}

SLOTS = [
    # Haymaker and Jumping Uppercut: authored slow (72/84 ticks, owner 2026-10-28: "we can speed them up easily unity
    # side"); played at authored length until the owner retimes them. Hit values kept from the moves they replace.
    slot('ground.1', 'Haymaker', 'Cock the fist out wide, step in and throw a big looping hook that carries him off balance.',
         [stage(72, [A + 'g1'], [aim_correction(28), hitbox(31, 8, 4, 8, 4, 20, 14, bone='bone.right-hand', radius=0.5),
                                 hitbox(32, 4, 4, 8, 4, 20, 14, radius=0.55, off=(0, 0.35, 0.6))], iasa=58,
                attack_range=2.25)], slide=True),
    slot('ground.3', 'Jumping Uppercut', 'Sink deep, then spring off the floor driving the fist up over the sombrero: a launcher.',
         [stage(84, [A + 'g3'], [aim_correction(30), hitbox(31, 5, 7, 80, 6, 26, 18, radius=0.6, off=(0, 0.35, 0.6)),
                                 hitbox(33, 10, 7, 80, 6, 26, 18, bone='bone.right-hand', radius=0.6)], iasa=58,
                attack_range=2)]),
    slot('ground.4', 'Tail Spin', 'Spin one full turn with the tail stretched out, sweeping all round him: slow and wide.',
         # clip authored at 48 ticks, played 1.5x slower (owner 2026-10-28: "good, just too fast")
         [stage(72, [A + 'g4'], [aim_correction(18), hitbox(21, 16, 14, 28, 9, 42, 26, radius=1.25, off=(0, 0.5, 0))],
                iasa=54, attack_range=1.6)]),
    slot('air.1', 'Double Tap', 'Two quick finger-gun jabs in the air, right then left.',
         [stage(38, [A + 'a1'], [aim_correction(7),
                                 hitbox(7, 6, 3, 55, 5, 24, 12, shape='capsule', radius=0.45, off=(0.15, 0.2, 0.3), end=(0.1, -0.25, 1.0)),
                                 hitbox(18, 6, 5, 45, 7, 30, 16, shape='capsule', radius=0.45, off=(-0.15, 0.2, 0.3), end=(-0.1, -0.25, 1.0))],
                iasa=33, landing=9)]),
    slot('air.3', 'Tail Whip', 'Arch back and whip the tail up over the head, like a scorpion sting.',
         [stage(44, [A + 'a3'], [aim_correction(12), hitbox(12, 8, 8, 80, 5, 26, 20, bone='bone.head', radius=0.65,
                                                          off=(0, 0.45, 0))], iasa=36, landing=10)]),
    slot('air.4', 'Spur Stomp', 'Knees up, then stamp both spurred boots straight down.',
         [stage(50, [A + 'a4'], [aim_correction(12), hitbox(12, 14, 13, 25, 8, 42, 26, bone='bone.left-foot'),
                                 hitbox(12, 14, 13, 25, 8, 42, 26, bone='bone.right-foot')], iasa=44, landing=12)]),
    slot('ground.A', 'Finger Guns', 'Tap to fire one shot where he faces; the stick turns him first. Six shots, then he '
         'blows the smoke, twirls and holsters: the reload. No refill over time. In the air each shot slows his fall and '
         'pushes him back.',
         [stage(2, [A + 'ga', A + 'ga-reload'], [cap('slop.ability.finger-guns.v1', {
             'fireTriggerTick': 8, 'recoveryTicks': 10, 'shotsPerReload': 6, 'reloadTicks': 90,
             'reloadAnimationId': A + 'ga-reload', 'airMaxFallSpeed': 3, 'airRecoilSpeed': 4,
             'projectile': projectile(launchOffsetY=0.3, launchOffsetZ=0.6, speed=28, radius=0.15, damage=4, angle=15,
                                      baseKnockback=5, knockbackGrowth=12, stunTicks=10, maxFlightTicks=30)})])],
         aim='groundVector'),
    slot('ground.E', 'Sombrero Copter', 'Grab the brim and spin the sombrero like a rotor: it lifts him high. Movement '
         'input trades height for reach, and steers him while he rises. Works with jumps spent.',
         [stage(50, [A + 'ge'], [cap('slop.ability.aimed-recovery.v1', {
             'startupTicks': 16, 'verticalSpeed': 15, 'directionalVerticalSpeed': 13, 'horizontalSpeed': 8,
             'preparationRiseSpeed': 0, 'riseSteerAcceleration': 24})])], cooldown=210, recovery=True),
    slot('ground.R', 'Tumbleweed', 'Tuck into a ball and roll forward like a tumbleweed, pop out and fire one shot '
         'back over the shoulder along the way you came.',
         [stage(64, [A + 'gr'], [aim_correction(8),
                                 {'kind': 'forwardLunge', 'tick': 8, 'unit': 'metersPerSecond', 'speed': 12, 'durationTicks': 32,
                                  'stopInAttackRange': False},
                                 {'kind': 'spawnProjectile', 'tick': 48, 'unit': 'meters', 'projectile': projectile(
                                     launchOffsetY=0.3, speed=26, radius=0.2, damage=6, angle=20, baseKnockback=6,
                                     knockbackGrowth=18, stunTicks=14, maxFlightTicks=30, yawOffsetDegrees=180)}],
                iasa=58)], cooldown=240),
    slot('ground.F', 'Big Iron', 'Hold to charge a two-handed finger gun, release a big slow shot toward the target. The '
         'recoil throws his arms up and half peels his mustache off.',
         [stage(45, [A + 'gf'], [cap('slop.ability.charged-projectile.v1', {
             'maxChargeTicks': 60, 'tier2Ticks': 20, 'tier3Ticks': 45, 'fireTriggerTick': 8, 'tier2Radius': 0.75,
             'tier3Radius': 1.0, 'projectile': projectile(launchOffsetY=1.0, speed=18, radius=0.5, damage=9, angle=30,
                                                          baseKnockback=6, knockbackGrowth=30, stunTicks=20,
                                                          maxFlightTicks=90)})])],
         behavior='chargeAttack', cooldown=300, aimAnimationId=A + 'gf-loop'),
]
CAPABILITIES = ['slop.ability.finger-guns.v1', 'slop.ability.aimed-recovery.v1', 'slop.ability.charged-projectile.v1']

ap = argparse.ArgumentParser()
ap.add_argument('--out', default=str(SRC))
ap.add_argument('--check', action='store_true')
args = ap.parse_args()
c = json.loads(SRC.read_text())
order = [s['id'] for s in c['slots']]
by_id = {s['id']: s for s in SLOTS}
assert set(order) == set(by_id), (order, list(by_id))
c['slots'] = [copy.deepcopy(by_id[i]) for i in order]
c['capabilityRequirements'] = [{'capabilityId': i, 'capabilityVersion': '1'} for i in CAPABILITIES]
for s in c['slots']:
    st = s['timeline']['stages'][0]
    print(f"{s['id']:9} {s['name']:16} dur {st['durationTicks']:3} iasa {st['iasaTicks']:2} clips {st['animationIds']}")
if not args.check:
    Path(args.out).write_text(json.dumps(c, indent=2, ensure_ascii=False) + '\n')
    print('wrote', args.out)
