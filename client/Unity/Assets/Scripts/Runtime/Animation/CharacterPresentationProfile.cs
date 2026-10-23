using System;
using SlopArena.Shared;
using SlopArena.Client.Combat;
using UnityEngine;

namespace SlopArena.Client.Animation
{
    public enum AudioPoolSelection { Fixed, Cycle }
    public enum ProjectileOrientation { PreservePrefab, FlightPositiveY }

    /// <summary>Package-owned presentation policy. Unity references are borrowed; arrays are owned.</summary>
    [Serializable]
    public sealed class CharacterPresentationProfile
    {
        [SerializeField] private Color _respawnTint = new(1f, 1f, 1f, .95f);
        [SerializeField] private AudioPool _swing = new(AudioPoolSelection.Fixed, Array.Empty<AudioClip>());
        [SerializeField] private AudioPool _lightImpact = new(AudioPoolSelection.Fixed, new AudioClip[1]);
        [SerializeField] private AudioPool _mediumImpact = new(AudioPoolSelection.Fixed, new AudioClip[1]);
        [SerializeField] private AudioPool _heavyImpact = new(AudioPoolSelection.Fixed, new AudioClip[1]);
        [SerializeField] private bool _suppressRemovalExplosion;
        [SerializeField] private ProjectilePresentation[] _projectiles = Array.Empty<ProjectilePresentation>();

        public Color RespawnTint => _respawnTint;
        public AudioPool Swing => _swing;
        public AudioPool LightImpact => _lightImpact;
        public AudioPool MediumImpact => _mediumImpact;
        public AudioPool HeavyImpact => _heavyImpact;
        public bool SuppressRemovalExplosion => _suppressRemovalExplosion;
        public ReadOnlySpan<ProjectilePresentation> Projectiles => _projectiles;

        public CharacterPresentationProfile() { }
        public CharacterPresentationProfile(Color respawnTint, AudioPool swing, AudioPool lightImpact,
            AudioPool mediumImpact, AudioPool heavyImpact, bool suppressRemovalExplosion,
            ProjectilePresentation[] projectiles)
        {
            _respawnTint = respawnTint;
            _swing = swing?.DeepClone();
            _lightImpact = lightImpact?.DeepClone();
            _mediumImpact = mediumImpact?.DeepClone();
            _heavyImpact = heavyImpact?.DeepClone();
            _suppressRemovalExplosion = suppressRemovalExplosion;
            _projectiles = CloneRows(projectiles);
        }

        public AudioClip SelectSwing(ref int sequence) => _swing.Select(ref sequence);
        public AudioClip SelectImpact(ImpactTier tier, ref int sequence)
            => (tier switch
            {
                ImpactTier.Heavy or ImpactTier.Launch => _heavyImpact,
                ImpactTier.Medium => _mediumImpact,
                _ => _lightImpact,
            }).Select(ref sequence);

        public ProjectilePresentation FindProjectile(byte wireSlot, bool activationAirborne)
        {
            if (!CanonicalSlotProjection.TryGet(wireSlot, activationAirborne, out var slot)) return null;
            foreach (var row in _projectiles)
                if (row.SlotId == slot.Id) return row;
            return null;
        }

        public CharacterPresentationProfile DeepClone()
            => new(_respawnTint, _swing, _lightImpact, _mediumImpact, _heavyImpact,
                _suppressRemovalExplosion, _projectiles);
        public CharacterPresentationProfile WithProjectiles(ProjectilePresentation[] rows)
            => new(_respawnTint, _swing, _lightImpact, _mediumImpact, _heavyImpact,
                _suppressRemovalExplosion, rows);
        private static ProjectilePresentation[] CloneRows(ProjectilePresentation[] rows)
            => Array.ConvertAll(rows ?? Array.Empty<ProjectilePresentation>(), row => row?.DeepClone());
    }

    [Serializable]
    public sealed class AudioPool
    {
        [SerializeField] private AudioPoolSelection _selection;
        [SerializeField] private AudioClip[] _clips = Array.Empty<AudioClip>();
        // Committed bindings can reference uninstalled media (ADR-0038). Retain every position and ID.
        [SerializeField, HideInInspector] private string[] _clipObjectIds = Array.Empty<string>();
        public AudioPoolSelection Selection => _selection;
        public ReadOnlySpan<AudioClip> Clips => _clips;
        public ReadOnlySpan<string> ClipObjectIds => _clipObjectIds;
        public AudioPool() { }
        public AudioPool(AudioPoolSelection selection, AudioClip[] clips, string[] clipObjectIds = null)
        {
            _selection = selection;
            _clips = (AudioClip[])(clips ?? Array.Empty<AudioClip>()).Clone();
            _clipObjectIds = (string[])(clipObjectIds ?? Array.Empty<string>()).Clone();
        }
        public AudioClip Select(ref int sequence)
        {
            if (_clips.Length == 0) return null;
            if (_selection == AudioPoolSelection.Fixed) return _clips[0];
            int index = (int)((uint)sequence % (uint)_clips.Length);
            sequence = unchecked(sequence + 1);
            return _clips[index];
        }
        public AudioPool DeepClone() => new(_selection, _clips, _clipObjectIds);
    }

    [Serializable]
    public sealed class ProjectilePresentation
    {
        [SerializeField] private string _slotId = "";
        [SerializeField] private GameObject _prefab;
        [SerializeField] private float _scale = 1f;
        [SerializeField] private ProjectileOrientation _orientation;
        [SerializeField] private string _heldBoneId = "";
        [SerializeField, HideInInspector] private string _prefabObjectId = "";
        public string SlotId => _slotId;
        public GameObject Prefab => _prefab;
        public float Scale => _scale;
        public ProjectileOrientation Orientation => _orientation;
        public string HeldBoneId => _heldBoneId;
        public string PrefabObjectId => _prefabObjectId;
        public ProjectilePresentation() { }
        public ProjectilePresentation(string slotId, GameObject prefab, float scale,
            ProjectileOrientation orientation, string heldBoneId = "", string prefabObjectId = "")
        {
            _slotId = slotId; _prefab = prefab; _scale = scale; _orientation = orientation;
            _heldBoneId = heldBoneId; _prefabObjectId = prefabObjectId;
        }
        public ProjectilePresentation DeepClone()
            => new(_slotId, _prefab, _scale, _orientation, _heldBoneId, _prefabObjectId);
    }
}
