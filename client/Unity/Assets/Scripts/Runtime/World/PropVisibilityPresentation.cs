#nullable enable
using System;
using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AttackBoundPresentation))]
    public sealed class PropVisibilityPresentation : MonoBehaviour, IPresentationOwnerReceiver
    {
        [SerializeField] private byte _form;
        [SerializeField] private string[] _show = Array.Empty<string>();
        [SerializeField] private string[] _hide = Array.Empty<string>();
        private FormPresentation? _forms;

        public void Configure(byte form, string[] show, string[] hide)
        {
            _form = form;
            _show = show;
            _hide = hide;
        }

        public void BindOwner(PlayerRenderer owner)
        {
            Release();
            _forms = owner != null ? owner.GetComponentInChildren<FormPresentation>(true) : null;
            if (_forms != null) _forms.BeginPropOverride(this, _form, _show, _hide);
        }

        private void OnDisable() => Release();
        private void OnDestroy() => Release();

        private void Release()
        {
            if (_forms != null) _forms.EndPropOverride(this);
            _forms = null;
        }
    }
}
