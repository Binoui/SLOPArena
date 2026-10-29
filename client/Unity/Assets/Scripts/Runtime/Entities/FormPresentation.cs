#nullable enable
using System;
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
        private int _shown = -1;
        private SkinnedMeshRenderer[]? _skins;

        public int ShownForm => _shown;

        public void Configure(GameObject[] realForm, GameObject[] alternateForm)
        {
            _realForm = realForm;
            _alternateForm = alternateForm;
            _skins = null;
            _shown = -1;
        }

        public void Show(byte form)
        {
            if (form == _shown) return;
            _shown = form;
            foreach (var part in _realForm) if (part != null) part.SetActive(form != 1);
            foreach (var part in _alternateForm) if (part != null) part.SetActive(form == 1);
        }

        /// <summary>Both forms' skinned meshes, so the death blink and respawn keep the hidden form's flags in step.</summary>
        public SkinnedMeshRenderer[] Skins => _skins ??= _realForm.Concat(_alternateForm).Where(part => part != null)
            .SelectMany(part => part.GetComponentsInChildren<SkinnedMeshRenderer>(true)).ToArray();
    }
}
