"""Shared kit for the procedural stage-art generators in tools/blender/<stage>/.

    geo      mesh kit in Unity coordinates (pure Python until to_object)
    checks   blast-corridor clipping and the planned triangle budget (pure Python)
    arena    baked .arena reader and hashes (pure Python)
    blender  objects, Cycles AO bake, FBX export and the geometry digest (needs bpy)
    preview  Eevee camera placement on a gameplay orbit (needs bpy)
    noise    smooth value noise and fbm for textures and terrain (numpy)

A generator puts tools/blender on sys.path and imports `from stagekit import geo`.
Kit changes must keep every generator's geometryDigest unchanged unless the art is
meant to change; see tools/blender/README.md.
"""
