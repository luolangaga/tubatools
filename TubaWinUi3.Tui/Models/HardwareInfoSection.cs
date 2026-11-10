using System.Collections.Generic;

namespace TubaWinUi3.Tui.Models
{
    public sealed class HardwareInfoSection
    {
        public string Title { get; set; }
        public List<HardwareInfoItem> Items { get; } = new List<HardwareInfoItem>();

        public HardwareInfoSection()
        {
            Title = "";
        }
    }
}
