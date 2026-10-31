using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    public sealed class MankiChargeIris : MonoBehaviour, IPresentationOwnerReceiver
    {
        public int ChargeTicks = 40;
        private PlayerRenderer _owner;
        public void BindOwner(PlayerRenderer owner) => _owner = owner;
        private void LateUpdate()
        {
            if (_owner == null) return;
            float progress = Mathf.Clamp01(_owner.CurrentAttackElapsedTicks / (float)ChargeTicks);
            transform.localScale = Vector3.one * (_owner.CurrentAttackElapsedTicks < ChargeTicks ? Mathf.Lerp(1.15f, .7f, progress) : 0f);
        }
    }
}
