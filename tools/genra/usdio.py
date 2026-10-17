"""Write a clip into a copy of the rig USD (lossless quaternions, same structure as the rig pack)."""
from __future__ import annotations

import shutil
from pathlib import Path

from pxr import Gf, Usd, UsdSkel, Vt

from .clip import Clip


def write_usd(clip: Clip, out_path) -> Path:
    """Copy the rig's USD next to `out_path` and replace its skeleton animation with `clip`.

    A sibling `textures/` folder is copied too, because the materials use relative paths.
    """
    rig = clip.rig
    out = Path(out_path)
    out.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(rig.path, out)
    textures = rig.path.parent / 'textures'
    if textures.is_dir() and not (out.parent / 'textures').exists():
        shutil.copytree(textures, out.parent / 'textures')
    stage = Usd.Stage.Open(str(out))
    skel = UsdSkel.Skeleton(stage.GetPrimAtPath(rig.skeleton.GetPrim().GetPath()))
    binding = UsdSkel.BindingAPI(skel.GetPrim())
    targets = binding.GetAnimationSourceRel().GetTargets()
    if targets:
        anim = UsdSkel.Animation(stage.GetPrimAtPath(targets[0]))
    else:
        anim = UsdSkel.Animation.Define(stage, skel.GetPrim().GetPath().AppendChild('GeneratedAnimation'))
        binding.CreateAnimationSourceRel().SetTargets([anim.GetPrim().GetPath()])
    anim.GetJointsAttr().Set(Vt.TokenArray(rig.joint_paths))
    for attr in (anim.GetTranslationsAttr(), anim.GetRotationsAttr(), anim.GetScalesAttr()):
        attr.Clear()
    trans, rots, scales = anim.CreateTranslationsAttr(), anim.CreateRotationsAttr(), anim.CreateScalesAttr()
    for tick in range(len(clip)):
        time = Usd.TimeCode(tick)
        trans.Set(Vt.Vec3fArray([Gf.Vec3f(*map(float, v)) for v in clip.t[tick]]), time)
        rots.Set(Vt.QuatfArray([Gf.Quatf(float(q[3]), Gf.Vec3f(float(q[0]), float(q[1]), float(q[2]))) for q in clip.q[tick]]), time)
        scales.Set(Vt.Vec3hArray([Gf.Vec3h(*map(float, v)) for v in clip.s[tick]]), time)
    stage.SetStartTimeCode(0)
    stage.SetEndTimeCode(len(clip) - 1)
    stage.SetTimeCodesPerSecond(rig.fps)
    stage.SetFramesPerSecond(rig.fps)
    stage.GetRootLayer().Save()
    return out
