namespace TubaWinUi3.Tui.Models
{
    public sealed class HardwareInfoItem
    {
        public string Label { get; set; }
        public string Value { get; set; }

        public HardwareInfoItem()
        {
            Label = "";
            Value = "";
        }
    }
}
