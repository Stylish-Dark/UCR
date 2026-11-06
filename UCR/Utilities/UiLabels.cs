namespace HidWizards.UCR.Utilities
{
    /// <summary>
    /// Canonical UI vocabulary. This index is the single source of truth for
    /// shared action labels. Do not add descriptive prefixes to already
    /// identifiable device controls (for example, "Face button X").
    /// This is presentation text only; persistence identifiers and device
    /// protocol descriptors must not be translated.
    /// </summary>
    public static class UiLabels
    {
        public const string Bind = "Bind";
        public const string Not = "NOT";
        public const string Block = "Block";
        public const string Output = "Output";
        public const string Input = "Input";
        public const string Axis = "Axis";
        public const string Button = "Button";
        public const string FourWay = "4-Way";
        public const string Filter = "Filter";
        public const string Capture = "●  PRESS INPUT";
        public const string CustomFourWay = "Custom 4-Way";
        public const string LeftStick = "Left stick";
        public const string RightStick = "Right stick";
        public const string LeftTrigger = "Left trigger";
        public const string RightTrigger = "Right trigger";
        public const string LeftShoulder = "Left shoulder";
        public const string RightShoulder = "Right shoulder";
    }
}
