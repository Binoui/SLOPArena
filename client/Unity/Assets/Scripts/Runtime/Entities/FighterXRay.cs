using System.Collections.Generic;
using SlopArena.Client.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// Seat-colour silhouette of the body parts the stage hides. Each styled body skin gets a child copy drawn with
    /// FighterXRay (queue Geometry+40, ZTest Greater) between the stage and the fighters (GraphicCharacter, Geometry+50),
    /// so it only shows where stage geometry covers the fighter, including walkable tops the sight holes keep solid.
    /// The opaque props its rig carries (a held weapon, a shield) are moved to the fighters' queue for the same reason:
    /// drawn with the stage they would sit in the depth buffer first and show the silhouette through themselves.
    /// </summary>
    public sealed class FighterXRay : MonoBehaviour
    {
        public static bool Enabled = true;

        private const string CopySuffix = "_XRay";
        private const int FighterQueue = (int)RenderQueue.Geometry + 50;   // GraphicCharacter's queue
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly Dictionary<Material, Material> PropMaterials = new();   // a prop's material → its copy drawn with the fighter
        private static readonly List<Material> PropScratch = new();
        private static Material? _material;

        private readonly List<SkinnedMeshRenderer> _skins = new();
        private readonly Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer> _copies = new();
        private readonly List<SkinnedMeshRenderer> _gone = new();
        private readonly List<MeshRenderer> _props = new();
        private Transform? _rig;
        private MaterialPropertyBlock? _block;
        private PlayerRenderer? _fighter;

        // Entering Play Mode without a domain reload keeps statics; the copies belong to the session that made them.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay()
        {
            foreach (var copy in PropMaterials.Values)
                if (copy != null) Destroy(copy);
            PropMaterials.Clear();
        }

        private void Awake() => _fighter = GetComponent<PlayerRenderer>();

        private void LateUpdate()
        {
            if (_material == null)
            {
                var shader = Resources.Load<Shader>("Shaders/SlopArenaFighterXRay");
                if (shader == null) return;
                _material = new Material(shader) { name = "FighterXRay (runtime)" };
            }

            _block ??= new MaterialPropertyBlock();
            _block.SetColor(ColorId, _fighter != null && _fighter.PlayerNumber > 0
                ? SeatColours.ForFighter(_fighter.EntityId, _fighter.PlayerNumber) : Color.white);

            GetComponentsInChildren(true, _skins);
            foreach (var skin in _skins)
            {
                if (skin.name.EndsWith(CopySuffix) || !Styled(skin)) continue;
                if (!_copies.TryGetValue(skin, out var copy) || copy == null)
                    _copies[skin] = copy = CreateCopy(skin);
                if (copy.sharedMesh != skin.sharedMesh) Sync(copy, skin);
                copy.enabled = Enabled && skin.enabled;
                copy.SetPropertyBlock(_block);
            }

            // Only a match-styled fighter has a silhouette to protect, and only what its rig carries counts as its
            // own prop: effects that hang off the fighter's root keep their materials.
            if (_copies.Count > 0)
            {
                if (_rig == null)
                {
                    var animator = GetComponentInChildren<Animator>(true);
                    _rig = animator != null ? animator.transform : null;
                }
                if (_rig != null)
                {
                    _rig.GetComponentsInChildren(true, _props);
                    foreach (var prop in _props)
                        DrawWithFighter(prop);
                }
            }

            // Copies are children of their source, so a destroyed model takes its copies along.
            foreach (var pair in _copies)
                if (pair.Key == null) _gone.Add(pair.Key!);
            foreach (var key in _gone) _copies.Remove(key);
            _gone.Clear();
        }

        // An opaque prop still in the stage's queue gets copies of its materials in the fighters' queue. Checked
        // every frame without allocating, because a weapon or shield can be attached or re-skinned at any time.
        private static void DrawWithFighter(MeshRenderer prop)
        {
            prop.GetSharedMaterials(PropScratch);
            bool moved = false;
            for (int i = 0; i < PropScratch.Count; i++)
            {
                var material = PropScratch[i];
                if (material == null || material.renderQueue >= FighterQueue) continue;
                if (!PropMaterials.TryGetValue(material, out var copy) || copy == null)
                    PropMaterials[material] = copy = new Material(material) { name = material.name + " (with fighter)", renderQueue = FighterQueue };
                PropScratch[i] = copy;
                moved = true;
            }
            if (moved) prop.SetSharedMaterials(PropScratch);
        }

        // Only match-styled body skins (MatchVisualStyle's GraphicCharacter) draw after the stage.
        private static bool Styled(SkinnedMeshRenderer skin)
        {
            var material = skin.sharedMaterial;
            return skin.sharedMesh != null && material != null && material.shader.name == "SLOPArena/GraphicCharacter";
        }

        private static SkinnedMeshRenderer CreateCopy(SkinnedMeshRenderer skin)
        {
            var go = new GameObject(skin.name + CopySuffix) { layer = skin.gameObject.layer };
            go.transform.SetParent(skin.transform, false);
            var copy = go.AddComponent<SkinnedMeshRenderer>();
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            copy.lightProbeUsage = LightProbeUsage.Off;
            copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Sync(copy, skin);
            return copy;
        }

        private static void Sync(SkinnedMeshRenderer copy, SkinnedMeshRenderer skin)
        {
            copy.sharedMesh = skin.sharedMesh;
            copy.bones = skin.bones;
            copy.rootBone = skin.rootBone;
            copy.localBounds = skin.localBounds;
            copy.updateWhenOffscreen = skin.updateWhenOffscreen;
            var materials = new Material[skin.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = _material!;
            copy.sharedMaterials = materials;
        }
    }
}
