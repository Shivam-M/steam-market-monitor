using System;

namespace SteamMarketMonitor {
    public class Item {
        public Item() {}

        public string Name { get; set; }
        public string Price { get; set; }
        public double Threshold { get; set; }

        public string MedianPrice { get; set; } = "£0.00";

        public string LowestPrice { get; set; } = "£0.00";

        public int Change { get; set; } = 0;

        public string PercentageLowest { get; set; }

        public string PercentageMedian { get; set; }

        public double ChangeLowest { get; set; } = 0;
        public double ChangeMedian { get; set; } = 0;

        public bool Notified { get; set; } = false;

        public void CalculateChange() {
            ChangeLowest = (GetLowestPrice() - GetPrice()) / GetPrice();
            ChangeMedian = (GetMedianPrice() - GetPrice()) / GetPrice();
            PercentageLowest = $"{(int)(ChangeLowest * 100)}%";
            PercentageMedian = $"{(int)(ChangeMedian * 100)}%";
            // Change = (int)(Math.Abs(ChangeLowest) / 0.25) - 1 // + <=;
            if (Math.Abs(ChangeLowest) <= 0.25) Change = 0;
            else if (Math.Abs(ChangeLowest) <= 0.50) Change = 1;
            else if (Math.Abs(ChangeLowest) <= 0.75) Change = 2;
            else Change = 3;
            if (ChangeLowest < 0) Change *= -1;
        }

        public double GetPrice() => double.Parse(Price[1..]);

        public double GetMedianPrice() => double.TryParse(MedianPrice[1..], out double priceValue) ? priceValue : 0;

        public double GetLowestPrice() => double.TryParse(LowestPrice[1..], out double priceValue) ? priceValue : 0;

    }
}
