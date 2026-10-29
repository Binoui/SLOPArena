#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using SlopArena.Client.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SlopArena.EditorTools
{
    public sealed class VirtualPadTimeline : IDisposable
    {
        public static bool Active { get; private set; }
        private readonly Gamepad _pad;
        private readonly IReadOnlyList<GamepadState> _frames;
        private readonly Func<int> _frame;
        private readonly bool _wasGamepad;
        private int _lastUnityFrame = -1;
        private bool _disposed;

        public VirtualPadTimeline(IReadOnlyList<GamepadState> frames, Func<int> frame)
        {
            if (Active || InputSystem.settings.updateMode != InputSettings.UpdateMode.ProcessEventsInDynamicUpdate)
                throw new InvalidOperationException("A free virtual pad and Dynamic Input System updates are required.");
            _frames = frames; _frame = frame;
            _wasGamepad = HumanInputActions.LastUsedGamepad;
            _pad = InputSystem.AddDevice<Gamepad>("Move Timeline");
            Active = true;
            InputSystem.onBeforeUpdate += Feed;
        }

        private void Feed()
        {
            if (!_pad.added || UnityEngine.InputSystem.LowLevel.InputState.currentUpdateType != InputUpdateType.Dynamic
                || _lastUnityFrame == Time.frameCount) return;
            _lastUnityFrame = Time.frameCount;
            int index = _frame();
            InputSystem.QueueStateEvent(_pad, index >= 0 && index < _frames.Count ? _frames[index] : default);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            InputSystem.onBeforeUpdate -= Feed;
            if (_pad.added) InputSystem.RemoveDevice(_pad);
            HumanInputActions.LastUsedGamepad = _wasGamepad;
            Active = false;
        }
    }
}
#endif
