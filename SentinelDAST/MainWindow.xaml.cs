using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SentinelDAST.Models;
using SentinelDAST.Services;

namespace SentinelDAST {
    public partial class MainWindow {
        readonly WebCrawler _webCrawler;
        bool _isScanning;
        CancellationTokenSource? _cts;
        SiteMap? _currentSiteMap;
        readonly Dictionary<string, TreeViewItem> _treeViewItems = new();

        public MainWindow() {
            InitializeComponent();

            _webCrawler = new WebCrawler(new CancellationTokenSource());
            ConfigureWebCrawler();

            // Setup event handlers for the crawler
            _webCrawler.CrawlProgressChanged += WebCrawler_CrawlProgressChanged;
            _webCrawler.CrawlCompleted += WebCrawler_CrawlCompleted;
            _webCrawler.PageCrawled += WebCrawler_PageCrawled;

            // Setup UI event handlers
            StartButton.Click += async (_, _) => await StartCrawlingAsync();

            PauseButton.Click += (_, _) =>
            {
                if(_isScanning) {
                    _webCrawler.PauseCrawling();
                    StatusText.Text = "Status: Scan paused";
                    PauseButton.Content = "Resume";
                }
                else {
                    _webCrawler.ResumeCrawling();
                    StatusText.Text = "Status: Scan resumed";
                    PauseButton.Content = "Pause";
                }
            };

            StopButton.Click += (_, _) =>
            {
                _webCrawler.StopCrawling();
                StatusText.Text = "Status: Scan stopped";
                _isScanning = false;
                UpdateButtonStates();
            };

            ExportReportButton.Click += (_, _) =>
            {
                // Will be implemented in future steps
                MessageBox.Show("Report export will be implemented in a future step", "Sentinel DAST");
            };

            // Add settings control
            SetupSettingsUi();
        }

        void ConfigureWebCrawler() {
            // Default crawler settings
            _webCrawler.MaxConcurrentRequests = 3;
            _webCrawler.MaxPagesToProcess = 100;
            _webCrawler.RequestDelayMs = 300;
        }

        static void SetupSettingsUi() {
            // Optional: Add settings UI elements if needed
            // This could be a separate control or panel
        }

        async Task StartCrawlingAsync() {
            if(string.IsNullOrWhiteSpace(TargetUrlTextBox.Text)) {
                MessageBox.Show("Please enter a valid URL", "Sentinel DAST", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Update UI state
            _isScanning = true;
            UpdateButtonStates();
            StatusText.Text = "Status: Starting scan...";

            // Reset progress indicators
            ScanProgressBar.Value = 0;
            ScanProgressText.Text = "0/0 pages";

            // Clear tree view and cached items
            SiteMapTreeView.Items.Clear();
            _treeViewItems.Clear();

            try {
                var url = TargetUrlTextBox.Text.Trim();

                // Ensure URL has a proper scheme
                if(!url.StartsWith("http://") && !url.StartsWith("https://")) {
                    url = "https://" + url;
                    TargetUrlTextBox.Text = url;
                }

                // Validate URL format
                if(!Uri.TryCreate(url, UriKind.Absolute, out _)) {
                    throw new ArgumentException($"The URL '{url}' is not in a valid format.");
                }

                Debug.WriteLine($"Starting recursive crawl of {url}");

                // Create a cancellation token source for this crawl session
                _cts = new CancellationTokenSource();

                // Start the recursive crawling process
                await Task.Run(() => _webCrawler.StartCrawlingAsync(url, _cts.Token));
            }
            catch (Exception ex) {
                Debug.WriteLine($"Error during crawl: {ex.Message}");
                StatusText.Text = $"Status: Error - {ex.Message}";
                MessageBox.Show($"Error crawling the specified URL: {ex.Message}", "Sentinel DAST", MessageBoxButton.OK,
                    MessageBoxImage.Error);

                // Update UI state in case of error
                _isScanning = false;
                UpdateButtonStates();
            }
        }

        void AddOrUpdateTreeViewNode(string? url, SiteNode? node, CrawlResult result) {
            if(node == null) return;
            if(_currentSiteMap == null) return;

            try {
                // First, make sure the root domain node exists
                if(!_treeViewItems.TryGetValue(_currentSiteMap.RootDomain.Url ?? throw new InvalidOperationException(), out var rootDomainItem)) {
                    rootDomainItem = new TreeViewItem {
                        Header = _currentSiteMap.RootDomain.DisplayName,
                        IsExpanded = true,
                        Tag = _currentSiteMap.RootDomain
                    };
                    SiteMapTreeView.Items.Add(rootDomainItem);
                    _treeViewItems[_currentSiteMap.RootDomain.Url] = rootDomainItem;
                }

                // Check if we're dealing with an external link
                if(node.Type == NodeType.ExternalDomain || IsExternalLink(url, _currentSiteMap.RootDomain.Url)) {
                    AddExternalLinkNode(url);
                    return;
                }

                // Process internal links
                AddInternalLinkNode(url, node, result, rootDomainItem);
            }
            catch (Exception ex) {
                Debug.WriteLine($"Error adding tree node for {url}: {ex.Message}");
            }
        }

        void AddInternalLinkNode(string? url, SiteNode? node, CrawlResult result, TreeViewItem rootDomainItem) {
            // Don't add if we already have this node
            if(url != null && _treeViewItems.ContainsKey(url)) return;

            if(!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                return;
            }

            // Find or create parent path nodes (for hierarchical organization)
            var path = uri.AbsolutePath.TrimStart('/');
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // Start from the root domain
            var currentItem = rootDomainItem;

            // Build path hierarchy
            if(segments.Length > 0) {
                var currentPath = uri.Scheme + "://" + uri.Host;

                for (var i = 0; i < segments.Length - 1; i++) {
                    currentPath += "/" + segments[i];

                    // Check if we already have this path segment
                    if(!_treeViewItems.TryGetValue(currentPath, out var pathItem)) {
                        pathItem = new TreeViewItem {
                            Header = segments[i],
                            IsExpanded = true,
                            Tag = currentPath
                        };
                        currentItem.Items.Add(pathItem);
                        _treeViewItems[currentPath] = pathItem;
                    }

                    currentItem = pathItem;
                }
            }

            // Create the page node
            if(node == null) {
                return;
            }

            var nodeItem = new TreeViewItem {
                Header = node.DisplayName,
                Tag = node,
                IsExpanded = false
            };
            currentItem.Items.Add(nodeItem);
            _treeViewItems[url] = nodeItem;

            // Add the page's assets
            // Add scripts
            if(result.ScriptSources.Count <= 0) {
                return;
            }

            var scriptsItem = new TreeViewItem { Header = "Scripts", IsExpanded = false };
            nodeItem.Items.Add(scriptsItem);

            foreach (var script in result.ScriptSources) {
                scriptsItem.Items.Add(new TreeViewItem { Header = GetDisplayName(script), Tag = script });
            }

            // Add stylesheets
            if(result.StylesheetLinks.Count > 0) {
                var stylesItem = new TreeViewItem { Header = "Stylesheets", IsExpanded = false };
                nodeItem.Items.Add(stylesItem);

                foreach (var style in result.StylesheetLinks) {
                    stylesItem.Items.Add(new TreeViewItem { Header = GetDisplayName(style), Tag = style });
                }
            }

            // Add forms
            if(result.FormActions.Count <= 0) {
                return;
            }

            var formsItem = new TreeViewItem { Header = "Forms", IsExpanded = false };
            nodeItem.Items.Add(formsItem);

            foreach (var form in result.FormActions) {
                formsItem.Items.Add(new TreeViewItem { Header = GetDisplayName(form), Tag = form });
            }
        }

        void AddExternalLinkNode(string? url) {
            // Ensure we have the external links section
            if(!_treeViewItems.TryGetValue("external_links", out var externalLinksItem)) {
                externalLinksItem = new TreeViewItem {
                    Header = "External Links",
                    IsExpanded = true,
                    Tag = "external_links"
                };
                SiteMapTreeView.Items.Add(externalLinksItem);
                _treeViewItems["external_links"] = externalLinksItem;
            }

            if(!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                return;
            }

            // Get or create the domain group
            var domainUrl = $"{uri.Scheme}://{uri.Host}";
            if(!_treeViewItems.TryGetValue(domainUrl, out var domainItem)) {
                domainItem = new TreeViewItem {
                    Header = uri.Host,
                    IsExpanded = true,
                    Tag = domainUrl
                };
                externalLinksItem.Items.Add(domainItem);
                _treeViewItems[domainUrl] = domainItem;
            }

            // Add the URL if we don't already have it
            if(_treeViewItems.ContainsKey(url)) {
                return;
            }

            var linkItem = new TreeViewItem {
                Header = GetDisplayName(url),
                Tag = url
            };
            domainItem.Items.Add(linkItem);
            _treeViewItems[url] = linkItem;
        }

        static bool IsExternalLink(string? url, string? rootDomainUrl) {
            if(!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
               !Uri.TryCreate(rootDomainUrl, UriKind.Absolute, out var rootUri)) {
                return false;
            }

            return !uri.Host.Equals(rootUri.Host, StringComparison.OrdinalIgnoreCase);
        }

        static string? GetDisplayName(string? url) {
            if(!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                return url;
            }

            var pathAndQuery = uri.PathAndQuery;

            if(string.IsNullOrEmpty(pathAndQuery) || pathAndQuery == "/") {
                return "/" + (string.IsNullOrEmpty(uri.Fragment) ? "" : uri.Fragment);
            }

            // For files, show the filename
            var fileName = System.IO.Path.GetFileName(uri.LocalPath);
            if(!string.IsNullOrEmpty(fileName)) {
                return fileName + uri.Query + uri.Fragment;
            }

            return pathAndQuery + uri.Fragment;
        }

        void UpdateButtonStates() {
            StartButton.IsEnabled = !_isScanning;
            PauseButton.IsEnabled = _isScanning;
            StopButton.IsEnabled = _isScanning;
            TargetUrlTextBox.IsEnabled = !_isScanning;
        }

        void WebCrawler_CrawlProgressChanged(object? sender, CrawlProgressEventArgs e) {
            // Update UI on the UI thread
            Dispatcher.Invoke(() =>
            {
                ScanProgressText.Text = $"{e.PagesProcessed}/{e.TotalPages} pages";
                ScanProgressBar.Value = e.Percentage;
                StatusText.Text = $"Status: Crawling {e.CurrentUrl}";
            });
        }

        void WebCrawler_CrawlCompleted(object? sender, CrawlCompletedEventArgs e) {
            // Update UI on the UI thread
            Dispatcher.Invoke(() =>
            {
                ScanProgressBar.Value = 100;
                StatusText.Text = e.Cancelled
                    ? "Status: Scan cancelled"
                    : $"Status: Scan completed - {e.PagesProcessed} pages, {e.TotalLinks} links, {e.Duration.TotalSeconds:F1}s";

                _isScanning = false;
                UpdateButtonStates();

                // Keep a reference to the completed site map
                _currentSiteMap = e.SiteMap;
            });
        }

        void WebCrawler_PageCrawled(object? sender, PageCrawledEventArgs e) {
            // Update the site map tree view with the new page
            Dispatcher.Invoke(() => { AddOrUpdateTreeViewNode(e.Url, e.Node, e.Result); });
        }
    }
}
