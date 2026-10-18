using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// The mascot ball's look: a sphere whose painted front (the mural's face with toon shading
    /// baked in) always turns to the camera, like the course lanterns. It swaps faces by mood,
    /// squashes and stretches on a spring, flashes and shivers through hitstop and trails a
    /// streak while it flies fast. <see cref="SlopballPresentation"/> places it on the Shared
    /// ball every frame and plays its landing outros; it never feeds back into play.
    /// </summary>
    public sealed class SlopballBall : MonoBehaviour
    {
        public enum Mood { Grin, Wince, Yikes, Laugh, Ko }
        private enum Outro { None, Score, Miss, Fizzle }

        [Tooltip("Turns toward the camera; its +Z is the painted front.")]
        public Transform Facing;
        [Tooltip("Rotated to the squash axis in the facing plane and scaled along it.")]
        public Transform Squash;
        [Tooltip("Undoes Squash's rotation so the face stays upright while the body squashes.")]
        public Transform Unsquash;
        public Transform Model;
        public Renderer ModelRenderer;
        public Renderer FlashRenderer;
        [Tooltip("Faces in Mood order: grin, wince, yikes, laugh, ko.")]
        public Material[] Moods = new Material[5];
        public TrailRenderer Trail;
        [Tooltip("The radius the prefab is built for; Place scales it to the court's ball.")]
        public float BuiltRadius = .65f;
        public float TrailSpeed = 9f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private const float Stiffness = 320f, Damping = 15f, TrailSeconds = .2f, CurveTrailSeconds = .6f;
        private MaterialPropertyBlock _block;
        private Transform _viewer;
        private Vector3 _velocity;
        private Mood _mood = Mood.Grin, _shown = (Mood)(-1);
        private float _wince, _flash, _flashSeconds, _shake, _squash, _squashSpeed, _squashAngle, _radius = .65f;
        private float _outroAge;
        private Outro _outro;
        private Vector3 _outroFloor;
        private Color _trailColor = new(1f, .42f, .29f), _curveColor;
        private bool _curving;

        public bool InOutro => _outro != Outro.None;
        public Mood CurrentMood => _shown;

        public void Init(Transform viewer)
        {
            _viewer = viewer;
            _block = new MaterialPropertyBlock();
            SetFlash(0f);
            if (Trail != null) Trail.emitting = false;
        }

        /// <summary>Follows the Shared ball: centre, radius and velocity (for stretch, lean and trail).</summary>
        public void Place(Vector3 centre, float radius, Vector3 velocity)
        {
            if (_outro != Outro.None) return;
            _radius = radius;
            _velocity = velocity;
            transform.position = centre;
            transform.localScale = Vector3.one * (radius / Mathf.Max(.01f, BuiltRadius));
        }

        /// <summary>A brand-new ball: no trail from the last landing spot, a clean face.</summary>
        public void Respawn(Vector3 centre, float radius, Vector3 velocity)
        {
            _outro = Outro.None;
            _curving = false;
            _wince = _flash = _shake = 0f;
            _squash = _squashSpeed = 0f;
            gameObject.SetActive(true);
            Place(centre, radius, velocity);
            if (Trail != null) Trail.Clear();
            Kick(velocity, -.35f);
        }

        public void SetMood(Mood mood) => _mood = mood;

        public void SetTrailColor(Color color) => _trailColor = color;

        /// <summary>A curveball streaks in <paramref name="color"/> at any speed while it bends, so the curve reads.</summary>
        public void SetCurving(bool curving, Color color) { _curving = curving; _curveColor = color; }

        /// <summary>A hit: white flash and a shiver through hitstop, a squash along the hit, then a wince.</summary>
        public void Hit(Vector3 direction, float strength01, float hitstopSeconds)
        {
            _flashSeconds = Mathf.Max(.07f, hitstopSeconds + .05f);
            _flash = 1f;
            _shake = hitstopSeconds;
            _wince = hitstopSeconds + .22f;
            Kick(direction, .2f + .22f * strength01);
        }

        /// <summary>A bounce off the cage, net or a fighter: a quick squash along the surface normal.</summary>
        public void Bounce(Vector3 normal, float strength01) => Kick(normal, .1f + .2f * strength01);

        /// <summary>Lands for good: a laugh into the score splat, an X_X pancake on a miss, or a quiet fizzle.</summary>
        public void PlayOutro(bool scored, bool fizzle, Vector3 floor)
        {
            _outro = fizzle ? Outro.Fizzle : scored ? Outro.Score : Outro.Miss;
            _outroAge = 0f;
            _outroFloor = floor;
            _shake = _flash = 0f;
            SetFlash(0f);
            if (Trail != null) Trail.emitting = false;
        }

        public void Hide()
        {
            _outro = Outro.None;
            gameObject.SetActive(false);
        }

        /// <summary>Positive squashes along the direction (projected into the facing plane), negative stretches.</summary>
        private void Kick(Vector3 direction, float amount)
        {
            if (Facing == null || direction.sqrMagnitude < 1e-6f) return;
            var local = Facing.InverseTransformDirection(direction);
            if (local.x * local.x + local.y * local.y > 1e-4f)
                _squashAngle = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
            _squash = Mathf.Clamp(amount, -.45f, .45f);
            _squashSpeed = 0f;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (_outro != Outro.None) { AnimateOutro(dt); return; }

            Vector3 toViewer = _viewer != null ? _viewer.position - transform.position : Vector3.back;
            if (toViewer.sqrMagnitude > 1e-4f)
            {
                // Lean the face a little toward where the ball is going.
                var forward = toViewer.normalized;
                float speed = _velocity.magnitude;
                if (speed > .5f)
                    forward = Vector3.RotateTowards(forward, _velocity / speed, Mathf.Deg2Rad * Mathf.Min(22f, speed * 1.6f), 0f);
                Facing.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }

            // Spring toward a slight stretch along the flight at speed; hits and bounces kick it.
            float speedStretch = -Mathf.Clamp01((_velocity.magnitude - 6f) / 30f) * .14f;
            if (_shake <= 0f)
            {
                _squashSpeed += (-Stiffness * (_squash - speedStretch) - Damping * _squashSpeed) * dt;
                _squash += _squashSpeed * dt;
            }
            if (_velocity.sqrMagnitude > 4f && Mathf.Abs(_squash - speedStretch) < .03f)
            {
                var local = Facing.InverseTransformDirection(_velocity);
                if (local.x * local.x + local.y * local.y > 1e-4f)
                    _squashAngle = Mathf.LerpAngle(_squashAngle, Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg, 1f - Mathf.Exp(-dt * 12f));
            }
            ApplySquash(_squash);

            // Hitstop: a model-level shiver (no camera shake) and a white flash that fades out.
            _shake = Mathf.Max(0f, _shake - dt);
            Model.localPosition = _shake > 0f ? Random.insideUnitSphere * .05f : Vector3.zero;
            if (_flash > 0f)
            {
                _flash = Mathf.Max(0f, _flash - dt / _flashSeconds);
                SetFlash(_flash);
            }

            _wince = Mathf.Max(0f, _wince - dt);
            ShowMood(_wince > 0f ? Mood.Wince : _mood);

            if (Trail != null)
            {
                Trail.emitting = (_curving || _velocity.magnitude >= TrailSpeed) && _shake <= 0f;
                Trail.widthMultiplier = _radius * (_curving ? 1.3f : 1.7f);
                // A curveball keeps a longer streak so the bend shows above it as it drops in; eased so the tail doesn't snap off.
                Trail.time = Mathf.MoveTowards(Trail.time, _curving ? CurveTrailSeconds : TrailSeconds, dt);
                var color = _curving ? _curveColor : _trailColor;
                Trail.startColor = color;
                Trail.endColor = new Color(color.r, color.g, color.b, 0f);
            }
        }

        private void AnimateOutro(float dt)
        {
            _outroAge += dt;
            float r = _radius;
            float sx, sy, alive;
            switch (_outro)
            {
                case Outro.Score:
                    // Laughs, squashes into the splat and melts away.
                    ShowMood(Mood.Laugh);
                    sx = 1f + .35f * Mathf.Clamp01(_outroAge / .08f);
                    sy = 1f - .45f * Mathf.Clamp01(_outroAge / .08f);
                    alive = 1f - Mathf.Clamp01((_outroAge - .3f) / .3f);
                    break;
                case Outro.Miss:
                    // Pancakes flat with X_X, lies there a beat, then shrinks.
                    ShowMood(Mood.Ko);
                    float hit = Mathf.Clamp01(_outroAge / .06f);
                    sx = 1f + .5f * hit;
                    sy = 1f - .58f * hit;
                    alive = 1f - Mathf.Clamp01((_outroAge - .55f) / .25f);
                    break;
                default:
                    sx = sy = 1f;
                    alive = 1f - Mathf.Clamp01(_outroAge / .25f);
                    break;
            }
            if (alive <= 0f) { Hide(); return; }
            // Keep the bottom on the floor while the body flattens in the facing plane.
            if (_viewer != null)
            {
                var toViewer = _viewer.position - _outroFloor;
                toViewer.y = 0f;
                if (toViewer.sqrMagnitude > 1e-4f) Facing.rotation = Quaternion.LookRotation(toViewer.normalized, Vector3.up);
            }
            Squash.localRotation = Quaternion.identity;
            Unsquash.localRotation = Quaternion.identity;
            Squash.localScale = new Vector3(sx * alive, sy * alive, alive);
            transform.position = _outroFloor + Vector3.up * r * sy * alive;
            Model.localPosition = Vector3.zero;
        }

        private void ApplySquash(float amount)
        {
            // Positive squashes along the axis and widens across it, roughly keeping volume.
            Squash.localRotation = Quaternion.Euler(0f, 0f, _squashAngle);
            Squash.localScale = new Vector3(1f - amount, 1f + amount * .6f, 1f + amount * .3f);
            Unsquash.localRotation = Quaternion.Euler(0f, 0f, -_squashAngle);
        }

        private void ShowMood(Mood mood)
        {
            if (mood == _shown || Moods == null || (int)mood >= Moods.Length || Moods[(int)mood] == null) return;
            _shown = mood;
            ModelRenderer.sharedMaterial = Moods[(int)mood];
        }

        private void SetFlash(float amount)
        {
            if (FlashRenderer == null) return;
            FlashRenderer.enabled = amount > 0f;
            if (amount <= 0f || _block == null) return;
            FlashRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, new Color(1.6f, 1.6f, 1.6f, Mathf.SmoothStep(0f, .82f, amount)));
            FlashRenderer.SetPropertyBlock(_block);
        }
    }
}
