namespace SlopArena.Shared
{
    /// <summary>
    /// Named constants for the keyboard-first move-slot layout (ADR-0016, issue #116).
    /// These are the <see cref="InputState.ActiveSlot"/> wire values (1-11).
    /// <see cref="CanonicalSlotProjection"/> maps the supported values to the package grid.
    ///
    /// Key mapping (default AZERTY/ZQSD-physical, remappable client-side):
    ///   1 = LMB     4 = E       7 = retired    10 = retired
    ///   2 = RMB     5 = R       8 = key "2"    11 = key "A"
    ///   3 = key "1" 6 = F       9 = key "3"
    /// Three normals since ADR-0042: Normal1 (jab/all-rounder), Normal2 (up), Normal3 (big),
    /// numbered as players press them. Wire values keep their historical indices.
    /// </summary>
    public static class AbilitySlots
    {
        public const byte None = 0;
        public const byte Lmb = 1;      // reserved retired mouse slot
        public const byte Rmb = 2;      // reserved retired mouse slot
        public const byte Normal1 = 3;  // key "1" (formerly Q)
        public const byte E = 4;
        public const byte R = 5;
        public const byte F = 6;
        public const byte RetiredNormal = 7;       // merged away by ADR-0042
        public const byte Normal2 = 8;             // key "2"
        public const byte Normal3 = 9;             // key "3"
        public const byte RetiredFifthNormal = 10; // retired fifth normal
        public const byte A = 11;       // key "A"

        /// <summary>Number of move slots (0-10 slot indices, ActiveSlot values 1-11).</summary>
        public const int Count = 11;

    }
}
