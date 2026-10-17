#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Particle-system construction helpers for generated character presentation
    /// (<see cref="PresentationBuilderKit"/>). Import with <c>using static</c>.
    /// </summary>
    public static class PresentationFx
    {
        /// <summary>
        /// A non-looping, world-space system that plays on spawn. Local scaling keeps sizes,
        /// speeds and shapes in metres when a cue is parented to a scaled bone (weapons often
        /// carry large import scales); hit contacts pass Hierarchy to keep their damage scale.
        /// </summary>
        public static ParticleSystem Particles(GameObject root, string name, Material material, float duration,
            ParticleSystemSimulationSpace space = ParticleSystemSimulationSpace.World,
            ParticleSystemScalingMode scaling = ParticleSystemScalingMode.Local)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            var system = child.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.duration = duration;
            main.loop = false;
            main.playOnAwake = true;
            main.startDelay = 0f;
            main.startLifetime = 0.3f;
            main.startSpeed = 0f;
            main.startSize = 0.2f;
            main.startRotation = 0f;
            main.startColor = Color.white;
            main.gravityModifier = 0f;
            main.simulationSpace = space;
            main.scalingMode = scaling;
            main.maxParticles = 64;
            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
            var shape = system.shape;
            shape.enabled = false;
            var renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.maxParticleSize = 3f;
            return system;
        }

        public static void Burst(ParticleSystem system, int count)
        {
            var emission = system.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        public static void Sphere(ParticleSystem system, float radius)
        {
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
        }

        public static void Spin(ParticleSystem system, float degreesPerSecond)
        {
            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = degreesPerSecond * Mathf.Deg2Rad;
        }

        /// <summary>Brakes particles toward 0.5 m/s; a high dampen stops a fast hop within frames.</summary>
        public static void Drag(ParticleSystem system, float dampen)
        {
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.5f;
            limit.dampen = dampen;
        }

        /// <summary>Stretched billboards: a texture's long U axis follows the velocity.</summary>
        public static void Stretch(ParticleSystem system, float velocityScale, float lengthScale)
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = velocityScale;
            renderer.lengthScale = lengthScale;
        }

        /// <summary>Straight down in world space, for rain-like layers.</summary>
        public static void Fall(ParticleSystem system, float minSpeed, float maxSpeed)
            => Drift(system, 0f, -maxSpeed, -minSpeed);

        /// <summary>Random world-space drift: ±horizontal on X and Z and a vertical range.</summary>
        public static void Drift(ParticleSystem system, float horizontal, float minUp, float maxUp)
        {
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-horizontal, horizontal);
            velocity.y = new ParticleSystem.MinMaxCurve(minUp, maxUp);
            velocity.z = new ParticleSystem.MinMaxCurve(-horizontal, horizontal);
        }

        /// <summary>Steps through a grid sheet at a fixed, choppy rate from a random frame.</summary>
        public static void FlickerFrames(ParticleSystem system, int tilesX, int tilesY, float fps)
        {
            var sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = tilesX;
            sheet.numTilesY = tilesY;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.timeMode = ParticleSystemAnimationTimeMode.FPS;
            sheet.fps = fps;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.9999f);
        }

        /// <summary>Gives each particle one random frame of a grid sheet for its whole life.</summary>
        public static void RandomFrame(ParticleSystem system, int tilesX, int tilesY)
        {
            var sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = tilesX;
            sheet.numTilesY = tilesY;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.9999f);
        }

        public static void SizeOverLife(ParticleSystem system, AnimationCurve curve)
        {
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        /// <summary>Separate length (X) and thickness (Y) curves for 3D-sized slashes.</summary>
        public static void SizeOverLife(ParticleSystem system, AnimationCurve length, AnimationCurve thickness)
        {
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = true;
            size.x = new ParticleSystem.MinMaxCurve(1f, length);
            size.y = new ParticleSystem.MinMaxCurve(1f, thickness);
            size.z = new ParticleSystem.MinMaxCurve(1f, Curve((0f, 1f), (1f, 1f)));
        }

        /// <summary>Holds full opacity until <paramref name="hold"/> of the lifetime, then fades out.</summary>
        public static void Fade(ParticleSystem system, float hold)
        {
            var color = system.colorOverLifetime;
            color.enabled = true;
            color.color = GradientOf(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 1f), (hold, 1f), (1f, 0f) });
        }

        /// <summary>Sorts hit layers over the character's other effects at the same spot.</summary>
        public static void InFront(ParticleSystem system) => system.GetComponent<ParticleSystemRenderer>().sortingFudge = -10f;

        /// <summary>Sorts a soft halo behind the alpha-blended shapes at the same spot.</summary>
        public static void Behind(ParticleSystem system) => system.GetComponent<ParticleSystemRenderer>().sortingFudge = 20f;

        public static ParticleSystem.MinMaxGradient GradientOf((float time, Color color)[] colors, (float time, float alpha)[] alphas)
        {
            var gradient = new Gradient();
            gradient.SetKeys(colors.Select(x => new GradientColorKey(x.color, x.time)).ToArray(),
                alphas.Select(x => new GradientAlphaKey(x.alpha, x.time)).ToArray());
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>A piecewise-linear curve through (time, value) keys.</summary>
        public static AnimationCurve Curve(params (float time, float value)[] keys)
        {
            var curve = new AnimationCurve(keys.Select(k => new Keyframe(k.time, k.value)).ToArray());
            for (int i = 0; i < curve.length; i++)
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            for (int i = 0; i < curve.length; i++)
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            return curve;
        }

        /// <summary>Stepped values for pixel flicker; constant tangents hold each value until the next key.</summary>
        public static AnimationCurve Steps(params float[] values)
        {
            var keys = new Keyframe[values.Length];
            for (int i = 0; i < values.Length; i++)
                keys[i] = new Keyframe(i / (float)(values.Length - 1), values[i], float.PositiveInfinity, float.PositiveInfinity);
            return new AnimationCurve(keys);
        }
    }
}
#endif
