using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Swaps between a regular and a heavy look by the spawn scale that hit contacts carry
    /// (<c>clamp(sqrt(damage / 6), 0.6, 1.6)</c>), e.g. a combo's finisher hit.
    /// Both children start inactive.
    /// </summary>
    public sealed class DamageScaleVariant : MonoBehaviour
    {
        public float HeavyFromScale = 1.2f;
        public GameObject Regular, Heavy;

        private void Start()
        {
            bool heavy = transform.lossyScale.x >= HeavyFromScale;
            if (Regular != null) Regular.SetActive(!heavy);
            if (Heavy != null) Heavy.SetActive(heavy);
        }
    }
}
