#nullable enable
using System.Collections.Generic;

namespace SlopArena.Client.Workshop
{
    public sealed class ModelSwapSession
    {
        private ModelSwapChoice? _beforePreview;
        private bool _preview;
        public void Preview(IDictionary<int, ModelSwapChoice> choices, ModelSwapChoice choice)
        {
            EndPreview(choices);
            choices.TryGetValue(1, out _beforePreview);
            _preview = true;
            choices[1] = choice;
        }
        public void EndPreview(IDictionary<int, ModelSwapChoice> choices)
        {
            if (!_preview) return;
            if (_beforePreview == null) choices.Remove(1); else choices[1] = _beforePreview;
            _beforePreview = null; _preview = false;
        }
        public static ModelSwapChoice? ForFighter(IReadOnlyDictionary<int, ModelSwapChoice> choices, int entity, bool online, ulong localEntity)
        {
            int seat = online ? entity == (long)localEntity ? 1 : 0 : entity;
            return choices.TryGetValue(seat, out var choice) ? choice : null;
        }
    }
}
