using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Http;
using System.Globalization;
using System.Web.Script.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.CompilerServices;

namespace ATM_In_Certain_Radius_Map
{
    public partial class Form1 : Form
    {
        private WebView2 webView;

        private Timer typingTimer = new Timer();

        private List<NominatimResult> lastResults = new List<NominatimResult>();

        private string pendingQuery;




        private static readonly HttpClient client = new HttpClient();

        private bool circleVisible = true;
        private double lastLat;
        private double lastLon;

        private int currentRadius = 2000;

        public Form1()
        {
            InitializeComponent();

            webView = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(webView);

            Load += Form1_Load;

            typingTimer.Interval = 400;
            typingTimer.Tick += TypingTimer_Tick;
        }

        // ---------------- INIT ----------------

        private async void Form1_Load(object sender, EventArgs e)
        {
            await webView.EnsureCoreWebView2Async(null);

            string candidate = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets",
                "map.html");

            webView.CoreWebView2.Navigate(new Uri(candidate).AbsoluteUri);


            var basePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Assets"
            );

            await webView.CoreWebView2.ExecuteScriptAsync(
                $"window.basePath = '{new Uri(basePath + "\\").AbsoluteUri}';"
            );

            webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            await Task.Delay(500);

        }

        // ---------------- ATM ----------------
        public class ATM
        {
            public string Name { get; set; }
            public double Lat { get; set; }
            public double Lon { get; set; }
        }
        // ---------------- Send ATMs to JS ----------------
        private async Task SendAtmsToJs(List<ATM> atms)
        {
            var json = new JavaScriptSerializer().Serialize(atms);
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"loadAtms({json});"
                );
        }

        // ---------------- Convert JSON into existing objects and add them to the map. ----------------

        public class OverpassResponse
        {
            public List<OverpassElements> elements { get; set; }
        }

        public class OverpassElements
        {
            public double lat { get; set; }
            public double lon { get; set; }

            public Dictionary<string, string> tags { get; set; }
        }

        // ---------------- MESSAGE BRIDGE ----------------
        private void CoreWebView2_WebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            string msg = e.TryGetWebMessageAsString();

            if (string.IsNullOrEmpty(msg))
                return;

            // ---------------- SELECT ----------------

            if (msg.StartsWith("SELECT|"))
            {
                var parts = msg.Split('|');
                if (parts.Length == 2 && int.TryParse(parts[1], out int index))
                {
                    _ = HandleSuggestionSelect(index);
                }
                return;
            }

            // ---------------- SEARCH ----------------

            if (msg.StartsWith("SEARCH|"))
            {
                var parts = msg.Split('|');

                if (parts.Length >= 2)
                {
                    pendingQuery = parts[1]?.Trim();
                    typingTimer.Stop();
                    typingTimer.Start();
                }

                return;
            }

            // ---------------- RADIUS ----------------

            if (msg.StartsWith("RADIUS|"))
            {
                var parts = msg.Split('|');

                if (parts.Length == 2 && int.TryParse(parts[1], out int value))
                {
                    currentRadius = value;

                    if (circleVisible && lastLat != 0 && lastLon != 0)
                        _ = DrawRadiusCircle(lastLat, lastLon, currentRadius);

                    _ = webView.CoreWebView2.ExecuteScriptAsync(
                        $"setRadiusValue({currentRadius});"
                    );
                }

                return;
            }

            // ---------------- TOGGLE ----------------

            if (msg.StartsWith("TOGGLE|"))
            {
                var parts = msg.Split('|');

                if (parts.Length == 2)
                {
                    circleVisible = parts[1] == "true";

                    if (circleVisible)
                    {
                        if (lastLat != 0 && lastLon != 0)
                            _ = DrawRadiusCircle(lastLat, lastLon, currentRadius);
                    }
                    else
                    {
                        _ = webView.CoreWebView2.ExecuteScriptAsync("removeRadius();");
                    }
                }

                return;
            }
        }

        // ---------------- SEARCH DEBOUNCE ----------------

        private async void TypingTimer_Tick(object sender, EventArgs e)
        {
            typingTimer.Stop();

            if (string.IsNullOrWhiteSpace(pendingQuery))
                return;

            var results = await QueryNominatim(pendingQuery, 6);
            lastResults = results;

            await webView.CoreWebView2.ExecuteScriptAsync(BuildSuggestionsJS(results));
        }

        // ---------------- SELECT ----------------

        private async Task HandleSuggestionSelect(int index)
        {
            if (index < 0 || index >= lastResults.Count)
                return;

            var sel = lastResults[index];

            lastLat = sel.Lat;
            lastLon = sel.Lon;

            await webView.CoreWebView2.ExecuteScriptAsync(
                $"map.setView([{sel.Lat.ToString(CultureInfo.InvariantCulture)},{sel.Lon.ToString(CultureInfo.InvariantCulture)}], 15);");

            await DrawRadiusCircle(sel.Lat, sel.Lon, currentRadius);
            await LoadNearbyATMs(sel.Lat, sel.Lon, 20000);
        }

        // ---------------- DRAW CIRCLE ----------------

        private async Task DrawRadiusCircle(double Lat, double Lon, double radiusMeters)
        {
            if (!circleVisible)
                return;

            if (Lat == 0 && Lon == 0)
                return;

            string script =
                $"updateRadius({Lat.ToString(CultureInfo.InvariantCulture)}," +
                $"{Lon.ToString(CultureInfo.InvariantCulture)}, {radiusMeters});";

            await webView.CoreWebView2.ExecuteScriptAsync(script);
        }

        // ---------------- LOAD NEARBY ATMS ----------------

        private async Task LoadNearbyATMs(double lat, double lon, int radius)
        {
            string query =
                "[out:json];" +
            $"node[amenity=atm](around:{radius}," +
            $"{lat.ToString(CultureInfo.InvariantCulture)}," +
            $"{lon.ToString(CultureInfo.InvariantCulture)});" +
            $"out center;";

            // Send the query to Overpass API
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("data", query)
            });

            var response = await client.PostAsync(
                "https://overpass-api.de/api/interpreter",
                content);

            response.EnsureSuccessStatusCode();

            // Read the JSON response
            string json = await response.Content.ReadAsStringAsync();

            // Deserialize the JSON
            var serializer = new JavaScriptSerializer();

            var overpassResult =
                serializer.Deserialize<OverpassResponse>(json);

            // Convert them into ATM List
            List<ATM> atms = new List<ATM>();

            foreach (var element in overpassResult.elements)
            {
                string name = "ATM";

                if (element.tags != null &&
                    element.tags.ContainsKey("name"))
                {
                    name = element.tags["name"];
                }

                atms.Add(new ATM
                {
                    Name = name,
                    Lat = element.lat,
                    Lon = element.lon
                });
            }

            System.Diagnostics.Debug.WriteLine("JSON LENGTH: " + json.Length);
            System.Diagnostics.Debug.WriteLine(json);

            await SendAtmsToJs(atms);
        }

        // ---------------- NOMINATIM ----------------

        private async Task<List<NominatimResult>> QueryNominatim(string q, int limit)
        {
            var url =
                "https://nominatim.openstreetmap.org/search?format=json&q=" +
                Uri.EscapeDataString(q) +
                "&limit=" + limit;

            client.DefaultRequestHeaders.UserAgent.ParseAdd("ATM-App/1.0");

            var res = await client.GetStringAsync(url);

            var items = new JavaScriptSerializer()
                .Deserialize<List<Dictionary<string, object>>>(res);

            var list = new List<NominatimResult>();

            foreach (var d in items)
            {
                // Use the actual JSON key names (lowercase) and guard against missing or malformed values
                if (!d.TryGetValue("display_name", out var dnObj) ||
                    !d.TryGetValue("lat", out var latObj) ||
                    !d.TryGetValue("lon", out var lonObj))
                {
                    // skip items missing expected keys
                    continue;
                }

                var displayName = dnObj?.ToString();

                if (!double.TryParse(latObj?.ToString(),
                                     NumberStyles.Float | NumberStyles.AllowThousands,
                                     CultureInfo.InvariantCulture,
                                     out var lat) ||
                    !double.TryParse(lonObj?.ToString(),
                                     NumberStyles.Float | NumberStyles.AllowThousands,
                                     CultureInfo.InvariantCulture,
                                     out var lon))
                {
                    // skip items with unparsable coordinates
                    continue;
                }

                list.Add(new NominatimResult
                {
                    DisplayName = displayName,
                    Lat = lat,
                    Lon = lon
                });
            }

            return list;
        }

        // ---------------- SUGGESTIONS ----------------

        private string BuildSuggestionsJS(List<NominatimResult> results)
        {
            var list = new List<string>();

            foreach (var r in results)
                list.Add(r.DisplayName);

            return $"setSuggestions({new JavaScriptSerializer().Serialize(list)});";
        }

        // ---------------- STUB ----------------

        private Task SelectSuggestionAndLoad(int index)
        {
            return Task.CompletedTask;
        }

        // ---------------- MODEL ----------------

        public class NominatimResult
        {
            public string DisplayName { get; set; }
            public double Lat { get; set; }
            public double Lon { get; set; }
        }
    }
}