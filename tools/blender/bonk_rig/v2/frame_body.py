"""Square the kit body to face -Y (it is yawed 25.1 deg), hips centre at x=y=0, soles at z=0. Parts untouched.
blender -b <parts.blend> --python frame_body.py -- <out.blend>"""
import bpy, sys, math, json, os
import numpy as np
from mathutils import Matrix, Vector
out = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
o = bpy.data.objects["body"]
B = np.array([v.co[:] for v in o.data.vertices])
c = B[(B[:, 2] >= 0.44) & (B[:, 2] < 0.47)][:, :2].mean(0)
M = Matrix.Translation((0, 0, -B[:, 2].min())) @ Matrix.Rotation(math.radians(-25.1), 4, "Z") @ Matrix.Translation((-c[0], -c[1], 0))
o.data.transform(M); o.data.update()
json.dump({"yaw_deg": -25.1, "hips_xy": c.tolist(), "z_shift": float(-B[:, 2].min())}, open(os.path.splitext(out)[0] + ".json", "w"))
bpy.ops.wm.save_as_mainfile(filepath=out)
