using SlopArena.Client.Entities;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Nyx's cast effect (ADR-0051 section 11): an ink flame and broken sigil on the casting hand, and an ink pool with
    /// short tendrils at her feet, or a flat ink swirl under her boots in the air. The cue is attack-bound, so recovery and
    /// interruption end it. Its motion runs on the attack clock, so hitstop holds it like her attack.
    /// </summary>
    public sealed class NyxCastAura : MonoBehaviour, IPresentationOwnerReceiver
    {
        public Transform Flame, Sigil, Pool, Tendrils, Swirl;
        private PlayerRenderer _owner;
        private Transform _hand;
        private int _spawnTick;

        public void BindOwner(PlayerRenderer owner)
        {
            _owner = owner;
            // The placement parented this cue to the casting hand; keep that bone, drop its import scale.
            _hand = transform.parent;
            _spawnTick = owner != null ? owner.CurrentAttackElapsedTicks : 0;
            transform.SetParent(null, true);
            transform.localScale = Vector3.one;
            Apply();
        }

        private void LateUpdate() => Apply();

        private void Apply()
        {
            if (_owner == null) return;
            float age = Mathf.Max(0, _owner.CurrentAttackElapsedTicks - _spawnTick);
            var feet = _owner.transform.position - Vector3.up * _owner.ModelYOffset;
            var yaw = Quaternion.Euler(0f, _owner.transform.eulerAngles.y, 0f);
            transform.SetPositionAndRotation(feet, yaw);
            // Full size on the first tick, then a slow ink breath.
            float breath = 1f + .06f * Mathf.Sin(age * .35f);
            if (Flame != null)
            {
                Flame.SetPositionAndRotation(_hand != null ? _hand.position : feet + Vector3.up, yaw);
                Flame.localScale = new Vector3(1f, 1f + .18f * Mathf.Sin(age * .9f), 1f);
            }
            if (Sigil != null)
            {
                Sigil.SetPositionAndRotation(_hand != null ? _hand.position : feet + Vector3.up, yaw * Quaternion.Euler(0f, age * 9f, 0f));
                Sigil.localScale = Vector3.one * breath;
            }
            bool grounded = _owner.CurrentIsGrounded;
            if (Pool != null)
            {
                Pool.gameObject.SetActive(grounded);
                Pool.SetPositionAndRotation(feet + Vector3.up * .03f, yaw);
                Pool.localScale = new Vector3(breath, 1f, breath);
            }
            if (Tendrils != null)
            {
                Tendrils.gameObject.SetActive(grounded);
                Tendrils.SetPositionAndRotation(feet, yaw * Quaternion.Euler(0f, age * 2.5f, 0f));
                Tendrils.localScale = new Vector3(1f, 1f + .12f * Mathf.Sin(age * .6f), 1f);
            }
            if (Swirl != null)
            {
                Swirl.gameObject.SetActive(!grounded);
                // A little under the capsule's base, below her boots in an airborne pose.
                Swirl.SetPositionAndRotation(feet - Vector3.up * .15f, yaw * Quaternion.Euler(0f, -age * 12f, 0f));
                Swirl.localScale = Vector3.one * breath;
            }
        }
    }
}
