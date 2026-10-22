#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using SlopArena.Client.Entities;
using SlopArena.Client.World;
using SlopArena.Shared;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using static SlopArena.EditorTools.PresentationFx;
using static SlopArena.EditorTools.ToonShapes;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Rebuilds Bashie's package-owned presentation from code (docs/characters/bashie.md): the pool lifeguard who
    /// enforces every rule and breaks them all herself. Hose Down (A) is cartoon pool water in toon bands (a deep
    /// pool-blue rim, blue, light cyan, white foam): a hose spray of streaks fanning out of her toy blaster, drips
    /// while she pumps, a splash where it lands, and the NO SPLASHING! pool sign she is breaking. The owner picked
    /// the spray look ("C") from painted concepts on 2026-10-22. Shell Slide (R) folds her into her own shell with
    /// <see cref="ShellTuck"/> (look "A", picked from renders of her rig the same day). Workflow and readability rules are in
    /// skills/sloparena-character-vfx-pass; plumbing is in <see cref="PresentationBuilderKit"/>.
    /// </summary>
    public static class BashiePresentationBuilder
    {
        private const string WeaponConfigPath = "Assets/CharacterPackages/bashie/Presentation/BashieWeaponAttachments.asset";
        private const string SignMaskPath = "Assets/CharacterPackages/bashie/Presentation/Source/NoSplashing.png";
        // character.json presentation.visualScale: her bones carry it in game, and the blaster's offset with them.
        private const float VisualScale = 1.1f;
        // The stream's projectile (character.json ground.A): its speed and gravity, which the streaks keep pace with.
        private const float StreamSpeed = 16f, StreamGravity = 4f;

        private static readonly Color Rim = new(0.055f, 0.227f, 0.463f);
        private static readonly Color Blue = new(0.157f, 0.584f, 0.902f);
        private static readonly Color Cyan = new(0.525f, 0.863f, 1f);
        private static readonly Color Foam = new(0.957f, 0.992f, 1f);
        private static readonly Color SignBlue = new(0.122f, 0.373f, 0.749f);
        private static readonly Color SignRed = new(0.898f, 0.2f, 0.165f);
        private static readonly Color Ink = new(0.094f, 0.118f, 0.18f);

        private static PresentationBuilderKit _kit;
        private static Materials _m;

        private sealed class Materials
        {
            public Material Streak, Drop, Mist, Crown, Puddle, Sign, Wave;
            public Mesh WaveMesh;
        }

        [CliCommand("sloparena.presentation.bashie.build", "Rebuild Bashie's package presentation prefabs, textures, materials and bindings in Edit Mode. Saving the catalog triggers the usual package cook.", MainThreadRequired = true, Tags = new[] { "presentation", "character" })]
        public static object Build()
        {
            using var kit = new PresentationBuilderKit("bashie", audioFolder: "Bashie");
            _kit = kit;
            _m = BuildMaterials();
            var (muzzle, barrel) = MuzzleInHand();
            var built = new List<(string id, GameObject prefab)>
            {
                ("presentation.bashie.hose.pump", Pump(muzzle, barrel)),
                ("presentation.bashie.hose.release", Release(muzzle, barrel)),
                ("presentation.bashie.hose.sign", Sign()),
                ("presentation.bashie.hose.hit", Hit()),
                ("presentation.bashie.shell.tuck", Tuck()),
                ("presentation.bashie.shell.plop", Plop()),
                ("presentation.bashie.shell.surf", Surf()),
                ("presentation.bashie.shell.hit", ShellHit()),
            };
            var drop = StreamDrop();
            kit.BindProjectile(CharacterClass.Bashie, AbilitySlots.A, drop);
            return kit.Finish(built, new { drop = AssetDatabase.GetAssetPath(drop), muzzle = muzzle.ToString("F3") });
        }

        [MenuItem("Tools/SLOPArena/Presentation/Rebuild Bashie Presentation")]
        private static void Menu() => Debug.Log("[BashiePresentationBuilder] " + Newtonsoft.Json.JsonConvert.SerializeObject(Build()));

        /// <summary>
        /// The blaster's muzzle in the right hand's frame, in metres under a unit-scale frame, and the barrel's
        /// rotation: <see cref="WeaponAttach"/> places the blaster at the hand's scaled offset at world scale 1.
        /// </summary>
        private static (Vector3 position, Quaternion rotation) MuzzleInHand()
        {
            var config = AssetDatabase.LoadAssetAtPath<WeaponAttachConfig>(WeaponConfigPath)
                ?? throw new System.InvalidOperationException("Missing " + WeaponConfigPath + " (blaster_import.eval.cs).");
            var entry = config.Entries.Single(e => e.AttackSlot == AbilitySlots.A);
            var muzzle = entry.Prefab.transform.Find("Muzzle")
                ?? throw new System.InvalidOperationException("The blaster prefab has no Muzzle anchor.");
            var rotation = Quaternion.Euler(entry.RotationOffset);
            return (entry.PositionOffset * VisualScale + rotation * muzzle.localPosition, rotation);
        }

        // ── Shell Slide ──────────────────────────────────────────────────────────

        /// <summary>
        /// Folds her into her shell for the dive and the slide (<see cref="ShellTuck"/>), the look the owner picked
        /// ("A", 2026-10-22): her hat and face peek out of the front. It pops her back out when the slide shows its
        /// hit, miss or look-down clip, or when the attack ends.
        /// </summary>
        private static GameObject Tuck()
        {
            var root = _kit.NewRoot("BashieShellTuck", attackBound: true);
            var tuck = root.AddComponent<ShellTuck>();
            tuck.Pose = ShellTuckPose.Bashie;
            tuck.HoldAnimationIds = new[] { "anim.bashie.gr", "anim.bashie.gr-slide" };
            return _kit.Save(root);
        }

        // Her hips while tucked sit this high (ShellTuckPose.Bashie's 0.141 at her visual scale): the surf's floor.
        private const float TuckedHipsHeight = 0.141f * VisualScale;
        private static readonly string[] SlideClips = { "anim.bashie.gr-slide" };

        /// <summary>The dive into the shell: a ring of splash round her where she lands on her belly, and the fwump.</summary>
        private static GameObject Plop()
        {
            var root = _kit.NewRoot("BashieShellPlop", attackBound: false);
            var floor = new GameObject("Floor");
            floor.transform.SetParent(root.transform, false);
            floor.AddComponent<SnapToFloor>();
            var crown = Particles(floor, "Crown", _m.Crown, 0.4f, ParticleSystemSimulationSpace.Local);
            Burst(crown, 1);
            var main = crown.main;
            main.startLifetime = 0.28f;
            main.startSize = 1.5f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            crown.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            SizeOverLife(crown, Curve((0f, 0.4f), (0.35f, 1f), (1f, 1.15f)));
            Fade(crown, 0.4f);
            var drops = Particles(floor, "Drops", _m.Drop, 0.4f);
            Burst(drops, 12);
            main = drops.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
            main.gravityModifier = 1.4f;
            var shape = drops.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55f;
            shape.radius = 0.35f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            Fade(drops, 0.6f);
            _kit.Sound(root, "bashie_shell_tuck", 0.45f, 1f);
            return _kit.Save(root);
        }

        /// <summary>
        /// The surf she rides while she slides (the owner's pick, "C", 2026-10-22): a little toon wave curling just
        /// ahead of her, low in front of her face and higher at the sides, foam on its crest, spray thrown back off
        /// its sides, a wet streak on the floor behind and the rush of water. It shows only while she slides on the
        /// ground (<see cref="AttackClipShow"/>).
        /// </summary>
        private static GameObject Surf()
        {
            var root = _kit.NewRoot("BashieShellSurf", attackBound: true);
            var frame = new GameObject("Frame");
            frame.transform.SetParent(root.transform, false);
            var pf = frame.AddComponent<PresentationFrame>();
            pf.UnitScale = true;
            pf.Upright = true;
            var floor = new GameObject("Floor");                       // the floor under her pinned hips
            floor.transform.SetParent(frame.transform, false);
            floor.transform.localPosition = new Vector3(0f, -TuckedHipsHeight, 0f);
            var grow = new GameObject("Grow");
            grow.transform.SetParent(floor.transform, false);
            var wave = new GameObject("Wave");
            wave.transform.SetParent(grow.transform, false);
            wave.AddComponent<MeshFilter>().sharedMesh = _m.WaveMesh;
            var renderer = wave.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _m.Wave;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var foam = Particles(grow, "Foam", _m.Mist, 1f, ParticleSystemSimulationSpace.World);
            var emission = foam.emission;
            emission.rateOverTime = 90f;
            var main = foam.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            main.startColor = new Color(1f, 1f, 1f, 0.9f);
            var shape = foam.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Mesh;
            shape.meshShapeType = ParticleSystemMeshShapeType.Vertex;
            shape.mesh = _m.WaveMesh;
            SizeOverLife(foam, Curve((0f, 0.6f), (1f, 1.3f)));
            Fade(foam, 0.4f);
            InFront(foam);

            foreach (float side in new[] { -1f, 1f })
            {
                var spray = Particles(grow, side < 0f ? "SprayL" : "SprayR", _m.Streak, 1f, ParticleSystemSimulationSpace.World);
                spray.transform.localPosition = new Vector3(side * 0.42f, 0.16f, 0.2f);
                spray.transform.localRotation = Quaternion.LookRotation(new Vector3(side * 0.55f, 0.9f, -0.75f));
                emission = spray.emission;
                emission.rateOverTime = 45f;
                main = spray.main;
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.06f);
                main.gravityModifier = 1.2f;
                shape = spray.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 18f;
                shape.radius = 0.05f;
                Stretch(spray, 0.05f, 1.2f);
                Fade(spray, 0.6f);
            }

            var streak = Particles(floor, "Streak", _m.Puddle, 1f, ParticleSystemSimulationSpace.World);
            streak.transform.localPosition = new Vector3(0f, 0.02f, -0.15f);
            emission = streak.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 12f;
            main = streak.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 0.8f);
            main.startColor = new Color(1f, 1f, 1f, 0.55f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            streak.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            SizeOverLife(streak, Curve((0f, 1f), (1f, 0.6f)));
            Fade(streak, 0.3f);

            var rush = _kit.Sound(root, "bashie_shell_slide", 0.32f, 1f);
            if (rush != null) { rush.loop = true; rush.playOnAwake = false; }
            var show = root.AddComponent<AttackClipShow>();
            show.AnimationIds = SlideClips;
            show.GroundedOnly = true;
            show.Grow = grow.transform;
            return _kit.Save(root);
        }

        /// <summary>The shell bowling someone over: a splash of the surf against them and a hollow knock.</summary>
        private static GameObject ShellHit()
        {
            var root = _kit.NewRoot("BashieShellHit", attackBound: false);
            var crown = Particles(root, "Crown", _m.Crown, 0.3f, ParticleSystemSimulationSpace.Local);
            Burst(crown, 1);
            var main = crown.main;
            main.startLifetime = 0.2f;
            main.startSize = 1.2f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(crown, Curve((0f, 0.5f), (0.4f, 1f), (1f, 1.1f)));
            Fade(crown, 0.4f);
            InFront(crown);
            var drops = Particles(root, "Drops", _m.Drop, 0.3f);
            Burst(drops, 14);
            main = drops.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.14f);
            main.gravityModifier = 1.4f;
            Sphere(drops, 0.2f);
            Fade(drops, 0.6f);
            InFront(drops);
            var mist = Particles(root, "Mist", _m.Mist, 0.3f);
            Burst(mist, 2);
            main = mist.main;
            main.startLifetime = 0.35f;
            main.startSize = 0.9f;
            SizeOverLife(mist, Curve((0f, 0.6f), (1f, 1.4f)));
            Fade(mist, 0.3f);
            _kit.Sound(root, "bashie_shell_hit", 0.45f, 1f);
            return _kit.Save(root);
        }

        /// <summary>
        /// The surf's wave, in metres on her tucked floor (+Z forward, the hips above the origin): a sheet whose
        /// cross-section rises from the floor under her shell to a crest that curls forward. Low in front of her face
        /// and higher at the sides, it sweeps back round her like a bow wave. V runs base (0) to crest (1).
        /// </summary>
        private static Mesh WaveMesh()
        {
            (float z, float y)[] profile = { (0.30f, 0f), (0.42f, 0.35f), (0.53f, 0.75f), (0.63f, 1f), (0.71f, 0.93f), (0.75f, 0.72f) };
            const int across = 16;
            const float halfWidth = 0.55f;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i <= across; i++)
            {
                float s = -1f + 2f * i / across;
                float a = Mathf.Abs(s);
                float height = 0.17f * (0.6f + 1.25f * Mathf.Pow(a, 1.3f)) * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(a, 8f)));
                float back = 0.34f * s * s;
                foreach (var (z, y) in profile)
                {
                    vertices.Add(new Vector3(s * halfWidth, y * height, z - back));
                    uvs.Add(new Vector2(0.5f, y));
                }
            }
            int n = profile.Length;
            for (int i = 0; i < across; i++)
                for (int j = 0; j < n - 1; j++)
                {
                    int k = i * n + j, k2 = (i + 1) * n + j;
                    triangles.AddRange(new[] { k, k2, k + 1, k + 1, k2, k2 + 1 });
                }
            var mesh = new Mesh { name = "BashieWave" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return _kit.SaveMesh(mesh);
        }

        /// <summary>Toon pool water up the wave: a deep rim at its foot, blue, light cyan, a white foam crest.</summary>
        private static Color WavePixel(float u, float v)
        {
            Color c = v < 0.08f ? Rim : v < 0.55f ? Blue : v < 0.82f ? Cyan : Foam;
            c.a = v < 0.04f ? v / 0.04f : 0.95f;
            return c;
        }

        // ── Hose Down ────────────────────────────────────────────────────────────

        /// <summary>
        /// One drop of the stream, as its projectile visual (+Y along the flight). Each drop fires a few streaks at its
        /// own speed in a narrow cone, so twenty drops a second make a hose spray that fans out as it flies; mist and
        /// drips peel off along the way and hang on after the drop lands.
        /// </summary>
        private static GameObject StreamDrop()
        {
            var root = _kit.NewRoot("BashieHoseDrop", attackBound: false);
            var streaks = Particles(root, "Streaks", _m.Streak, 0.5f);
            Burst(streaks, 5);
            var main = streaks.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(StreamSpeed * 0.88f, StreamSpeed * 1.04f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.11f, 0.19f);
            main.startColor = new Color(1f, 1f, 1f, 0.88f);
            main.gravityModifier = StreamGravity / 9.81f;
            main.maxParticles = 8;
            var shape = streaks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = 0.05f;
            shape.rotation = new Vector3(-90f, 0f, 0f);          // the cone fires along the drop's +Y
            // The quad trails behind each streak: its length is its speed plus a little of its size.
            Stretch(streaks, 0.065f, 1.1f);
            SizeOverLife(streaks, Curve((0f, 0.55f), (0.25f, 1f), (1f, 1.7f)));
            Fade(streaks, 0.7f);

            var mist = Particles(root, "Mist", _m.Mist, 0.5f);
            var emission = mist.emission;
            emission.rateOverDistance = 2.5f;
            main = mist.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.36f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(mist, Curve((0f, 0.6f), (1f, 1.6f)));
            Fade(mist, 0.3f);
            Behind(mist);
            mist.gameObject.AddComponent<LingerAfterProjectile>().Seconds = 0.4f;

            var drips = Particles(root, "Drips", _m.Drop, 0.5f);
            emission = drips.emission;
            emission.rateOverDistance = 0.8f;
            main = drips.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            main.gravityModifier = 1.3f;
            Sphere(drips, 0.12f);
            Fade(drips, 0.4f);
            drips.gameObject.AddComponent<LingerAfterProjectile>().Seconds = 0.45f;
            return _kit.Save(root);
        }

        /// <summary>While she pumps: each stroke spits a few drops out of the muzzle, with the pump's squeaks.</summary>
        private static GameObject Pump(Vector3 muzzle, Quaternion barrel)
        {
            var root = _kit.NewRoot("BashieHosePump", attackBound: true);
            var at = Muzzle(root, muzzle, barrel);
            var spits = Particles(at, "Spits", _m.Drop, 12f);
            var emission = spits.emission;
            // One spit per stroke of her 24-tick pump loop.
            emission.SetBursts(new[] { new ParticleSystem.Burst(0.2f, 3, 4, 0, 0.4f) });
            var main = spits.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.gravityModifier = 1.2f;
            var shape = spits.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.02f;
            Fade(spits, 0.6f);
            _kit.Sound(root, "bashie_pump", 0.4f, 1f);
            return _kit.Save(root);
        }

        /// <summary>
        /// The release: a splash crown at the muzzle, a gush of water thrown forward with a burst of drops, mist, and
        /// the hose's rush. It grows with the pump, as Manki's flame does with his shake (owner, 2026-10-22: "the
        /// release could splash a lot more water, depending on charge time like manki F"): a tap is a squirt, a full
        /// pump nearly twice the size with three and a half times the water.
        /// </summary>
        private static GameObject Release(Vector3 muzzle, Quaternion barrel)
        {
            var root = _kit.NewRoot("BashieHoseRelease", attackBound: true);
            var at = Muzzle(root, muzzle, barrel);
            var grow = new GameObject("Grow");
            grow.transform.SetParent(at.transform, false);
            var charge = grow.AddComponent<ChargeScale>();
            charge.Slot = AbilitySlots.A;
            charge.MinScale = 0.7f;
            charge.MaxScale = 1.9f;
            charge.MaxCount = 3.5f;
            charge.MaxSpeed = 1.6f;
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;

            var crown = Particles(grow, "Crown", _m.Crown, 0.3f, ParticleSystemSimulationSpace.Local, scaled);
            Burst(crown, 1);
            var main = crown.main;
            main.startLifetime = 0.18f;
            main.startSize = 0.42f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 1;
            SizeOverLife(crown, Curve((0f, 0.45f), (0.4f, 1f), (1f, 1.15f)));
            Fade(crown, 0.4f);
            InFront(crown);

            var gush = Particles(grow, "Gush", _m.Streak, 0.4f, scaling: scaled);
            Burst(gush, 6);
            main = gush.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
            main.gravityModifier = 1f;
            var shape = gush.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = 0.04f;
            Stretch(gush, 0.06f, 1f);
            SizeOverLife(gush, Curve((0f, 0.6f), (0.3f, 1f), (1f, 1.3f)));
            Fade(gush, 0.6f);

            var burst = Particles(grow, "Burst drops", _m.Drop, 0.4f, scaling: scaled);
            Burst(burst, 8);
            main = burst.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.gravityModifier = 1.2f;
            shape = burst.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 38f;
            shape.radius = 0.03f;
            Fade(burst, 0.6f);

            var mist = Particles(grow, "Mist", _m.Mist, 0.4f, scaling: scaled);
            Burst(mist, 2);
            main = mist.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            shape = mist.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 0.05f;
            SizeOverLife(mist, Curve((0f, 0.6f), (1f, 1.5f)));
            Fade(mist, 0.3f);
            _kit.Sound(root, "bashie_spray", 0.55f, 1f);
            return _kit.Save(root);
        }

        /// <summary>
        /// The rule she is breaking: a NO SPLASHING! pool sign pops just over her hat, a little toward where she
        /// sprays so the match camera keeps it in frame, with a soft pop; it hangs and fades. A positive pivot keeps
        /// it in front of the fighters.
        /// </summary>
        private static GameObject Sign()
        {
            var root = _kit.NewRoot("BashieHoseSign", attackBound: false);
            var sign = Particles(root, "Sign", _m.Sign, 1.2f, ParticleSystemSimulationSpace.Local);
            Burst(sign, 1);
            var main = sign.main;
            main.startLifetime = 1.05f;
            main.startSize3D = true;
            main.startSizeX = 1.3f;                           // the sign texture's 512 x 84
            main.startSizeY = 0.213f;
            main.startSizeZ = 1f;
            main.startRotation = -6f * Mathf.Deg2Rad;
            SizeOverLife(sign, Curve((0f, 0f), (0.08f, 1.15f), (0.14f, 0.95f), (0.2f, 1f), (0.85f, 1f), (1f, 0.85f)));
            Fade(sign, 0.8f);
            var renderer = sign.GetComponent<ParticleSystemRenderer>();
            renderer.pivot = new Vector3(0f, 0f, 1.2f);
            InFront(sign);
            _kit.Sound(root, "bashie_sign", 0.5f, 1f);
            return _kit.Save(root);
        }

        /// <summary>
        /// A drop of the stream landing on someone: a splash crown, drops flung back, a puff of mist and a little
        /// puddle on the floor. Contacts scale by damage and a push deals none, so it is drawn at the 0.6 floor.
        /// </summary>
        private static GameObject Hit()
        {
            var root = _kit.NewRoot("BashieHoseHit", attackBound: false);
            const ParticleSystemScalingMode scaled = ParticleSystemScalingMode.Hierarchy;
            var crown = Particles(root, "Crown", _m.Crown, 0.3f, ParticleSystemSimulationSpace.Local, scaled);
            Burst(crown, 1);
            var main = crown.main;
            main.startLifetime = 0.18f;
            main.startSize = 0.8f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            SizeOverLife(crown, Curve((0f, 0.5f), (0.4f, 1f), (1f, 1.1f)));
            Fade(crown, 0.4f);
            InFront(crown);
            var drops = Particles(root, "Drops", _m.Drop, 0.3f, scaling: scaled);
            Burst(drops, 6);
            main = drops.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
            main.gravityModifier = 1.4f;
            Sphere(drops, 0.15f);
            Fade(drops, 0.6f);
            InFront(drops);
            var mist = Particles(root, "Mist", _m.Mist, 0.3f, scaling: scaled);
            Burst(mist, 1);
            main = mist.main;
            main.startLifetime = 0.3f;
            main.startSize = 0.7f;
            SizeOverLife(mist, Curve((0f, 0.6f), (1f, 1.4f)));
            Fade(mist, 0.3f);
            var floor = new GameObject("Floor");
            floor.transform.SetParent(root.transform, false);
            floor.AddComponent<SnapToFloor>();
            var puddle = Particles(floor, "Puddle", _m.Puddle, 0.7f, ParticleSystemSimulationSpace.Local, scaled);
            Burst(puddle, 1);
            main = puddle.main;
            main.startLifetime = 0.6f;
            main.startSize = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            puddle.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            SizeOverLife(puddle, Curve((0f, 0.5f), (0.3f, 1f), (1f, 1.05f)));
            Fade(puddle, 0.5f);
            _kit.Sound(root, "bashie_splash", 0.22f, 1f);
            return _kit.Save(root);
        }

        // ── Shared ───────────────────────────────────────────────────────────────

        /// <summary>The muzzle of the blaster in a bone cue: a unit-scale frame, then the muzzle, +Z down the barrel.</summary>
        private static GameObject Muzzle(GameObject root, Vector3 position, Quaternion barrel)
        {
            var frame = new GameObject("Frame");
            frame.transform.SetParent(root.transform, false);
            frame.AddComponent<PresentationFrame>().UnitScale = true;
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(frame.transform, false);
            muzzle.transform.localPosition = position;
            muzzle.transform.localRotation = barrel;
            return muzzle;
        }

        private static Materials BuildMaterials()
        {
            var signMask = new MaskField(SignMaskPath);
            return new Materials
            {
                Streak = _kit.ParticleMaterial("BashieStreak", _kit.BakeTexture("BashieStreak", 256, 64, StreakPixel)),
                Drop = _kit.ParticleMaterial("BashieDrop", _kit.BakeTexture("BashieDrop", 64, 64, DropPixel)),
                Mist = _kit.ParticleMaterial("BashieMist", _kit.BakeTexture("BashieMist", 64, 64, MistPixel)),
                Crown = _kit.ParticleMaterial("BashieCrown", _kit.BakeTexture("BashieCrown", 256, 256, CrownPixel)),
                Puddle = _kit.ParticleMaterial("BashiePuddle", _kit.BakeTexture("BashiePuddle", 128, 128, PuddlePixel)),
                Sign = _kit.ParticleMaterial("BashieSign", _kit.BakeTexture("BashieSign", 512, 84,
                    (u, v) => SignPixel(u, v, signMask))),
                Wave = _kit.ParticleMaterial("BashieWave", _kit.BakeTexture("BashieWave", 8, 128, WavePixel), Color.white),
                WaveMesh = WaveMesh(),
            };
        }

        /// <summary>
        /// A streak of water along U: its round head at U = 0 (a stretched billboard's particle end), tapering
        /// toward the tail, with a foam highlight along its upper side.
        /// </summary>
        private static Color StreakPixel(float u, float v)
        {
            float x = u * 256f, y = (v - 0.5f) * 64f;
            const float R = 26f;
            float half = Mathf.Lerp(R, 4f, Mathf.Pow(Mathf.Clamp01((x - R) / (256f - R)), 0.8f));
            float head = new Vector2(x - R, y).magnitude - R;
            float body = x < R ? 999f : Mathf.Abs(y) - half;
            float distance = Mathf.Min(head, body);
            var color = Bands(distance, 2f, half * 0.5f, Rim, Blue, Cyan);
            float line = Mathf.Abs(y - half * 0.45f) - Mathf.Lerp(3.5f, 0.8f, u);
            float highlight = Mathf.Clamp01(0.5f - line) * Step(0.04f, 0.12f, u) * (1f - Step(0.55f, 0.8f, u));
            color = Color.Lerp(color, Foam, highlight * color.a);
            color.a *= 1f - Step(0.75f, 1f, u);
            return color;
        }

        /// <summary>A round drop with a foam glint.</summary>
        private static Color DropPixel(float u, float v)
        {
            float distance = (Radius(u, v) - 0.82f) * 32f;
            var color = Bands(distance, 2.2f, 6f, Rim, Blue, Cyan);
            float glint = new Vector2(u - 0.38f, v - 0.64f).magnitude * 64f - 5f;
            return Color.Lerp(color, Foam, Mathf.Clamp01(0.5f - glint) * color.a);
        }

        /// <summary>Soft spray mist: a pale puff that fades out toward its edge.</summary>
        private static Color MistPixel(float u, float v)
        {
            float r = Radius(u, v);
            float lumpy = r - 0.08f * (ValueNoise(u, v, 5f, 3) - 0.5f);
            var color = Color.Lerp(Foam, Cyan, 0.25f);
            color.a = Mathf.Pow(Mathf.Clamp01(1f - lumpy), 1.6f) * 0.75f;
            return color;
        }

        /// <summary>A splash crown: a spiky burst of water around a foam heart.</summary>
        private static Color CrownPixel(float u, float v)
        {
            var p = new Vector2(u * 2f - 1f, v * 2f - 1f);
            float distance = Polygon(p, StarOutline(9, 0.52f, 21)) * 128f;
            var color = Bands(distance, 3f, 20f, Rim, Blue, Cyan);
            float heart = (p.magnitude - 0.3f - 0.06f * Mathf.Sin(Mathf.Atan2(p.y, p.x) * 7f)) * 128f;
            return Color.Lerp(color, Foam, Mathf.Clamp01(0.5f - heart) * color.a);
        }

        /// <summary>A little puddle seen from above: a wobbly pool of water with a foam glint.</summary>
        private static Color PuddlePixel(float u, float v)
        {
            var p = new Vector2(u * 2f - 1f, v * 2f - 1f);
            float wobble = 0.08f * (ValueNoise(u, v, 4f, 9) - 0.5f) + 0.04f * Mathf.Sin(Mathf.Atan2(p.y, p.x) * 5f);
            float distance = (p.magnitude - 0.86f - wobble) * 64f;
            var color = Bands(distance, 1.6f, 7f, Rim, Blue, Cyan);
            float glint = (new Vector2((p.x + 0.25f) * 0.8f, (p.y - 0.3f) * 1.6f).magnitude - 0.28f) * 64f;
            color = Color.Lerp(color, Foam, Mathf.Clamp01(0.5f - glint) * color.a * 0.85f);
            color.a *= 0.85f;
            return color;
        }

        /// <summary>
        /// The NO SPLASHING! pool sign: a white board with a blue border and a dark rim, red lettering from the mask,
        /// and a no-splash pictogram (a splash in a red no-circle) at its left end.
        /// </summary>
        private static Color SignPixel(float u, float v, MaskField mask)
        {
            // Texels: 512 x 84. The board fills the texture with a little margin.
            var p = new Vector2(u * 512f - 256f, v * 84f - 42f);
            float board = Box(p, new Vector2(250f, 38f) - Vector2.one * 10f) - 10f;
            var color = Bands(board, 3.5f, 6f, Rim, SignBlue, Color.white);
            if (color.a <= 0f) return color;
            // Lettering: the mask's text box (u 0.038-0.966, v 0.462-0.538: 950 x 77 mask pixels), kept to its
            // aspect, in the board right of the pictogram.
            const float Left = -172f, Right = 238f;
            float texels = (Right - Left) / ((0.966f - 0.038f) * mask.Width);
            float halfHeight = 0.038f * mask.Height * texels;
            float lu = Mathf.InverseLerp(Left, Right, p.x), lv = Mathf.InverseLerp(-halfHeight, halfHeight, p.y);
            if (lu >= -0.02f && lu <= 1.02f && lv >= -0.3f && lv <= 1.3f)
            {
                float mu = Mathf.LerpUnclamped(0.038f, 0.966f, lu), mv = Mathf.LerpUnclamped(0.462f, 0.538f, lv);
                float letter = mask.Sample(mu, mv) * texels;
                color = Color.Lerp(color, Bands(letter, 2f, 99f, Ink, SignRed, SignRed), Mathf.Clamp01(0.5f - letter));
            }
            // Pictogram: a splash crown inside a red no-circle with a slash.
            var c = p - new Vector2(-210f, 0f);
            float splash = Polygon(c / 19f, StarOutline(7, 0.5f, 5)) * 19f;
            color = Color.Lerp(color, Bands(splash, 1.2f, 3f, Rim, Blue, Cyan), Mathf.Clamp01(0.5f - splash));
            float ring = Mathf.Abs(c.magnitude - 26f) - 3.5f;
            float slash = Box(Rotate(c, 45f), new Vector2(26f, 3.2f));
            return Color.Lerp(color, SignRed, Mathf.Clamp01(0.5f - Mathf.Min(ring, slash)));
        }
    }
}
#endif
