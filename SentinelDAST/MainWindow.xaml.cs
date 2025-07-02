using System.Windows;

namespace SentinelDAST {
    public partial class MainWindow : Window {
        public MainWindow() {
            InitializeComponent();

            // Setup event handlers
            StartButton.Click += (s, e) => {
                // Will be implemented in future steps
                StatusText.Text = "Status: Starting scan...";
            };

            PauseButton.Click += (s, e) => {
                // Will be implemented in future steps
                StatusText.Text = "Status: Scan paused";
            };

            StopButton.Click += (s, e) => {
                // Will be implemented in future steps
                StatusText.Text = "Status: Scan stopped";
            };

            ExportReportButton.Click += (s, e) => {
                // Will be implemented in future steps
                MessageBox.Show("Report export will be implemented in a future step", "Sentinel DAST");
            };
        }
    }
}
