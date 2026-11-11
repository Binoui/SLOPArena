using System;

namespace SlopArena.Client.UI
{
    public sealed class MenuChoice
    {
        private double _lastSubmit = double.NegativeInfinity;
        public int Count { get; }
        public int Index { get; private set; }
        public MenuChoice(int count, int selected = 0)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            Count = count; Index = count == 0 ? -1 : Math.Min(Math.Max(selected, 0), count - 1);
        }
        public bool Submit(double time)
        {
            if (time - _lastSubmit < .2) return false;
            _lastSubmit = time; Move(1); return true;
        }
        public void Move(int direction) => Index = NextIndex(Index, Count, direction);
        public static int NextIndex(int current, int count, int direction)
        {
            if (count <= 0) return -1;
            if (current < 0 || current >= count) return direction < 0 ? count - 1 : 0;
            return ((current + direction) % count + count) % count;
        }
    }
}
