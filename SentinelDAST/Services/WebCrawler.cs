using System.Diagnostics;
using System.Net.Http;
using System.Web;
using HtmlAgilityPack;
using SentinelDAST.Models;

namespace SentinelDAST.Services {
    public class WebCrawler {
        readonly HttpClient _httpClient;
        SiteMap? _siteMap;
        CancellationTokenSource _cancellationTokenSource;
        bool _isPaused;
        readonly SemaphoreSlim _pauseSemaphore = new SemaphoreSlim(1, 1);
        readonly Lock _lockObject = new();

        // Configurable crawler settings
        public int MaxConcurrentRequests { get; set; } = 3;
        public int MaxPagesToProcess { get; set; } = 100;
        public int RequestDelayMs { get; set; } = 500;

        // Crawler statistics - using fields for Interlocked operations
        int _pagesProcessed;
        int _totalLinks;
        int _totalAssets;

        int PagesProcessed => _pagesProcessed;
        int TotalLinks => _totalLinks;
        int TotalAssets => _totalAssets;

        // Crawler events
        public event EventHandler<CrawlProgressEventArgs>? CrawlProgressChanged;
        public event EventHandler<CrawlCompletedEventArgs>? CrawlCompleted;
        public event EventHandler<PageCrawledEventArgs>? PageCrawled;

        public WebCrawler(CancellationTokenSource cancellationTokenSource) {
            _cancellationTokenSource = cancellationTokenSource;
            _httpClient = new HttpClient(new HttpClientHandler {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            });

            // Set a user agent to mimic a browser
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd(
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");

            // Reasonable timeout
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task StartCrawlingAsync(string? rootUrl, CancellationToken? cancellationToken = null) {
            // Create a new site map with the root URL
            _siteMap = new SiteMap(rootUrl);

            // Reset statistics
            _pagesProcessed = 0;
            _totalLinks = 0;
            _totalAssets = 0;

            // Setup cancellation
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken ?? CancellationToken.None);

            _isPaused = false;

            var startTime = DateTime.Now;
            var tasks = new List<Task>();

            try {
                // Process URLs from the queue until it's empty, or we hit the max page limit
                while (_siteMap.HasUrlsToProcess &&
                       PagesProcessed < MaxPagesToProcess &&
                       !_cancellationTokenSource.Token.IsCancellationRequested) {
                    // Check if we need to pause
                    await CheckPauseStatusAsync();

                    // Control concurrency using a semaphore or similar approach
                    while (tasks.Count >= MaxConcurrentRequests) {
                        var completedTask = await Task.WhenAny(tasks);
                        tasks.Remove(completedTask);
                        await Task.Delay(RequestDelayMs);
                    }

                    var nextUrl = _siteMap.GetNextUrlToProcess();
                    if(string.IsNullOrEmpty(nextUrl)) continue;

                    // Don't reprocess URLs we've already handled
                    if(_siteMap.ProcessedUrls.Contains(nextUrl)) continue;

                    // Only process URLs from the same domain
                    if(!_siteMap.ShouldProcessUrl(nextUrl)) continue;

                    // Launch a task to process this URL
                    var task = ProcessUrlAsync(nextUrl, _cancellationTokenSource.Token);
                    tasks.Add(task);

                    // Small delay to avoid overwhelming the server
                    await Task.Delay(RequestDelayMs, _cancellationTokenSource.Token);
                }

                // Wait for all remaining tasks to complete
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) {
                Debug.WriteLine("Crawling was cancelled.");
            }
            catch (Exception ex) {
                Debug.WriteLine($"Error during crawling: {ex.Message}");
            }
            finally {
                // Notify that crawling is complete
                var duration = DateTime.Now - startTime;
                var cancelled = _cancellationTokenSource.Token.IsCancellationRequested;

                CrawlCompleted?.Invoke(this, new CrawlCompletedEventArgs(
                    _siteMap,
                    PagesProcessed,
                    TotalLinks,
                    TotalAssets,
                    duration,
                    cancelled
                ));
            }
        }

        public void StopCrawling() {
            _cancellationTokenSource.Cancel();
        }

        public void PauseCrawling() {
            _isPaused = true;
        }

        public void ResumeCrawling() {
            _isPaused = false;
            _pauseSemaphore.Release();
        }

        async Task CheckPauseStatusAsync() {
            if(_isPaused) {
                await _pauseSemaphore.WaitAsync();
                _pauseSemaphore.Release();
            }
        }

        async Task ProcessUrlAsync(string? url, CancellationToken cancellationToken) {
            try {
                // Mark the URL as being processed to avoid duplicates
                lock (_lockObject) {
                    if(_siteMap != null && _siteMap.ProcessedUrls.Contains(url)) return;
                    _siteMap?.ProcessedUrls.Add(url); // Temporarily mark to avoid concurrent processing
                }

                // Fetch and parse the page
                var result = await CrawlPageAsync(url, cancellationToken);

                // Update a site map with this page's data
                UpdateSiteMap(url, result);

                // Update statistics
                Interlocked.Increment(ref _pagesProcessed);
                Interlocked.Add(ref _totalLinks, result.InternalLinks.Count + result.ExternalLinks.Count);
                Interlocked.Add(ref _totalAssets, result.ScriptSources.Count + result.StylesheetLinks.Count);

                // Raise progress event
                if(_siteMap != null) {
                    CrawlProgressChanged?.Invoke(this, new CrawlProgressEventArgs(
                        PagesProcessed,
                        _siteMap.ProcessedUrls.Count + _siteMap.UrlsToProcess.Count,
                        url
                    ));

                    // Get the site node for this URL
                    SiteNode? pageNode = null;
                    // Create a local variable to avoid using property without a parameter
                    var allNodes = _siteMap.AllNodes;
                    if(url != null && allNodes.TryGetValue(url, out var node)) {
                        pageNode = node;
                    }

                    // Raise page crawled event
                    PageCrawled?.Invoke(this, new PageCrawledEventArgs(url, result, pageNode));
                }
            }
            catch (Exception ex) {
                Debug.WriteLine($"Error processing URL {url}: {ex.Message}");
            }
        }

        void UpdateSiteMap(string? url, CrawlResult result) {
            // Add the current page to a site map if not already there
            if(_siteMap != null && url != null && !_siteMap.AllNodes.ContainsKey(url)) {
                _siteMap.AddPage(url);
            }

            // Process internal links - add to the queue for further crawling
            foreach (var link in result.InternalLinks) {
                _siteMap?.AddPage(link, url);
            }

            // Process external links - add to a site map but don't crawl further
            foreach (var link in result.ExternalLinks) {
                _siteMap?.AddExternalLink(link, url);
            }

            // Add assets to the current page
            foreach (var script in result.ScriptSources) {
                _siteMap?.AddAsset(script, url);
            }

            foreach (var style in result.StylesheetLinks) {
                _siteMap?.AddAsset(style, url);
            }

            // Add form actions
            foreach (var form in result.FormActions) {
                _siteMap?.AddForm(form, url);
            }

            // Mark URL as fully processed
            _siteMap?.MarkAsProcessed(url);
        }

        async Task<CrawlResult> CrawlPageAsync(string? url, CancellationToken cancellationToken = default) {
            if(!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                throw new ArgumentException("Invalid URL format", nameof(url));
            }

            Debug.WriteLine($"Crawling: {url}");

            try {
                var response = await _httpClient.GetAsync(uri, cancellationToken);
                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if(!contentType.Contains("html")) {
                    Debug.WriteLine($"Skipping non-HTML content: {contentType}");
                    return new CrawlResult(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
                        Array.Empty<string>(), Array.Empty<string>());
                }

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = ParseHtml(html, uri);

                // Log discovered URLs
                Debug.WriteLine($"Found {result.InternalLinks.Count} internal links on {url}");
                foreach (var link in result.InternalLinks) {
                    Debug.WriteLine($"  Internal Link: {link}");
                }

                Debug.WriteLine($"Found {result.ExternalLinks.Count} external links on {url}");
                foreach (var link in result.ExternalLinks) {
                    Debug.WriteLine($"  External Link: {link}");
                }

                Debug.WriteLine($"Found {result.ScriptSources.Count} script sources on {url}");
                foreach (var script in result.ScriptSources) {
                    Debug.WriteLine($"  Script: {script}");
                }

                Debug.WriteLine($"Found {result.StylesheetLinks.Count} stylesheet links on {url}");
                foreach (var css in result.StylesheetLinks) {
                    Debug.WriteLine($"  Stylesheet: {css}");
                }

                Debug.WriteLine($"Found {result.FormActions.Count} form actions on {url}");
                foreach (var form in result.FormActions) {
                    Debug.WriteLine($"  Form: {form}");
                }

                return result;
            }
            catch (HttpRequestException ex) {
                Debug.WriteLine($"HTTP error crawling {url}: {ex.Message}");
                throw;
            }
            catch (TaskCanceledException) {
                Debug.WriteLine($"Timeout crawling {url}");
                throw;
            }
            catch (Exception ex) {
                Debug.WriteLine($"Error crawling {url}: {ex.Message}");
                throw;
            }
        }

        CrawlResult ParseHtml(string html, Uri baseUri) {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var internalLinks = new HashSet<string?>();
            var externalLinks = new HashSet<string?>();
            var scriptSources = new HashSet<string?>();
            var stylesheetLinks = new HashSet<string?>();
            var formActions = new HashSet<string?>();

            // Base domain for determining internal vs external links
            var baseDomain = baseUri.Host;

            // Extract hyperlinks from anchor tags
            var anchorNodes = doc.DocumentNode.SelectNodes("//a[@href]");
            if(anchorNodes != null) {
                foreach (var anchor in anchorNodes) {
                    var href = anchor.GetAttributeValue("href", "");
                    if(string.IsNullOrWhiteSpace(href)) {
                        continue;
                    }

                    var absoluteUrl = ResolveUrl(href, baseUri);
                    if(string.IsNullOrEmpty(absoluteUrl)) {
                        continue;
                    }

                    // Check if it's internal or external
                    if(!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var linkUri)) {
                        continue;
                    }

                    if(linkUri.Host.Equals(baseDomain, StringComparison.OrdinalIgnoreCase)) {
                        internalLinks.Add(absoluteUrl);
                    }
                    else {
                        externalLinks.Add(absoluteUrl);
                    }
                }
            }

            // Extract script sources
            var scriptNodes = doc.DocumentNode.SelectNodes("//script[@src]");
            if(scriptNodes != null) {
                foreach (var script in scriptNodes) {
                    var src = script.GetAttributeValue("src", "");
                    if(string.IsNullOrWhiteSpace(src)) {
                        continue;
                    }

                    var absoluteUrl = ResolveUrl(src, baseUri);
                    if(!string.IsNullOrEmpty(absoluteUrl)) {
                        scriptSources.Add(absoluteUrl);
                    }
                }
            }

            // Extract stylesheet links
            var linkNodes = doc.DocumentNode.SelectNodes("//link[@rel='stylesheet' or @type='text/css'][@href]");
            if(linkNodes != null) {
                foreach (var link in linkNodes) {
                    var href = link.GetAttributeValue("href", "");
                    if(string.IsNullOrWhiteSpace(href)) {
                        continue;
                    }

                    var absoluteUrl = ResolveUrl(href, baseUri);
                    if(!string.IsNullOrEmpty(absoluteUrl)) {
                        stylesheetLinks.Add(absoluteUrl);
                    }
                }
            }

            // Extract form actions
            var formNodes = doc.DocumentNode.SelectNodes("//form[@action]");
            if(formNodes == null) {
                return new CrawlResult(internalLinks,
                    externalLinks,
                    scriptSources,
                    stylesheetLinks,
                    formActions
                );
            }

            {
                foreach (var form in formNodes) {
                    var action = form.GetAttributeValue("action", "");
                    if(string.IsNullOrWhiteSpace(action)) {
                        continue;
                    }

                    var absoluteUrl = ResolveUrl(action, baseUri);
                    if(!string.IsNullOrEmpty(absoluteUrl)) {
                        formActions.Add(absoluteUrl);
                    }
                }
            }

            return new CrawlResult(internalLinks,
                externalLinks,
                scriptSources,
                stylesheetLinks,
                formActions
            );
        }

        static string? ResolveUrl(string url, Uri baseUri) {
            // Skip empty URLs, javascript: and data: URLs
            if(string.IsNullOrWhiteSpace(url) ||
               url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
               url.StartsWith("#")) {
                return null;
            }

            // Decode HTML entities in URL
            url = HttpUtility.HtmlDecode(url.Trim());

            // Detect and fix common URL issues
            if(url.Contains("\\u0026")) {
                url = url.Replace("\\u0026", "&");
            }

            // Fix URLs with double slashes except after protocol
            if(url.Contains("//") && !url.StartsWith("http://") && !url.StartsWith("https://")) {
                url = url.Replace("//", "/");
            }

            try {
                // Check if it's already an absolute URL
                if(Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri)) {
                    return absoluteUri.ToString();
                }

                // Handle relative URLs
                return Uri.TryCreate(baseUri, url, out var combinedUri) ? combinedUri.ToString() : null; // Couldn't create a valid URI
            }
            catch (Exception ex) {
                Debug.WriteLine($"Failed to resolve URL: {url}. Error: {ex.Message}");
                return null;
            }
        }
    }

    public record CrawlResult(
        ICollection<string?> InternalLinks,
        ICollection<string?> ExternalLinks,
        ICollection<string?> ScriptSources,
        ICollection<string?> StylesheetLinks,
        ICollection<string?> FormActions
    );
}
