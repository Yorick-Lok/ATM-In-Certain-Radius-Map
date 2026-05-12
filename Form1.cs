using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ATM_In_Certain_Radius_Map
{
    public partial class Form1 : Form
    {
        private WebView2 webView;
        private Timer timer = new Timer();

        public Form1()
        {
            InitializeComponent();

            // Setup WebView2
            webView = new WebView2 { Dock = DockStyle.Fill };
            this.Controls.Add(webView);

            this.Load += Form1_Load;
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            await webView.EnsureCoreWebView2Async(null);
            //string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "map.html");

            // from bin\Debug (or bin\Release) go up to project root then into Assets
            string candidate = Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Assets", "map.html"));

            if (!File.Exists(candidate))
            {
                MessageBox.Show($"Map file not found: {candidate}");
                return;
            }

            webView.CoreWebView2.Navigate(new Uri(candidate).AbsoluteUri);

            // Wait for HTML to load completely
            webView.NavigationCompleted += async (s, ev) =>
            {
                // Tiny delay to ensure JS functions exist
                await Task.Delay(100);
            };

        }
    }
}
