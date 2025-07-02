using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;

namespace SentinelDAST.Services
{
    public class WebCrawler
    {
        private readonly HttpClient _httpClient;

        public WebCrawler()
        {
            _httpClient = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            });

            // Set user agent to mimic a browser
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");

            // Reasonable timeout
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<CrawlResult> CrawlPageAsync(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                throw new ArgumentException("Invalid URL format", nameof(url));
            }

            Debug.WriteLine($"Crawling: {url}");

            try
            {
                var response = await _httpClient.GetAsync(uri);
                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!contentType.Contains("html"))
                {
                    Debug.WriteLine($"Skipping non-HTML content: {contentType}");
                    return new CrawlResult(url, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
                }

                var html = await response.Content.ReadAsStringAsync();
                var result = ParseHtml(html, uri);

                // Log discovered URLs
                Debug.WriteLine($"Found {result.Hyperlinks.Count} hyperlinks on {url}");
                foreach (var link in result.Hyperlinks)
                {
                    Debug.WriteLine($"  Link: {link}");
                }

                Debug.WriteLine($"Found {result.ScriptSources.Count} script sources on {url}");
                foreach (var script in result.ScriptSources)
                {
                    Debug.WriteLine($"  Script: {script}");
                }

                Debug.WriteLine($"Found {result.StylesheetLinks.Count} stylesheet links on {url}");
                foreach (var css in result.StylesheetLinks)
                {
                    Debug.WriteLine($"  Stylesheet: {css}");
                }

                Debug.WriteLine($"Found {result.FormActions.Count} form actions on {url}");
                foreach (var form in result.FormActions)
                {
                    Debug.WriteLine($"  Form: {form}");
                }

                return result;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"HTTP error crawling {url}: {ex.Message}");
                throw;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine($"Timeout crawling {url}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error crawling {url}: {ex.Message}");
                throw;
            }
        }

        private CrawlResult ParseHtml(string html, Uri baseUri)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var hyperlinks = new HashSet<string>();
            var scriptSources = new HashSet<string>();
            var stylesheetLinks = new HashSet<string>();
            var formActions = new HashSet<string>();

            // Extract hyperlinks from anchor tags
            var anchorNodes = doc.DocumentNode.SelectNodes("//a[@href]");
            if (anchorNodes != null)
            {
                foreach (var anchor in anchorNodes)
                {
                    var href = anchor.GetAttributeValue("href", "");
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        var absoluteUrl = ResolveUrl(href, baseUri);
                        if (!string.IsNullOrEmpty(absoluteUrl))
                        {
                            hyperlinks.Add(absoluteUrl);
                        }
                    }
                }
            }

            // Extract script sources
            var scriptNodes = doc.DocumentNode.SelectNodes("//script[@src]");
            if (scriptNodes != null)
            {
                foreach (var script in scriptNodes)
                {
                    var src = script.GetAttributeValue("src", "");
                    if (!string.IsNullOrWhiteSpace(src))
                    {
                        var absoluteUrl = ResolveUrl(src, baseUri);
                        if (!string.IsNullOrEmpty(absoluteUrl))
                        {
                            scriptSources.Add(absoluteUrl);
                        }
                    }
                }
            }

            // Extract stylesheet links
            var linkNodes = doc.DocumentNode.SelectNodes("//link[@rel='stylesheet' or @type='text/css'][@href]");
            if (linkNodes != null)
            {
                foreach (var link in linkNodes)
                {
                    var href = link.GetAttributeValue("href", "");
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        var absoluteUrl = ResolveUrl(href, baseUri);
                        if (!string.IsNullOrEmpty(absoluteUrl))
                        {
                            stylesheetLinks.Add(absoluteUrl);
                        }
                    }
                }
            }

            // Extract form actions
            var formNodes = doc.DocumentNode.SelectNodes("//form[@action]");
            if (formNodes != null)
            {
                foreach (var form in formNodes)
                {
                    var action = form.GetAttributeValue("action", "");
                    if (!string.IsNullOrWhiteSpace(action))
                    {
                        var absoluteUrl = ResolveUrl(action, baseUri);
                        if (!string.IsNullOrEmpty(absoluteUrl))
                        {
                            formActions.Add(absoluteUrl);
                        }
                    }
                }
            }

            return new CrawlResult(
                baseUri.ToString(),
                hyperlinks,
                scriptSources,
                stylesheetLinks,
                formActions
            );
        }

        private string? ResolveUrl(string url, Uri baseUri)
        {
            // Skip empty URLs, javascript: and data: URLs
            if (string.IsNullOrWhiteSpace(url) ||
                url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("#"))
            {
                return null;
            }

            // Decode HTML entities in URL
            url = HttpUtility.HtmlDecode(url.Trim());

            try
            {
                // Handle relative URLs
                var absoluteUri = new Uri(baseUri, url);
                return absoluteUri.ToString();
            }
            catch
            {
                Debug.WriteLine($"Failed to resolve URL: {url}");
                return null;
            }
        }
    }

    public record CrawlResult(
        string Url,
        ICollection<string> Hyperlinks,
        ICollection<string> ScriptSources,
        ICollection<string> StylesheetLinks,
        ICollection<string> FormActions
    );
}
