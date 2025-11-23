namespace biped
{
    public static class CustomButtons
    {
        public const uint MouseLeft = 0xFF01;
        public const uint MouseMiddle = 0xFF02;
        public const uint MouseRight = 0xFF03;
    }
    public static class ModifierMasks
    {
        // We use the upper bits for modifiers
        public const uint SHIFT = 0x10000;
        public const uint CTRL = 0x20000;
        public const uint ALT = 0x40000;
        public const uint WIN = 0x80000;

        // The lower 16 bits are for the actual key code
        public const uint KEY_MASK = 0xFFFF;
    }
}