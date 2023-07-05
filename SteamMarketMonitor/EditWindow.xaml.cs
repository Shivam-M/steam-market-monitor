using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SteamMarketMonitor {

    public partial class EditWindow : Window {

        private readonly MainWindow _mainWindow;
        private readonly Item _item;

        private readonly Slider _profitSlider;
        private readonly CheckBox _profitBox;
        private readonly TextBox _priceValue;
        private readonly Label _sliderValue;
        private readonly Label _newPrice;
        private readonly Label _priceLabel;

        private readonly string _originalPrice;
        private readonly double _originalThreshold;
        private double _profitPercentage = 0;
        private bool _okTrigger = false;
        private string _lastPriceInput = string.Empty;

        private const string REGEX_PRICE = @"^[0-9.]+$";

        public EditWindow(MainWindow mw, ref Item i) {
            InitializeComponent();

            Title = "Edit: " + i.Name;
            Background = new SolidColorBrush(Color.FromRgb(236, 240, 241));
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = mw;

            _mainWindow = mw;
            _item = i;

            _originalPrice = _item.Price;
            _originalThreshold = _item.Threshold;

            _profitSlider = FindName("Slider") as Slider;
            _sliderValue = FindName("SliderValue") as Label;
            _priceValue = FindName("PriceValue") as TextBox;
            _newPrice = FindName("NewPrice") as Label;
            _profitBox = FindName("ProfitBox") as CheckBox;
            _priceLabel = FindName("PriceLabel") as Label;

            _priceValue.Text = i.Price.Remove(0, 1);
            _priceLabel.Content = "Price:  " + mw.GetCurrency();
            _lastPriceInput = _priceValue.Text;

            if (i.Threshold >= 0) {
                _profitSlider.Value = i.Threshold / 50;
                _profitBox.IsChecked = true;
                _profitSlider.IsEnabled = true;
            } else {
                _profitBox.IsChecked = false;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

        private void OkButton_Click(object sender, RoutedEventArgs e) {
            _okTrigger = true;
            _item.Price = _mainWindow.FormatPrice(double.Parse(_priceValue.Text.ToString()));
            Close();
        }

        protected override void OnClosing(CancelEventArgs e) {
            if (!_okTrigger) {
                _item.Threshold = _originalThreshold;
                _item.Price = _originalPrice;
            } base.OnClosing(e);
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e) {
            if (_priceValue == null || (_priceValue.Text == string.Empty)) {
                _lastPriceInput = string.Empty;
                return;
            }
            int selectionStart = _priceValue.SelectionStart;
            if (!Regex.IsMatch(_priceValue.Text, REGEX_PRICE)) {
                _priceValue.Text = _lastPriceInput;
                _priceValue.SelectionStart = selectionStart - 1;
            }
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            double currentPrice = double.Parse(_priceValue.Text);
            double updatedPrice = Math.Round(currentPrice + (currentPrice * (e.NewValue * 50 / 100.0)), 2);
            _profitPercentage = (int)(e.NewValue * 50);
            _sliderValue.Content = _profitPercentage + "%";
            _newPrice.Content = $"{_mainWindow.GetCurrency()}{updatedPrice:0.00}";
            _item.Threshold = _profitPercentage;
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e) {
            _item.Threshold = _profitPercentage;
            _profitSlider.IsEnabled = true;
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e) {
            _profitSlider.IsEnabled = false;
            _item.Threshold = -1;
        }

    }
}
