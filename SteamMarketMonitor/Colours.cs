using Colour = System.Drawing.Color;
using System.Windows.Media;


namespace SteamMarketMonitor {
    internal class Colours {

        public static readonly SolidColorBrush HINT_FG = new SolidColorBrush(Color.FromRgb(127, 140, 141));
        public static readonly SolidColorBrush MAIN_BG = new SolidColorBrush(Color.FromRgb(236, 240, 241));
        public static readonly Colour MAIN_FG;

        public static readonly Colour EDIT_BG;
        public static readonly Colour EDIT_FG;

        public static readonly Colour MENU_BG = Colour.FromArgb(45, 52, 54);
        public static readonly Colour MENU_FG = Colour.FromArgb(223, 230, 233);
        
    }
}
