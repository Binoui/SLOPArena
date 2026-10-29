using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.World
{
    /// <summary>Faces a cosmetic silhouette toward the render camera about its own up axis.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class YawBillboard : MonoBehaviour
    {
        public float ViewRightOffset;
        private Vector3 _anchor;

        private void Awake() => _anchor = transform.localPosition;

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += FaceCamera;
        private void OnDisable() => RenderPipelineManager.beginCameraRendering -= FaceCamera;

        private void FaceCamera(ScriptableRenderContext context, UnityEngine.Camera camera)
        {
            if (camera == null) return;
            Vector3 up = transform.parent != null ? transform.parent.up : Vector3.up;
            Vector3 facing = Vector3.ProjectOnPlane(-camera.transform.forward, up);
            if (facing.sqrMagnitude < .0001f) return;
            transform.rotation = Quaternion.LookRotation(facing, up);
            Vector3 origin = transform.parent != null ? transform.parent.TransformPoint(_anchor) : _anchor;
            transform.position = origin + camera.transform.right * ViewRightOffset;
        }
    }
}
