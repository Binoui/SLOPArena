using System;
using System.Collections.Generic;
using SlopArena.Client.UI;
using SlopArena.Shared;
using Unity.Cinemachine;
using UnityEngine;

namespace SlopArena.Client.Camera
{
    [DefaultExecutionOrder(80)]
    public sealed class CourseLeaderCamera : MonoBehaviour
    {
        private CameraMount _mount;
        private CinemachineCamera _camera;
        private CinemachineOrbitalFollow _orbit;
        private CinemachineInputAxisController _axes;
        private Func<ServerSimulation> _simulation;
        private IReadOnlyDictionary<ulong, Transform> _racers;
        private CharacterCourseDefinition _course;
        private Transform _focus;
        private float _radius, _groundY, _yaw;
        private bool _initialized, _turning;
        public void Initialize(CameraMount mount, Func<ServerSimulation> simulation, IReadOnlyDictionary<ulong, Transform> racers, CharacterCourseDefinition course)
        {
            _mount = mount; _simulation = simulation; _racers = racers; _course = course;
            _camera = mount.GetComponent<CinemachineCamera>(); _orbit = mount.GetComponent<CinemachineOrbitalFollow>();
            _axes = mount.GetComponent<CinemachineInputAxisController>();
            if (_axes != null) _axes.enabled = false;
            _radius = _orbit.Radius; mount.enabled = false;
            _focus = new GameObject("Course leader focus").transform; _focus.SetParent(transform);
            _camera.Target = new CameraTarget { TrackingTarget = _focus, LookAtTarget = _focus }; ResetView();
        }
        public void ResetView() { _initialized = false; _turning = false; }
        private void LateUpdate()
        {
            if (_mount == null || _focus == null) return;
            var sim = _simulation(); ulong id = sim.Scramble?.LeaderEntityId ?? 0;
            if (id == 0) id = 1;
            if (!_racers.TryGetValue(id, out var rendered)) return;
            var leader = sim.GetState(id); var p = rendered.position;
            if (!_initialized) { _groundY = p.y; _yaw = leader.FacingYaw * Mathf.Rad2Deg; _initialized = true; }
            float ground = leader.IsGrounded ? p.y : _groundY - p.y > 3f ? p.y + 3f : _groundY;
            _groundY = Mathf.Lerp(_groundY, ground, 1f - Mathf.Exp(-Time.deltaTime));
            int next = sim.CourseFor(id).Value.SavedCheckpoint + 1;
            var marker = next < _course.Checkpoints.Count ? _course.Checkpoints[next].Volume.Center : _course.Finish.Center;
            var direction = new Vector3(marker.X - p.x, 0, marker.Z - p.z);
            if (direction.sqrMagnitude > 9f)
            {
                float wanted = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                float error = Mathf.DeltaAngle(_yaw, wanted);
                if (Mathf.Abs(error) > 60f) _turning = true;
                if (_turning)
                {
                    _yaw = Mathf.MoveTowardsAngle(_yaw, wanted - Mathf.Sign(error) * 24f,
                        ClientSettingsService.Instance.CameraTurnSpeed * Time.deltaTime);
                    if (Mathf.Abs(error) <= 24.5f) _turning = false;
                }
            }
            var focus = new Vector3(p.x, _groundY + 1f, p.z);
            var rotation = Quaternion.Euler(35f, _yaw, 0);
            float tanY = Mathf.Tan(_camera.Lens.FieldOfView * .5f * Mathf.Deg2Rad);
            float tanX = tanY * (_mount.RenderCamera != null ? _mount.RenderCamera.aspect : 16f / 9f);
            float distance = _radius;
            foreach (var racer in _racers)
            {
                if (sim.IsEntityInactive(racer.Key) && racer.Key != id || CourseScrambleTuning.OutsideLeash(leader, sim.GetState(racer.Key))) continue;
                var local = Quaternion.Inverse(rotation) * (racer.Value.position + Vector3.up - focus);
                distance = Mathf.Max(distance, (Mathf.Abs(local.x) + 2f) / tanX - local.z,
                    (Mathf.Abs(local.y) + 2f) / tanY - local.z);
            }
            _focus.position = focus;
            _mount.SetCameraYawDeg(_yaw); _mount.SetCameraPitchDeg(35f); _orbit.RadialAxis.Value = 1f;
            _orbit.Radius = Mathf.Lerp(_orbit.Radius, distance, 1f - Mathf.Exp(-4f * Time.deltaTime));
        }
        private void OnDestroy()
        {
            if (_mount == null) return;
            _mount.enabled = true;
            if (_axes != null) _axes.enabled = true;
        }
    }
}
