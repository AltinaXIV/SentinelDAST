using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace SentinelDAST.Models
{
    public class SiteNode
    {
        public string Url { get; }
        public Uri Uri { get; }
        public string DisplayName { get; }
        public NodeType Type { get; }
        public Dictionary<string, SiteNode> Children { get; }
        public HashSet<string> Assets { get; }
        public HashSet<string> Forms { get; }
        public bool IsProcessed { get; set; }
        public DateTime DiscoveredAt { get; }

        public SiteNode(string url, NodeType type, string? displayName = null)
        {
            Url = url;
            Type = type;
            Uri = new Uri(url);
            DisplayName = displayName ?? GetDisplayName();
            Children = new Dictionary<string, SiteNode>();
            Assets = new HashSet<string>();
            Forms = new HashSet<string>();
            IsProcessed = false;
            DiscoveredAt = DateTime.Now;
        }

        private string GetDisplayName()
        {
            switch (Type)
            {
                case NodeType.Domain:
                    return Uri.Host;
                case NodeType.Page:
                    var path = Uri.AbsolutePath;
                    if (string.IsNullOrEmpty(path) || path == "/")
                    {
                        return "/" + (string.IsNullOrEmpty(Uri.Fragment) ? "" : Uri.Fragment);
                    }
                    return Uri.PathAndQuery + Uri.Fragment;
                case NodeType.ExternalDomain:
                    return Uri.Host;
                case NodeType.Asset:
                    return System.IO.Path.GetFileName(Uri.LocalPath);
                default:
                    return Url;
            }
        }

        public void AddChild(SiteNode child)
        {
            if (!Children.ContainsKey(child.Url))
            {
                Children.Add(child.Url, child);
            }
        }

        public void AddAsset(string assetUrl)
        {
            Assets.Add(assetUrl);
        }

        public void AddForm(string formAction)
        {
            Forms.Add(formAction);
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public enum NodeType
    {
        Domain,
        Page,
        ExternalDomain,
        CategoryFolder, // For grouping (e.g., "External Links", "Assets")
        Asset
    }

    public class SiteMap
    {
        public SiteNode RootDomain { get; }
        public SiteNode ExternalLinks { get; }
        public Dictionary<string, SiteNode> AllNodes { get; }
        public HashSet<string> ProcessedUrls { get; }
        public Queue<string> UrlsToProcess { get; }

        public SiteMap(string rootUrl)
        {
            if (!Uri.TryCreate(rootUrl, UriKind.Absolute, out Uri? rootUri))
            {
                throw new ArgumentException("Invalid root URL format", nameof(rootUrl));
            }

            // Create the root domain node
            string domainUrl = $"{rootUri.Scheme}://{rootUri.Host}";
            RootDomain = new SiteNode(domainUrl, NodeType.Domain);

            // Create the external links category
            ExternalLinks = new SiteNode("external_links", NodeType.CategoryFolder, "External Links");

            AllNodes = new Dictionary<string, SiteNode>
            {
                { domainUrl, RootDomain },
                { "external_links", ExternalLinks }
            };

            ProcessedUrls = new HashSet<string>();
            UrlsToProcess = new Queue<string>();

            // Queue the initial URL for processing
            UrlsToProcess.Enqueue(rootUrl);
        }

        public void AddPage(string url, string parentUrl = null)
        {
            if (AllNodes.ContainsKey(url)) return;

            // Create a new page node
            var node = new SiteNode(url, NodeType.Page);
            AllNodes.Add(url, node);

            // Determine the parent node
            if (parentUrl != null && AllNodes.TryGetValue(parentUrl, out SiteNode parent))
            {
                parent.AddChild(node);
            }
            else
            {
                // If no parent is specified or found, add to the root domain
                RootDomain.AddChild(node);
            }

            // Queue for processing if not already processed
            if (!ProcessedUrls.Contains(url))
            {
                UrlsToProcess.Enqueue(url);
            }
        }

        public void AddExternalLink(string url, string sourceUrl)
        {
            if (AllNodes.ContainsKey(url)) return;

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                return;
            }

            // Create external domain node if it doesn't exist
            string domainUrl = $"{uri.Scheme}://{uri.Host}";
            if (!AllNodes.TryGetValue(domainUrl, out SiteNode domainNode))
            {
                domainNode = new SiteNode(domainUrl, NodeType.ExternalDomain);
                AllNodes.Add(domainUrl, domainNode);
                ExternalLinks.AddChild(domainNode);
            }

            // Create the external page node
            var node = new SiteNode(url, NodeType.Page);
            AllNodes.Add(url, node);
            domainNode.AddChild(node);
        }

        public void AddAsset(string assetUrl, string parentUrl)
        {
            if (AllNodes.TryGetValue(parentUrl, out SiteNode parent))
            {
                parent.AddAsset(assetUrl);
            }
        }

        public void AddForm(string formAction, string parentUrl)
        {
            if (AllNodes.TryGetValue(parentUrl, out SiteNode parent))
            {
                parent.AddForm(formAction);
            }
        }

        public void MarkAsProcessed(string url)
        {
            ProcessedUrls.Add(url);

            if (AllNodes.TryGetValue(url, out SiteNode node))
            {
                node.IsProcessed = true;
            }
        }

        public bool HasUrlsToProcess => UrlsToProcess.Count > 0;

        public string GetNextUrlToProcess()
        {
            return HasUrlsToProcess ? UrlsToProcess.Dequeue() : null;
        }

        public bool ShouldProcessUrl(string url)
        {
            if (ProcessedUrls.Contains(url))
            {
                return false;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            // Check if it's an internal URL (same domain as root)
            string host = uri.Host;
            string rootHost = new Uri(RootDomain.Url).Host;

            return host.Equals(rootHost, StringComparison.OrdinalIgnoreCase);
        }
    }
}
