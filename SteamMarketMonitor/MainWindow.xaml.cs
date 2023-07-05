using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Forms = System.Windows.Forms;
using Microsoft.Win32;

namespace SteamMarketMonitor {

    public partial class MainWindow : Window {

        private const string ICON_FILE = "Resources/smm.ico";
        private const string DATA_FILE = "Resources/data.json";
        private const string CONF_FILE = "Resources/settings.xml";
        private const string CACH_FILE = "cached_items.json";
        private readonly string CACHE_DIR = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), WINDOW_TITLE, "Cache");

        private const string PLACEHOLDER_URL = "Example: https://steamcommunity.com/market/listings/730/P250%20%7C%20Sand%20Dune%20%28Field-Tested%29";
        private const string REGEX_URL = @"https:\/\/steamcommunity\.com\/market\/listings\/\d+\/.*";
        private const string STARTUP_PATH = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string WINDOW_TITLE = "Steam Market Monitor";
        private const string PROGRAM_VERSION = "v1.0.0";
        private const string API_KEY = "";
        private readonly char[] CURRENCIES = { '$', '$', '£', '€' }; // USD, CAD, GBP, EUR

        private readonly Forms.ToolStripMenuItem _startupCheckbox;
        private readonly Forms.NotifyIcon _notifyIcon;
        private readonly TextBox _textBox;
        private readonly TextBox _priceBox;
        private readonly ListView _listView;
        private readonly Label _textUpdate;
        private readonly Label _currencyIndicator;
        private readonly Button _removeButton;
        private readonly Button _editButton;

        private readonly XmlDocument _configFile = new XmlDocument();
        
        private int _currencyID = 1;
        private string _lastHeader = "";

        private readonly List<Item> _items = new List<Item> { };
        private JObject _savedItems;
        private JObject _cachedItems = new JObject();

        public MainWindow() {
            InitializeComponent();

            Title = WINDOW_TITLE;

            _configFile.Load(CONF_FILE);
            _listView = FindName("Items") as ListView;
            _textBox = FindName("EnterURL") as TextBox;
            _priceBox = FindName("EnterPrice") as TextBox;
            _textUpdate = FindName("UpdateText") as Label;
            _currencyIndicator = FindName("CurrencyIndicator") as Label;
            _editButton = FindName("EditButton") as Button;
            _removeButton = FindName("RemoveButton") as Button;

            _notifyIcon = new Forms.NotifyIcon { Icon = new Icon(ICON_FILE), Visible = true, Text = WINDOW_TITLE, ContextMenuStrip = new Forms.ContextMenuStrip() };
            _notifyIcon.Click += NotifyIcon_Click;

            Forms.ContextMenuStrip CMS = _notifyIcon.ContextMenuStrip;
            CMS.Items.Add("Open", null, (_, __) => PresentWindow());
            CMS.Items.Add("Refresh", null, async (_, __) => await UpdatePrices());
            CMS.Items.Add("Change Currency...");
            CMS.Items.Add("Open on Startup", null, (_, __) => ToggleStartup());
            CMS.Items.Add(new Forms.ToolStripSeparator());
            CMS.Items.Add(WINDOW_TITLE + " " + PROGRAM_VERSION).Enabled = false;
            CMS.Items.Add("Exit", null, ExitProgram);

            Forms.ToolStripMenuItem currencyChanger = CMS.Items[2] as Forms.ToolStripMenuItem;
            currencyChanger.DropDownItems.Add("USD - $", null, (sender, e) => CheckID(0));
            currencyChanger.DropDownItems.Add("CAD - $", null, (sender, e) => CheckID(1));
            currencyChanger.DropDownItems.Add("GBP - £", null, (sender, e) => CheckID(2));
            currencyChanger.DropDownItems.Add("EUR - €", null, (sender, e) => CheckID(3));

            for (int x = 0; x < _notifyIcon.ContextMenuStrip.Items.Count; x++) {
                if (!(CMS.Items[x] is Forms.ToolStripMenuItem menuItem)) continue;
                menuItem.BackColor = Colours.MENU_BG;
                menuItem.ForeColor = Colours.MENU_FG;
                menuItem.MouseEnter += TSMI_MouseEnter;
                menuItem.MouseLeave += TSMI_MouseLeave;
            }

            for (int y = 0; y < (CMS.Items[2] as Forms.ToolStripMenuItem).DropDownItems.Count; y++) {
                currencyChanger.DropDownItems[y].BackColor = Colours.MENU_BG;
                currencyChanger.DropDownItems[y].ForeColor = Colours.MENU_FG;
            }

            _startupCheckbox = (CMS.Items[3] as Forms.ToolStripMenuItem);
            _startupCheckbox.Checked = bool.Parse(_configFile.DocumentElement.SelectSingleNode("/settings/startup").InnerText);

            (CMS.Items[4] as Forms.ToolStripSeparator).Paint += TSS_Paint;

            _textBox.GotFocus += RemoveText;
            _textBox.LostFocus += AddText;
            _listView.ItemsSource = _items;

            _currencyID = int.Parse(_configFile.DocumentElement.SelectSingleNode("/settings/currency").InnerText);

            Loaded += OnLoaded;
            LoadItems();
            LoadCache();
            CheckID(_currencyID);
            UpdateData();
            AddText(this, null);
        }

        private void TSS_Paint(object sender, Forms.PaintEventArgs e) {
            Forms.ToolStripSeparator sep = sender as Forms.ToolStripSeparator;
            e.Graphics.FillRectangle(new SolidBrush(Colours.MENU_BG), 0, 0, sep.Width, sep.Height);
            e.Graphics.DrawLine(new System.Drawing.Pen(Colours.MENU_FG), 30, sep.Height / 2, sep.Width - 4, sep.Height / 2);
        }

        private void TSMI_MouseEnter(object sender, EventArgs e) => (sender as Forms.ToolStripMenuItem).ForeColor = Colours.MENU_BG;

        private void TSMI_MouseLeave(object sender, EventArgs e) => (sender as Forms.ToolStripMenuItem).ForeColor = Colours.MENU_FG;

        private async void OnLoaded(object sender, RoutedEventArgs e) {
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            await RegularlyCheck(TimeSpan.FromSeconds(int.Parse(_configFile.DocumentElement.SelectSingleNode("/settings/interval").InnerText)), cancellationTokenSource.Token);
        }

        // Has little use -> make it time-based for long refresh intervals?
        private void LoadCache() {
            Directory.CreateDirectory(CACHE_DIR);
            string cacheFile = Path.Combine(CACHE_DIR, CACH_FILE);

            if (!File.Exists(cacheFile)) {
                using StreamWriter file = File.CreateText(cacheFile);
                _cachedItems["items"] = new JObject();
                new JsonSerializer().Serialize(file, _cachedItems);
            } else {
                _cachedItems = JObject.Parse(File.ReadAllText(cacheFile));
                foreach (Item item in _items) {
                    JToken cachedItem = _cachedItems["items"][item.Name];
                    if (cachedItem == null) continue;
                    item.LowestPrice = cachedItem["lowest"].ToString();
                    item.MedianPrice = cachedItem["median"].ToString();
                    item.CalculateChange();
                }
                _listView.Items.Refresh();
            }
        }

        private void SaveCache() {
            using StreamWriter cacheFile = File.CreateText(Path.Combine(CACHE_DIR, CACH_FILE));
            new JsonSerializer().Serialize(cacheFile, _cachedItems);
        }

        private void CacheItem(Item item) {
            _cachedItems["items"][item.Name] ??= new JObject();
            _cachedItems["items"][item.Name]["lowest"] = item.LowestPrice;
            _cachedItems["items"][item.Name]["median"] = item.MedianPrice;
            SaveCache();
        }

        private bool CheckCache(ref Item item) {
            if (_cachedItems["items"][item.Name] == null) return false;
            item.LowestPrice = _cachedItems["items"][item.Name]["lowest"].ToString();
            item.MedianPrice = _cachedItems["items"][item.Name]["median"].ToString();
            return true;
        }

        private void ToggleStartup() {
            _startupCheckbox.Checked = !_startupCheckbox.Checked;
            _configFile.DocumentElement.SelectSingleNode("/settings/startup").InnerText = _startupCheckbox.Checked.ToString();
            _configFile.Save(CONF_FILE);
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(STARTUP_PATH, true);
            if (_startupCheckbox.Checked)
                key.SetValue(WINDOW_TITLE, Environment.GetCommandLineArgs()[0]);
            else {
                key.DeleteValue(WINDOW_TITLE, false);
            }
        }

        private async void CheckID(int id) {
            foreach (Forms.ToolStripMenuItem item in (_notifyIcon.ContextMenuStrip.Items[2] as Forms.ToolStripMenuItem).DropDownItems) item.Checked = false;
            ((Forms.ToolStripMenuItem)(_notifyIcon.ContextMenuStrip.Items[2] as Forms.ToolStripMenuItem).DropDownItems[id]).Checked = true;
            _currencyID = id;
            _currencyIndicator.Content = GetCurrency();
            _configFile.DocumentElement.SelectSingleNode("/settings/currency").InnerText = id.ToString();
            _configFile.Save(CONF_FILE);
            await UpdatePrices();
        }

        private bool ItemExists(string name) => _items.Any(i => i.Name.Equals(name));

        public string FormatPrice(double price) => GetCurrency() + Math.Round(RoundSF(price, 4), 4).ToString("F2");

        private void LoadItems() {
            _savedItems = JObject.Parse(File.ReadAllText(DATA_FILE));
            _items.AddRange(from JObject data in _savedItems["items"]
                            select new Item() { Name = data["item"].ToString(), Price = FormatPrice((double)data["price"]), Threshold = (double)data["threshold"] });
        }

        private async Task UpdatePrices(bool background = false) {
            Title = WINDOW_TITLE + " - Loading...";
            Mouse.OverrideCursor = Cursors.AppStarting;
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            foreach (Item item in _items) {
                await UpdatePrice(item, client);
                if (!background) _listView.Items.Refresh();
            }
            _textUpdate.Content = "Last Updated: " + DateTime.Now.ToString(@"dd\/MM\/yyyy HH:mm:ss");

            Title = WINDOW_TITLE;
            
            int reachedThreshold = 0;
            foreach (Item item in _items)
                if (!item.Notified && item.Threshold >= item.ChangeLowest * 100 && item.Threshold > 0 && item.ChangeLowest > 0) {
                    reachedThreshold++;
                    item.Notified = true;
                }
            if (reachedThreshold == 1) _notifyIcon.ShowBalloonTip(3000, WINDOW_TITLE, $"You have 1 item that is now valued above its set threshold.", Forms.ToolTipIcon.Info);
            else if (reachedThreshold >= 1) _notifyIcon.ShowBalloonTip(3000, WINDOW_TITLE, $"You have {reachedThreshold} item(s) that are now valued above their set threshold.", Forms.ToolTipIcon.Info);
            Mouse.OverrideCursor = Cursors.Arrow;
        }

        private async void UpdatePrice(Item item) {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            await UpdatePrice(item, client);
        }

        // Currently not using the Steam API, so this is heavily rate-limited.
        private async Task UpdatePrice(Item item, HttpClient client) {
            HttpResponseMessage responseMsg = await client.GetAsync($"https://steamcommunity.com/market/priceoverview/?appid=730&currency={_currencyID}&market_hash_name={HttpUtility.UrlEncode(item.Name)}");
            if (responseMsg.StatusCode != System.Net.HttpStatusCode.OK) return;
            string responseString = await responseMsg.Content.ReadAsStringAsync();
            JObject responseData = JObject.Parse(responseString);
            item.LowestPrice = responseData["lowest_price"]?.ToString() ?? "N/A";
            item.MedianPrice = responseData["median_price"]?.ToString() ?? "N/A";
            item.CalculateChange();
            CacheItem(item);
        }

        public async Task RegularlyCheck(TimeSpan interval, CancellationToken cancellationToken) {
            while (true) {
                await UpdatePrices(true);
                await Task.Delay(interval, cancellationToken);
            }
        }

        private void RemoveText(object sender, EventArgs e) {
            _textBox.Foreground = new SolidColorBrush(Colors.Black);
            if (_textBox.Text == PLACEHOLDER_URL) _textBox.Text = "";
        }

        private void AddText(object sender, EventArgs e) {
            _textBox.Foreground = new SolidColorBrush(Colors.Black);
            if (string.IsNullOrWhiteSpace(_textBox.Text)) {
                _textBox.Text = PLACEHOLDER_URL;
                _textBox.Foreground = Colours.HINT_FG;
            }
        }

        static public double RoundSF(double n, int digits) {
            if (n == 0) return 0;
            double scale = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(n))) + 1);
            return scale * Math.Round(n / scale, digits);
        }

        private void NotifyIcon_Click(object sender, EventArgs e) {
            Forms.MouseEventArgs m = e as Forms.MouseEventArgs;
            if (m.Button == Forms.MouseButtons.Left) PresentWindow();
        }

        private void PresentWindow() {
            Show();
            Activate();
        }

        private void ExitProgram(object sender, EventArgs e) {
            _notifyIcon.Dispose();
            Close();
            Application.Current.Shutdown();
        }

        private void SaveData() {
            using StreamWriter file = File.CreateText(DATA_FILE);
            new JsonSerializer().Serialize(file, _savedItems);
        }

        private void UpdateData() { 
            foreach (Item item in _items) {
                int count = 0;
                foreach (JObject data in _savedItems["items"].Cast<JObject>()) {
                    if (data["item"].ToString().Equals(item.Name)) {
                        _savedItems["items"][count]["price"] = item.GetPrice().ToString();
                        _savedItems["items"][count]["threshold"] = item.Threshold.ToString();
                        break;
                    } count++;
                }
            } SaveData();
        }

        public char GetCurrency() => CURRENCIES[_currencyID];

        /*
        protected override void OnStateChanged(EventArgs e) {
            if (WindowState == WindowState.Minimized) Hide();
            base.OnStateChanged(e);
        }
        */

        protected override void OnClosing(CancelEventArgs e) {
            e.Cancel = true;
            Hide();
            base.OnClosing(e);
        }

        private void AddButton_Click(object sender, RoutedEventArgs e) {
            string itemName = HttpUtility.UrlDecode(_textBox.Text.Split("/").Last());

            if (!Regex.IsMatch(_textBox.Text, REGEX_URL)) {
                MessageBox.Show("Error: Invalid item URL!", "SMM - Add Item", MessageBoxButton.OK);
                return;
            }

            if (ItemExists(itemName)) {
                MessageBox.Show("Error: There is already an item with this name.", "SMM - Add Item", MessageBoxButton.OK);
                return;
            }

            if (double.TryParse(_priceBox.Text, out double price)) {
                Item item = new Item() { Name = itemName, Price = FormatPrice(price), LowestPrice = "N/A", MedianPrice = "N/A", Threshold = 50 };
                _items.Add(item);
                if (CheckCache(ref item)) _listView.Items.Refresh();
                UpdatePrice(item);
                JArray itemList = (JArray)_savedItems["items"];
                JObject itemObject = new JObject() { { "item", item.Name }, { "threshold", -1 }, { "price", item.GetPrice()} };
                itemList.Add(itemObject);
                SaveData();
                _listView.Items.Refresh();
                CacheItem(item);
            }
        }

        private void EditButton_Click(object sender, RoutedEventArgs e) {
            Item selectedItem = (Item)_listView.SelectedItem;
            new EditWindow(this, ref selectedItem).ShowDialog();
            UpdateData();
            _listView.Items.Refresh();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e) {
            System.Collections.IList list = _listView.SelectedItems;
            foreach (Item item in list) {
                _items.Remove(item);
                foreach (JToken data in _savedItems["items"].Where(data => data["item"].ToString().Equals(item.Name))) {
                    data.Remove();
                    break;
                }
            }
            SaveData();
            _listView.Items.Refresh();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await UpdatePrices();

        private void GridViewColumnHeaderClickedHandler(object sender, RoutedEventArgs e) {
            GridViewColumnHeader header = e.OriginalSource as GridViewColumnHeader;
            _listView.SelectedItem = null;
            if (header.Content.ToString().Equals(_lastHeader)) {
                _items.Reverse();
            } else {
                _lastHeader = header.Content.ToString();
                if (_lastHeader.Equals("Name")) _items.Sort((x, y) => x.Name.CompareTo(y.Name));
                else if (_lastHeader.Equals("Price")) _items.Sort((x, y) => x.GetPrice().CompareTo(y.GetPrice()));
                else if (_lastHeader.Equals("Lowest Price")) _items.Sort((x, y) => x.LowestPrice.CompareTo(y.LowestPrice));
                else if (_lastHeader.Equals("Median Price")) _items.Sort((x, y) => x.MedianPrice.CompareTo(y.MedianPrice));
                else if (_lastHeader.Equals("%Δ Lowest")) _items.Sort((x, y) => x.ChangeLowest.CompareTo(y.ChangeLowest));
                else if (_lastHeader.Equals("%Δ Median")) _items.Sort((x, y) => x.ChangeMedian.CompareTo(y.ChangeMedian));
            } _listView.Items.Refresh();
        }

        private void Items_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            System.Collections.IList list = _listView.SelectedItems;
            _removeButton.IsEnabled = list.Count > 0;
            _editButton.IsEnabled = list.Count == 1;
        }

        private void Items_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            if (_listView.SelectedItems.Count == 1) {
                Item item = (Item)_listView.SelectedItem;
                new EditWindow(this, ref item).ShowDialog();
                UpdateData();
                _listView.Items.Refresh();
            }
        }

    }
}
