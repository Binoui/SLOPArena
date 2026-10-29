using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>Flaps the package crow in flight and folds its wings while the resolver reports a perch.</summary>
    public sealed class NyxCrowVisual : MonoBehaviour
    {
        public Transform LeftWing, RightWing;
        private bool _perched;
        private float _age;
        public void SetPerched(bool perched) => _perched = perched;
        private void LateUpdate()
        {
            _age += Time.deltaTime;
            float flap = _perched ? 68f : Mathf.Sin(_age * 32f) * 40f;
            if (LeftWing != null) LeftWing.localRotation = Quaternion.Euler(0f, -flap, _perched ? 56f : -12f);
            if (RightWing != null) RightWing.localRotation = Quaternion.Euler(0f, flap, _perched ? -56f : 12f);
        }
    }
}
