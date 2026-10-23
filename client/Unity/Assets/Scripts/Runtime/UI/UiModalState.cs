namespace SlopArena.Client.UI
{
    /// <summary>
    /// Modal owners must pair Push and Pop so obscured conversations remain unread.
    /// </summary>
    public static class UiModalState
    {
        private static int _depth;

        /// <summary>True while at least one modal surface is presented.</summary>
        public static bool Presented => _depth > 0;

        public static void Push() => _depth++;

        public static void Pop() => _depth = System.Math.Max(0, _depth - 1);
    }
}
