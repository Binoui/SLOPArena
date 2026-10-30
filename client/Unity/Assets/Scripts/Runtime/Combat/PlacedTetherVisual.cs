using UnityEngine;

namespace SlopArena.Client.Combat
{
    /// <summary>A catalog-bound hand carried by the authoritative tether partner, with a paper drag tail.</summary>
    public sealed class PlacedTetherVisual : MonoBehaviour
    {
        public GameObject DragPrefab;
        public Transform Drag;
        public void Present(Vector3 feet, Vector3 towardOwner)
        {
            Vector3 direction = Vector3.ProjectOnPlane(towardOwner - feet, Vector3.up);
            transform.SetPositionAndRotation(feet, direction.sqrMagnitude > .0001f
                ? Quaternion.LookRotation(direction) : Quaternion.identity);
            if (Drag != null)
            {
                Drag.gameObject.SetActive(true);
                // The tendril mesh is one metre long: stretched, it reaches back to her hand.
                Drag.localScale = new Vector3(1f, 1f, Mathf.Max(.4f, direction.magnitude));
            }
        }
    }
}
