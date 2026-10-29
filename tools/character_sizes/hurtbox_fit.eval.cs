// Run with hurtboxes.py. Direct eval is read-only. Fit writes only hurtboxBoneDefs, then imports that asset.
string Mode = "evaluate";
string[] Targets = Array.Empty<string>();
string repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", ".."));
string output = System.IO.Path.Combine(repo, "tmp", "hurtbox-fit");
System.IO.Directory.CreateDirectory(output);
var catalogs = AssetDatabase.FindAssets("t:CharacterAssetCatalog", new[] { "Assets/CharacterPackages" })
    .Select(g => AssetDatabase.LoadAssetAtPath<SlopArena.Client.Animation.CharacterAssetCatalog>(AssetDatabase.GUIDToAssetPath(g)))
    .Where(c => c != null && c.Rig != null && (Targets.Length == 0 || Targets.Contains(c.PackageId)))
    .OrderBy(c => c.PackageId == "nyx" ? "zzzz" : c.PackageId, StringComparer.Ordinal).ToArray();
var reports = new List<object>();
float Distance(Vector3 p, Vector3 a, Vector3 b)
{
    var d = b - a;
    return (p - a - d * (d.sqrMagnitude < 1e-10f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude))).magnitude;
}
float Percentile(List<float> values, float fraction)
{
    if (values.Count == 0) throw new InvalidOperationException("No mesh vertices fit this segment.");
    values.Sort();
    return values[Mathf.Min(values.Count - 1, (int)(values.Count * fraction))];
}
string Json(object value) => Newtonsoft.Json.JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented);
var sphereDoc = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(repo, "tools/character_sizes/hurtbox_sphere_baseline.json")));
foreach (var catalog in catalogs)
{
    string sourcePath = System.IO.Path.Combine(Application.dataPath, "CharacterPackages", catalog.PackageId, "character.json");
    string sourceText = System.IO.File.ReadAllText(sourcePath);
    var source = SlopArena.Shared.CharacterPackageSourceCodec.Load(
        System.IO.File.ReadAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(sourcePath), "package.json")), sourceText);
    if (!source.IsValid) throw new InvalidOperationException(string.Join("\n", source.Diagnostics));
    var character = source.Source.Character;
    var presentation = character.Presentation;
    var go = UnityEngine.Object.Instantiate(catalog.Rig);
    go.hideFlags = HideFlags.HideAndDontSave;
    var bakedMesh = new Mesh();
    try
    {
        go.transform.SetPositionAndRotation(new Vector3(0f, character.CapsuleHeight * .5f + presentation.ModelYOffset, 0f), Quaternion.identity);
        go.transform.localScale = Vector3.one * presentation.VisualScale;
        var animator = go.GetComponent<Animator>();
        animator.applyRootMotion = false;
        animator.fireEvents = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        var transforms = go.GetComponentsInChildren<Transform>(true);
        var restPositions = transforms.Select(t => t.localPosition).ToArray();
        var restRotations = transforms.Select(t => t.localRotation).ToArray();
        var restScales = transforms.Select(t => t.localScale).ToArray();
        void ResetPose()
        {
            animator.Rebind();
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].localPosition = restPositions[i];
                transforms[i].localRotation = restRotations[i];
                transforms[i].localScale = restScales[i];
            }
        }
        var mapped = new Dictionary<Transform, string>();
        foreach (var point in SlopArena.Shared.CharacterBoneRegistry.HumanoidPoints)
        {
            var t = animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), point.HumanoidBone));
            if (t != null) mapped[t] = point.Id;
        }
        Transform Bone(string id)
        {
            var point = SlopArena.Shared.CharacterBoneRegistry.Find(id);
            if (point != null) return animator.GetBoneTransform((HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), point.HumanoidBone));
            if (!SlopArena.Shared.CharacterBoneRegistry.IsRigId(id)) throw new InvalidOperationException("Unknown bone " + id);
            return transforms.Single(t => t.name == id.Substring(SlopArena.Shared.CharacterBoneRegistry.RigPrefix.Length));
        }
        string Region(Transform bone)
        {
            if (bone == null) return "other";
            string name = bone.name.ToLowerInvariant();
            if (name.Contains("skirt") || name.Contains("cape") || name.Contains("coat")) return "cloth";
            if (name.Contains("hat")) return "hat";
            if (name.Contains("tail")) return "tail";
            if (name == "shell" || name.StartsWith("shell_")) return "shell";
            // Manki's humanoid controls and weighted stretch/deform bones have separate names.
            if (name.Contains("upperarmstretch")) return "upperarm";
            if (name.Contains("forearmoffset")) return "forearm";
            if (name.Contains("fistdeform")) return "hand";
            for (var t = bone; t != null; t = t.parent)
            {
                if (!mapped.TryGetValue(t, out string id)) continue;
                if (id.Contains("hand")) return "hand";
                if (id.Contains("upper-arm")) return "upperarm";
                if (id.Contains("lower-arm")) return "forearm";
                if (id.Contains("upper-leg")) return "thigh";
                if (id.Contains("lower-leg")) return "shin";
                if (id.Contains("foot") || id.Contains("toes")) return "foot";
                if (id == "bone.head" || id == "bone.neck") return "head";
                return "torso";
            }
            return "other";
        }
        var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(s => s.enabled && s.sharedMesh != null).ToArray();
        var regions = new List<string[]>();
        var areas = new List<float[]>();
        var weightedBones = new HashSet<Transform>();
        foreach (var skin in skins)
        {
            var mesh = skin.sharedMesh;
            var weights = mesh.boneWeights;
            var verts = mesh.vertices;
            var triangle = mesh.triangles;
            var reg = new string[verts.Length];
            var area = new float[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                int index = -1;
                if (i < weights.Length)
                {
                    var w = weights[i]; index = w.boneIndex0; float maximum = w.weight0;
                    if (w.weight1 > maximum) { maximum = w.weight1; index = w.boneIndex1; }
                    if (w.weight2 > maximum) { maximum = w.weight2; index = w.boneIndex2; }
                    if (w.weight3 > maximum) index = w.boneIndex3;
                }
                var bone = index >= 0 && index < skin.bones.Length ? skin.bones[index] : skin.rootBone;
                reg[i] = Region(bone);
                if (bone != null) weightedBones.Add(bone);
            }
            for (int i = 0; i < triangle.Length; i += 3)
            {
                float a = Vector3.Cross(verts[triangle[i + 1]] - verts[triangle[i]], verts[triangle[i + 2]] - verts[triangle[i]]).magnitude / 6f;
                area[triangle[i]] += a; area[triangle[i + 1]] += a; area[triangle[i + 2]] += a;
            }
            regions.Add(reg); areas.Add(area);
        }
        var bindings = catalog.Bindings.Where(b => b != null && b.Clip != null).ToList();
        var idle = bindings.First(b => b.SemanticId == presentation.Idle);
        bindings.Remove(idle); bindings.Insert(0, idle);
        var defs = new List<SlopArena.Shared.HurtboxBoneSource>();
        var segmentRegions = new List<string>();
        void Segment(string start, string end, string region)
        {
            if (Bone(start) == null) throw new InvalidOperationException("Missing humanoid mapping " + start);
            if (Bone(end) == null) end = start;
            defs.Add(new SlopArena.Shared.HurtboxBoneSource(start, 0, 0, 0, .05f, end));
            segmentRegions.Add(region);
        }
        Segment("bone.head", "bone.head", "head");
        Segment("bone.hips", "bone.neck", "torso");
        foreach (string side in new[] { "right", "left" })
        {
            Segment("bone." + side + "-upper-arm", "bone." + side + "-lower-arm", "upperarm");
            Segment("bone." + side + "-lower-arm", "bone." + side + "-hand", "forearm");
            Segment("bone." + side + "-hand", "bone." + side + "-hand-end", "hand");
            Segment("bone." + side + "-upper-leg", "bone." + side + "-lower-leg", "thigh");
            Segment("bone." + side + "-lower-leg", "bone." + side + "-foot", "shin");
            Segment("bone." + side + "-foot", "bone." + side + "-toes", "foot");
        }
        foreach (var tail in transforms.Where(t => t.name.StartsWith("Tail", StringComparison.OrdinalIgnoreCase)))
        {
            var child = tail.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("Tail", StringComparison.OrdinalIgnoreCase));
            if (child != null) Segment("rig:" + tail.name, "rig:" + child.name, "tail");
        }
        var total = new Dictionary<string, double>();
        var before = new Dictionary<string, double>();
        var current = new Dictionary<string, double>();
        int sampledFrames = 0;
        long surfaceSamples = 0;
        var maxPoseError = 0f;
        string poseErrorWitness = "";
        SlopArena.Shared.CookedCharacterPackageLoadResult loaded = null;
        if (Mode != "fit")
        {
            string dir = System.IO.Path.Combine(repo, "content-cooked", catalog.PackageId);
            var manifest = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "manifest.json")));
            loaded = SlopArena.Shared.CookedCharacterPackageLoader.LoadDirectory(dir, new SlopArena.Shared.MatchContentPackageRequirement(
                catalog.PackageId, manifest["version"].ToObject<string>(), manifest["cookedContentHash"].ToObject<string>(), manifest["packageHash"].ToObject<string>()));
            if (!loaded.IsValid) throw new InvalidOperationException(string.Join("\n", loaded.Diagnostics));
        }
        if (Mode == "visual")
        {
            var utility = new PreviewRenderUtility(true);
            Material wires = null;
            var paths = new List<object>();
            string visualDir = System.IO.Path.Combine(output, "visual");
            System.IO.Directory.CreateDirectory(visualDir);
            try
            {
                utility.AddSingleGO(go);
                var camera = utility.camera;
                camera.clearFlags = CameraClearFlags.Color;
                camera.backgroundColor = new Color(.13f, .15f, .19f, 1f);
                camera.nearClipPlane = .01f; camera.farClipPlane = 100f; camera.orthographic = true;
                camera.aspect = 640f / 480f;
                utility.lights[0].type = LightType.Directional; utility.lights[0].intensity = 1.25f;
                utility.lights[0].transform.rotation = Quaternion.Euler(35f, 160f, 0f);
                utility.lights[1].type = LightType.Directional; utility.lights[1].intensity = .6f;
                utility.lights[1].transform.rotation = Quaternion.Euler(20f, -30f, 0f);
                utility.ambientColor = new Color(.45f, .45f, .5f);
                wires = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                wires.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                wires.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                wires.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                wires.SetInt("_ZWrite", 0); wires.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                var attack = loaded.Package.Definition.Slots.First(s => s.Id == "ground.1").Timeline.Stages
                    .SelectMany(s => s.AnimationIds).First();
                var poses = new[] { (name: "idle", id: presentation.Idle, fraction: 0f),
                    (name: "attack", id: attack, fraction: .45f), (name: "hit", id: presentation.HitHard, fraction: .45f) };
                foreach (var pose in poses)
                {
                    ResetPose();
                    var binding = bindings.First(b => b.SemanticId == pose.id);
                    int frame = Mathf.Min(Mathf.CeilToInt(binding.Clip.length * 60f) - 1, Mathf.FloorToInt(binding.Clip.length * 60f * pose.fraction));
                    var graph = UnityEngine.Playables.PlayableGraph.Create("hurtbox-visual");
                    var frozen = new List<(Mesh mesh, Matrix4x4 matrix, Material[] materials)>();
                    try
                    {
                        graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
                        var playableOutput = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "pose", animator);
                        var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, binding.Clip);
                        playable.SetApplyFootIK(false);
                        UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(playableOutput, playable);
                        graph.Play(); UnityEngine.Playables.PlayableExtensions.SetTime(playable, frame / 60d); graph.Evaluate(0f);
                        var bounds = new Bounds(go.transform.position, Vector3.zero);
                        foreach (var skin in skins)
                        {
                            var snapshot = new Mesh();
                            skin.BakeMesh(snapshot, true);
                            var matrix = skin.transform.localToWorldMatrix;
                            frozen.Add((snapshot, matrix, skin.sharedMaterials));
                            skin.enabled = false;
                            foreach (var vertex in snapshot.vertices) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertex));
                        }
                        var state = new SlopArena.Shared.CharacterState { PY = character.CapsuleHeight * .5f };
                        var shapes = SlopArena.Shared.ServerSimulation.BuildEntitiesFromState(state, loaded.ToCharacterDefinition(),
                            loaded.BakedAnimation, binding.SemanticId, frame, 1);
                        foreach (var shape in shapes)
                        {
                            foreach (var point in new[] { new Vector3(shape.PosX, shape.PosY, shape.PosZ), new Vector3(shape.EndX, shape.EndY, shape.EndZ) })
                            {
                                bounds.Encapsulate(point + Vector3.one * shape.Radius);
                                bounds.Encapsulate(point - Vector3.one * shape.Radius);
                            }
                        }
                        foreach (string view in new[] { "front", "side" })
                        {
                            const int width = 640, height = 480;
                            Vector3 direction = view == "front" ? Vector3.forward : Vector3.right;
                            camera.orthographicSize = Mathf.Max(bounds.extents.y, (view == "front" ? bounds.extents.x : bounds.extents.z) / (width / (float)height)) * 1.28f;
                            camera.transform.position = bounds.center + direction * 10f;
                            camera.transform.LookAt(bounds.center);
                            utility.BeginPreview(new Rect(0, 0, width, height), GUIStyle.none);
                            // Preview cameras can retain a previous GPU skin pose; draw this exact CPU snapshot.
                            foreach (var snapshot in frozen)
                                for (int submesh = 0; submesh < snapshot.mesh.subMeshCount; submesh++)
                                    utility.DrawMesh(snapshot.mesh, snapshot.matrix, snapshot.materials[Mathf.Min(submesh, snapshot.materials.Length - 1)], submesh);
                            camera.Render();
                            var texture = (RenderTexture)utility.EndPreview();
                            var previous = RenderTexture.active;
                            var png = new Texture2D(width, height, TextureFormat.RGBA32, false);
                            try
                            {
                                RenderTexture.active = texture;
                                GL.PushMatrix();
                                try
                                {
                                    GL.LoadPixelMatrix(0, width, 0, height);
                                    wires.SetPass(0); GL.Begin(GL.LINES); GL.Color(new Color(.1f, 1f, .55f, .88f));
                                    void Line(Vector3 a, Vector3 b)
                                    {
                                        var sa = camera.WorldToScreenPoint(a); var sb = camera.WorldToScreenPoint(b);
                                        GL.Vertex3(sa.x, sa.y, 0); GL.Vertex3(sb.x, sb.y, 0);
                                    }
                                    void Sphere(Vector3 center, float radius)
                                    {
                                        for (int plane = 0; plane < 3; plane++)
                                            for (int step = 0; step < 32; step++)
                                            {
                                                Vector3 At(int n)
                                                {
                                                    float a = n * Mathf.PI * 2f / 32;
                                                    float x = Mathf.Cos(a) * radius, y = Mathf.Sin(a) * radius;
                                                    return center + (plane == 0 ? new Vector3(x, y, 0) : plane == 1 ? new Vector3(x, 0, y) : new Vector3(0, x, y));
                                                }
                                                Line(At(step), At(step + 1));
                                            }
                                    }
                                    foreach (var shape in shapes)
                                    {
                                        var a = new Vector3(shape.PosX, shape.PosY, shape.PosZ);
                                        var b = new Vector3(shape.EndX, shape.EndY, shape.EndZ);
                                        Sphere(a, shape.Radius);
                                        if ((b - a).sqrMagnitude < .000001f) continue;
                                        Sphere(b, shape.Radius);
                                        var axis = (b - a).normalized;
                                        var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.right).normalized * shape.Radius;
                                        var v = Vector3.Cross(axis, u).normalized * shape.Radius;
                                        foreach (var offset in new[] { u, -u, v, -v }) Line(a + offset, b + offset);
                                    }
                                    GL.End();
                                }
                                finally { GL.PopMatrix(); }
                                png.ReadPixels(new Rect(0, 0, width, height), 0, 0); png.Apply();
                                string path = System.IO.Path.Combine(visualDir, catalog.PackageId + "-" + pose.name + "-" + view + ".png");
                                System.IO.File.WriteAllBytes(path, png.EncodeToPNG());
                                paths.Add(new { pose = pose.name, semanticId = pose.id, frame, view, path, capsules = shapes.Count });
                            }
                            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(png); }
                        }
                    }
                    finally
                    {
                        foreach (var skin in skins) skin.enabled = true;
                        foreach (var snapshot in frozen) UnityEngine.Object.DestroyImmediate(snapshot.mesh);
                        graph.Destroy();
                    }
                }
            }
            finally { if (wires != null) UnityEngine.Object.DestroyImmediate(wires); utility.Cleanup(); }
            var reportVisual = new { package = catalog.PackageId, paths };
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "visual-" + catalog.PackageId + ".json"), Json(reportVisual));
            reports.Add(reportVisual);
            continue;
        }
        var baseline = sphereDoc[catalog.PackageId];
        float oldScale = baseline["hurtboxBoneScale"].ToObject<float>();
        var oldBones = ((Newtonsoft.Json.Linq.JArray)baseline["hurtboxBoneDefs"])
            .Select(x => (id: x["boneId"].ToObject<string>(), radius: x["radius"].ToObject<float>())).ToArray();
        var worst = new List<(string clip, double coverage)>();
        foreach (var binding in bindings)
        {
            ResetPose();
            var graph = UnityEngine.Playables.PlayableGraph.Create("hurtbox-fit");
            try
            {
                graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
                var outputPlayable = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "pose", animator);
                var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, binding.Clip);
                playable.SetApplyFootIK(false);
                UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(outputPlayable, playable);
                graph.Play();
                double clipTotal = 0, clipCovered = 0;
                int frames = Mode == "fit" ? 1 : Mathf.Max(1, Mathf.CeilToInt(binding.Clip.length * 60f));
                for (int frame = 0; frame < frames; frame += 4)
                {
                    UnityEngine.Playables.PlayableExtensions.SetTime(playable, frame / 60d); graph.Evaluate(0f);
                    var points = new List<(Vector3 point, string region, float area)>();
                    for (int s = 0; s < skins.Length; s++)
                    {
                        skins[s].BakeMesh(bakedMesh, true);
                        var matrix = skins[s].transform.localToWorldMatrix;
                        var vertices = bakedMesh.vertices;
                        for (int i = 0; i < vertices.Length; i++) points.Add((matrix.MultiplyPoint3x4(vertices[i]), regions[s][i], areas[s][i]));
                    }
                    sampledFrames++; surfaceSamples += points.Count;
                    if (Mode == "fit")
                    {
                        var head = Bone("bone.head").position;
                        float height = Percentile(points.Where(p => p.region == "head").Select(p => Mathf.Max(0f, p.point.y - head.y)).ToList(), .95f);
                        defs[0] = defs[0] with { EndOffsetY = Mathf.Round(height * .5f * 10000f) / 10000f };
                        var starts = defs.Select(d => Bone(d.BoneId).position).ToArray();
                        var ends = defs.Select(d => Bone(d.EndBoneId).position + new Vector3(d.EndOffsetX, d.EndOffsetY, d.EndOffsetZ)).ToArray();
                        for (int k = 0; k < defs.Count; k++)
                        {
                            var distances = new List<float>();
                            foreach (var point in points)
                            {
                                if (point.region != segmentRegions[k] && !(point.region == "shell" && segmentRegions[k] == "torso")) continue;
                                // Tufts past a tail's last bone would size the whole tip segment.
                                if (segmentRegions[k] == "tail")
                                {
                                    var axis = ends[k] - starts[k];
                                    float t = Vector3.Dot(point.point - starts[k], axis) / Mathf.Max(axis.sqrMagnitude, 1e-8f);
                                    if (t < 0f || t > 1f) continue;
                                }
                                float mine = Distance(point.point, starts[k], ends[k]), best = mine;
                                for (int j = 0; j < defs.Count; j++) if (segmentRegions[j] == segmentRegions[k]) best = Mathf.Min(best, Distance(point.point, starts[j], ends[j]));
                                if (mine <= best + .00001f) distances.Add(mine);
                            }
                            if (distances.Count == 0) throw new InvalidOperationException($"{catalog.PackageId} segment {defs[k].BoneId} has no weighted surface.");
                            float radius = Mathf.Max(.015f, Percentile(distances, .85f));
                            defs[k] = defs[k] with { Radius = Mathf.Round(radius * 10000f) / 10000f };
                        }
                        var shellPoints = points.Where(p => p.region == "shell").ToArray();
                        if (shellPoints.Length > 0)
                        {
                            int torso = 1;
                            if (shellPoints.Any(p => Distance(p.point, starts[torso], ends[torso]) > defs[torso].Radius + .015f))
                            {
                                var shell = transforms.Single(t => t.name == "Shell");
                                float radius = Percentile(shellPoints.Select(p => Vector3.Distance(p.point, shell.position)).ToList(), .95f);
                                defs.Add(new SlopArena.Shared.HurtboxBoneSource("rig:" + shell.name, 0, 0, 0, Mathf.Round(radius * 10000f) / 10000f, "rig:" + shell.name));
                                segmentRegions.Add("shell");
                            }
                        }
                        break;
                    }
                    var runtime = loaded.ToCharacterDefinition();
                    var state = new SlopArena.Shared.CharacterState { PY = character.CapsuleHeight * .5f };
                    var shapes = SlopArena.Shared.ServerSimulation.BuildEntitiesFromState(state, runtime, loaded.BakedAnimation, binding.SemanticId, frame, 1);
                    if (shapes.Count != runtime.HurtboxBoneDefs.Count) throw new InvalidOperationException("Missing runtime shapes for " + binding.SemanticId);
                    var hips = Bone("bone.hips").position;
                    var spheres = oldBones.Select(x => (point: hips + (Bone(x.id).position - hips) * oldScale, x.radius)).ToArray();
                    foreach (string id in character.HurtboxBoneDefs.SelectMany(d => new[] { d.BoneId, d.EndBoneId })
                        .Where(id => id != null).Distinct(StringComparer.Ordinal))
                    {
                        int i = loaded.BakedAnimation.FindAnimIndex(binding.SemanticId);
                        int b = loaded.BakedAnimation.FindBoneIndex(SlopArena.Shared.CharacterBoneRegistry.BakedName(id));
                        if (!SlopArena.Shared.HitboxGeometry.BakedPointToWorld(state, runtime, loaded.BakedAnimation, i, frame, b, 1f, out float x, out float y, out float z))
                            throw new InvalidOperationException("Missing evaluated pose point " + id + " at " + binding.SemanticId);
                        float error = Vector3.Distance(Bone(id).position, new Vector3(x, y, z));
                        if (error > maxPoseError) { maxPoseError = error; poseErrorWitness = id + " " + binding.SemanticId + " frame " + frame; }
                    }
                    foreach (var p in points)
                    {
                        if (!total.ContainsKey(p.region)) { total[p.region] = 0; before[p.region] = 0; current[p.region] = 0; }
                        total[p.region] += p.area;
                        bool hitOld = false, hitCurrent = false;
                        foreach (var sphere in spheres) if ((p.point - sphere.point).sqrMagnitude <= sphere.radius * sphere.radius) { hitOld = true; break; }
                        foreach (var shape in shapes)
                            if (Distance(p.point, new Vector3(shape.PosX, shape.PosY, shape.PosZ), new Vector3(shape.EndX, shape.EndY, shape.EndZ)) <= shape.Radius) { hitCurrent = true; break; }
                        if (hitOld) before[p.region] += p.area;
                        if (hitCurrent) current[p.region] += p.area;
                        if (p.region != "cloth" && p.region != "hat") { clipTotal += p.area; if (hitCurrent) clipCovered += p.area; }
                    }
                }
                if (Mode == "fit") break;
                worst.Add((binding.SemanticId, clipCovered / clipTotal));
            }
            finally { graph.Destroy(); }
        }
        object report;
        if (Mode == "fit")
        {
            string latest = System.IO.File.ReadAllText(sourcePath);
            var latestSource = SlopArena.Shared.CharacterPackageSourceCodec.Load(
                System.IO.File.ReadAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(sourcePath), "package.json")), latest).Source.Character;
            if (latestSource.Presentation != character.Presentation || latestSource.CapsuleHeight != character.CapsuleHeight)
                throw new InvalidOperationException("Model placement changed during fitting; rerun against current source.");
            var jsonDefs = defs.Select(d => new { boneId = d.BoneId, offsetX = d.OffsetX, offsetY = d.OffsetY, offsetZ = d.OffsetZ,
                radius = d.Radius, endBoneId = d.EndBoneId, endOffsetX = d.EndOffsetX, endOffsetY = d.EndOffsetY, endOffsetZ = d.EndOffsetZ }).ToArray();
            var pattern = new System.Text.RegularExpressions.Regex("(\"hurtboxBoneDefs\"\\s*:\\s*)\\[[^\\]]*\\]", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (pattern.Matches(latest).Count != 1) throw new InvalidOperationException("Expected one hurtboxBoneDefs array.");
            string replacement = Json(jsonDefs).Replace("\n", "\n  ");
            string rewritten = pattern.Replace(latest, m => m.Groups[1].Value + replacement);
            bool changed = latest != rewritten;
            if (changed) System.IO.File.WriteAllText(sourcePath, rewritten);
            AssetDatabase.ImportAsset("Assets/CharacterPackages/" + catalog.PackageId + "/character.json", ImportAssetOptions.ForceUpdate);
            report = new { package = catalog.PackageId, changed, count = defs.Count, defs = jsonDefs,
                weightedBones = weightedBones.Select(b => new { b.name, region = Region(b) }).OrderBy(b => b.name).ToArray() };
        }
        else
        {
            if (maxPoseError > .0001f) throw new InvalidOperationException($"{catalog.PackageId} cooked/live pose mismatch {maxPoseError * 1000f:F3} mm at {poseErrorWitness}.");
            string[] hittable = total.Keys.Where(r => r != "cloth" && r != "hat").ToArray();
            double allArea = total.Values.Sum(), bodyArea = hittable.Sum(r => total[r]);
            report = new { package = catalog.PackageId, mode = Mode, sampleStep = 4, clips = bindings.Count, sampledFrames, surfaceSamples,
                beforeAll = Math.Round(100 * before.Values.Sum() / allArea, 1), currentAll = Math.Round(100 * current.Values.Sum() / allArea, 1),
                beforeBody = Math.Round(100 * hittable.Sum(r => before[r]) / bodyArea, 1), currentBody = Math.Round(100 * hittable.Sum(r => current[r]) / bodyArea, 1),
                maxPoseErrorMm = maxPoseError * 1000f, poseErrorWitness,
                regions = total.Keys.OrderBy(r => r).Select(r => new { region = r, areaPercent = Math.Round(100 * total[r] / allArea, 1),
                    before = Math.Round(100 * before[r] / total[r], 1), current = Math.Round(100 * current[r] / total[r], 1), hittable = r != "cloth" && r != "hat" }).ToArray(),
                worstClips = worst.OrderBy(c => c.coverage).Take(5).Select(c => new { c.clip, coverage = Math.Round(100 * c.coverage, 1) }).ToArray() };
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, Mode + "-" + catalog.PackageId + ".json"), Json(report));
        reports.Add(report);
    }
    finally { UnityEngine.Object.DestroyImmediate(bakedMesh); UnityEngine.Object.DestroyImmediate(go); }
}
return reports;
