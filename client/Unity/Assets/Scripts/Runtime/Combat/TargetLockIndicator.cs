using SlopArena.Client.UI;
using UnityEngine;
using SlopArena.Client.Entities;
using UnityEngine.UIElements;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Screen-space locked-target readout above the presented fighter and weapon.
    /// Performs no selection, range, physics, or gameplay queries.
    /// </summary>
    public sealed class TargetLockIndicator : MonoBehaviour
    {
        private const string VisibleClass = "visible";
        private const string DamageHiddenClass = "damage-hidden";
        private static readonly Color Paper = new(240f / 255f, 238f / 255f, 232f / 255f);
        private static readonly Color Ink = new(22f / 255f, 21f / 255f, 19f / 255f);
        private static readonly Color Hot = new(1f, 107f / 255f, 107f / 255f);
        private static readonly Color Crimson = new(239f / 255f, 68f / 255f, 68f / 255f);

        [Header("Framing")]
        [SerializeField] private float _paddingPx = 10f;
        private const float HeadClearanceWorld = 0.2f;

        private VisualElement _root;
        private Label? _damage;
        private Label? _percent;
        private VisualElement? _marker;
        private Color _markerColour = Paper;
        private int _damagePercent = -1;
        private UnityEngine.Camera _camera;
        private PlayerRenderer _target;
        private Transform _objectTarget;
        private float _objectRadius;
        private bool _showDamage = true;
        private Transform _targetHead;
        private Renderer[] _targetRenderers = System.Array.Empty<Renderer>();
        private float _authoredOpacity = 1f;
        private ClientSettingsService _settings;

        public void Init(VisualElement root, UnityEngine.Camera camera)
        {
            if (_settings != null) _settings.Changed -= ApplySettings;
            _root = root;
            _damage = root?.Q<Label>("target-lock-damage");
            _percent = root?.Q<Label>("target-lock-percent");
            _marker = root?.Q<VisualElement>("target-lock-marker");
            if (_marker != null) _marker.generateVisualContent = DrawChevron;
            _damagePercent = -1;
            _target = null;
            _objectTarget = null;
            _targetHead = null;
            _targetRenderers = System.Array.Empty<Renderer>();
            _camera = camera;
            _settings = ClientSettingsService.Instance;
            _authoredOpacity = root != null ? root.resolvedStyle.opacity : 1f;
            _settings.Changed += ApplySettings;
            ApplySettings();
            HideImmediate();
        }

        /// <summary>Show the resolved target's arrow and authoritative damage percent.</summary>
        public void SetTarget(PlayerRenderer target, bool locked, ushort damagePercent)
        {
            if (_root == null) return;
            _showDamage = true;
            _objectTarget = null;
            if (target != _target)
            {
                _targetHead = target != null ? target.ResolvePresentationBone("bone.head") : null;
                _targetRenderers = target != null ? target.GetComponentsInChildren<Renderer>(true)
                    : System.Array.Empty<Renderer>();
            }
            _target = target;
            SetMarkerColour(target != null ? SeatColours.ForFighter(target.EntityId, target.PlayerNumber) : Paper);
            if (target == null || !locked)
            {
                HideImmediate();
                return;
            }

            ShowDamage(damagePercent);
        }

        /// <summary>Object framing only; identity, eligibility and selection come from Shared.</summary>
        public void SetObjectTarget(Transform target, float radius, bool locked, ushort? damagePercent)
        {
            if (_root == null) return;
            if (_objectTarget != target)
                _targetRenderers = target != null ? target.GetComponentsInChildren<Renderer>(true)
                    : System.Array.Empty<Renderer>();
            _target = null; _targetHead = null;
            _objectTarget = target; _objectRadius = radius;
            SetMarkerColour(Paper);
            _showDamage = damagePercent.HasValue;
            if (target == null || !locked) { HideImmediate(); return; }
            if (damagePercent.HasValue) ShowDamage(damagePercent.Value);
            else { ApplySettings(); _root.EnableInClassList(VisibleClass, true); }
        }

        private void ShowDamage(ushort damagePercent)
        {
            ApplySettings();
            if (_damage != null && damagePercent != _damagePercent)
            {
                _damage.text = damagePercent.ToString();
                // Heat avoids the seat palette: paper, then red, then a bigger crimson from 90%.
                _damage.style.fontSize = damagePercent >= 1000 ? 26f : damagePercent >= 90 ? 35f : 30f;
                var color = damagePercent < 40 ? Paper : damagePercent < 90 ? Hot : Crimson;
                _damage.style.color = color;
                if (_percent != null) _percent.style.color = color;
                _damagePercent = damagePercent;
            }

            _root.EnableInClassList(VisibleClass, true);
        }
        private void SetMarkerColour(Color colour)
        {
            if (_marker == null || _markerColour == colour) return;
            _markerColour = colour;
            _marker.MarkDirtyRepaint();
        }

        private void DrawChevron(MeshGenerationContext context)
        {
            var rect = _marker!.contentRect;
            var painter = context.painter2D;
            Triangle(painter, rect, Ink);
            var inner = new Rect(rect.x + 3f, rect.y + 2f, rect.width - 6f, rect.height - 5f);
            Triangle(painter, inner, _markerColour);
        }

        private static void Triangle(Painter2D painter, Rect rect, Color colour)
        {
            painter.fillColor = colour;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.center.x, rect.yMax));
            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>Clear the presentation during lifecycle reset or lock loss.</summary>
        public void Clear(bool immediate = false)
        {
            _target = null;
            _objectTarget = null;
            _targetHead = null;
            _targetRenderers = System.Array.Empty<Renderer>();
            HideImmediate();
        }

        private void LateUpdate()
        {
            if (_root == null || (_target == null && _objectTarget == null) || !_root.ClassListContains(VisibleClass)) return;

            var cam = _camera != null ? _camera : FindRenderCamera();
            if (cam == null || _root.panel == null) return;

            // A fixed pixel offset from the body centre overlaps tall/nearby fighters.
            // Keep the horizontal anchor stable, then clear the projected visible silhouette.
            Vector3 world = _target != null
                ? _target.transform.position + Vector3.up * (_target.CapsuleHeight * 0.5f)
                : _objectTarget.position + Vector3.up * _objectRadius;
            Vector3 screenPoint = cam.WorldToScreenPoint(world);
            if (screenPoint.z <= 0.001f || !float.IsFinite(screenPoint.x) || !float.IsFinite(screenPoint.y))
            {
                _root.style.visibility = Visibility.Hidden;
                return;
            }

            if (_targetHead != null)
                RaiseAbove(cam, _targetHead.position + Vector3.up * HeadClearanceWorld, ref screenPoint);
            foreach (var renderer in _targetRenderers)
            {
                // Body/accessory/weapon geometry counts; trails and particles do not.
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                    || (renderer is not SkinnedMeshRenderer && renderer is not MeshRenderer)) continue;
                Bounds bounds = renderer.bounds;
                Vector3 min = bounds.min, max = bounds.max;
                for (int corner = 0; corner < 8; corner++)
                    RaiseAbove(cam, new Vector3((corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z), ref screenPoint);
            }

            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(_root.panel,
                new Vector2(screenPoint.x, Screen.height - screenPoint.y));
            float rootWidth = _root.resolvedStyle.width;
            float rootHeight = _root.resolvedStyle.height;
            if (!float.IsFinite(rootWidth) || !float.IsFinite(rootHeight) || rootWidth <= 0f || rootHeight <= 0f) return;
            _root.style.visibility = Visibility.Visible;
            _root.style.left = panelPoint.x - rootWidth * 0.5f;
            _root.style.top = panelPoint.y - _paddingPx - rootHeight;
        }

        private static void RaiseAbove(UnityEngine.Camera camera, Vector3 world, ref Vector3 anchor)
        {
            Vector3 projected = camera.WorldToScreenPoint(world);
            if (projected.z > 0.001f && float.IsFinite(projected.y))
                anchor.y = Mathf.Max(anchor.y, projected.y);
        }

        private void ApplySettings()
        {
            if (_root == null) return;
            _root.style.opacity = _authoredOpacity * (_settings != null ? _settings.TargetOpacity : 1f);
            _root.EnableInClassList(DamageHiddenClass, !_showDamage || _settings != null && !_settings.ShowOverheadDamage);
        }

        private void HideImmediate()
        {
            if (_root == null) return;
            _root.EnableInClassList(VisibleClass, false);
        }

        /// <summary>Resolve the live gameplay camera without selecting a target.</summary>
        private static UnityEngine.Camera FindRenderCamera()
        {
            var main = UnityEngine.Camera.main;
            if (main != null) return main;
            foreach (var c in UnityEngine.Camera.allCameras)
                if (c != null && c.enabled && c.gameObject.activeInHierarchy)
                    return c;
            return null;
        }

        private void OnEnable() => HideImmediate();

        private void OnDisable() => HideImmediate();
        private void OnDestroy()
        {
            if (_settings != null) _settings.Changed -= ApplySettings;
        }
    }
}
