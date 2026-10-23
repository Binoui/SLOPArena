using UnityEngine;

namespace SlopArena.Client.Combat
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ProjectileMaterialOwner : MonoBehaviour
    {
        [SerializeField, HideInInspector] internal Material[] Materials;

        private void OnDestroy()
        {
            if (Materials == null) return;
            foreach (var material in Materials)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }
    }
}
