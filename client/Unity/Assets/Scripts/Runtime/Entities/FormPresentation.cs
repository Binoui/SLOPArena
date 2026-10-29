#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// The only writer of which body a two-form fighter shows (ADR-0051). One skeleton carries both forms' meshes and
    /// props; this activates the parts of the form the simulation says she is in. A mispredicted swap flips back with
    /// the next corrected state, like any other state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FormPresentation : MonoBehaviour
    {
        [SerializeField] private GameObject[] _realForm = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] _alternateForm = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] _hiddenProps = Array.Empty<GameObject>();
        private int _shown = -1;
        private SkinnedMeshRenderer[]? _skins;
        private readonly Dictionary<UnityEngine.Object, (byte form, GameObject[] show, GameObject[] hide)> _propOverrides = new();

        public int ShownForm => _shown;
        public bool TravelHidden { get; private set; }

        public void Configure(GameObject[] realForm, GameObject[] alternateForm, GameObject[]? hiddenProps = null)
        {
            _realForm = realForm;
            _alternateForm = alternateForm;
            _hiddenProps = hiddenProps ?? Array.Empty<GameObject>();
            _propOverrides.Clear();
            _skins = null;
            _shown = -1;
        }

        public void Show(byte form, bool travelHidden = false)
        {
            if (form == _shown && TravelHidden == travelHidden) return;
            _shown = form;
            TravelHidden = travelHidden;
            ApplyVisibility();
        }

        public void BeginPropOverride(UnityEngine.Object source, byte form, string[] show, string[] hide)
        {
            var parts = _realForm.Concat(_alternateForm).Concat(_hiddenProps).Where(p => p != null).ToArray();
            GameObject[] Resolve(string[] names) => names.Select(name => parts.SingleOrDefault(p => p.name == name)
                ?? throw new InvalidOperationException($"Prop renderer '{name}' is absent from the form defaults.")).ToArray();
            _propOverrides[source] = (form, Resolve(show), Resolve(hide));
            ApplyVisibility();
        }

        public void EndPropOverride(UnityEngine.Object source)
        {
            if (_propOverrides.Remove(source)) ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            foreach (var part in _realForm) if (part != null) part.SetActive(_shown != 1 && !TravelHidden);
            foreach (var part in _alternateForm) if (part != null) part.SetActive(_shown == 1 && !TravelHidden);
            foreach (var part in _hiddenProps) if (part != null) part.SetActive(false);
            if (TravelHidden) return;
            foreach (var toggle in _propOverrides.Values)
            {
                if (toggle.form != _shown) continue;
                foreach (var part in toggle.show) if (part != null) part.SetActive(true);
                foreach (var part in toggle.hide) if (part != null) part.SetActive(false);
            }
        }

        /// <summary>Both forms' skinned meshes, so the death blink and respawn keep the hidden form's flags in step.</summary>
        public SkinnedMeshRenderer[] Skins => _skins ??= _realForm.Concat(_alternateForm).Concat(_hiddenProps).Where(part => part != null)
            .SelectMany(part => part.GetComponentsInChildren<SkinnedMeshRenderer>(true)).ToArray();
    }
}
