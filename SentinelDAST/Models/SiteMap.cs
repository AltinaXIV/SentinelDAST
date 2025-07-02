namespace SentinelDAST.Models {
    public class SiteNode {
        public string? Url { get; }
        Uri? Uri { get; }
        public string? DisplayName { get; }
        public NodeType Type { get; }

        // Collection of child nodes representing the hierarchical structure of the site
        // Used to build the site map tree and maintain parent-child relationships
        Dictionary<string, SiteNode> Children { get; }
        HashSet<string?> Assets { get; }
        HashSet<string?> Forms { get; }

        public SiteNode(string? url, NodeType type, string? displayName = null) {
            Url = url;
            Type = type;

            // Make sure we can create a valid URI
            if(Uri.TryCreate(url, UriKind.Absolute, out var parsedUri)) {
                Uri = parsedUri;
                DisplayName = displayName ?? GetDisplayName();
            }
            else {
                // For special cases like category folders with non-URL identifiers
                Uri = null;
                DisplayName = displayName ?? url;
            }

            Children = new Dictionary<string, SiteNode>();
            Assets = [];
            Forms = [];
        }

        string? GetDisplayName() {
            if(Uri == null) {
                return Type == NodeType.CategoryFolder ? Url : "Invalid URL";
            }

            switch (Type) {
                case NodeType.Domain:
                    return Uri.Host;
                case NodeType.Page:
                    var path = Uri.AbsolutePath;
                    if(string.IsNullOrEmpty(path) || path == "/") {
                        return "/" + (string.IsNullOrEmpty(Uri.Fragment) ? "" : Uri.Fragment);
                    }

                    return Uri.PathAndQuery + Uri.Fragment;
                case NodeType.ExternalDomain:
                    return Uri.Host;
                case NodeType.Asset:
                    return System.IO.Path.GetFileName(Uri.LocalPath);
                case NodeType.CategoryFolder:
                default:
                    return Url;
            }
        }

        public void AddChild(SiteNode child) {
            if(child.Url != null) {
                Children.TryAdd(child.Url, child);
            }
        }

        public void AddAsset(string? assetUrl) {
            Assets.Add(assetUrl);
        }

        public void AddForm(string? formAction) {
            Forms.Add(formAction);
        }

        public IReadOnlyCollection<string?> GetAssets() {
            return Assets.ToList().AsReadOnly();
        }

        public IReadOnlyCollection<string?> GetForms() {
            return Forms.ToList().AsReadOnly();
        }

        public IReadOnlyDictionary<string, SiteNode> GetChildren() {
            return new Dictionary<string, SiteNode>(Children);
        }

        public override string? ToString() {
            return DisplayName;
        }
    }

    public enum NodeType {
        Domain,
        Page,
        ExternalDomain,
        CategoryFolder, // For grouping (e.g., "External Links", "Assets")
        Asset
    }

    public class SiteMap {
        public SiteNode RootDomain { get; }
        SiteNode ExternalLinks { get; }
        public Dictionary<string, SiteNode> AllNodes { get; }
        public HashSet<string?> ProcessedUrls { get; }
        public Queue<string?> UrlsToProcess { get; }

        public SiteMap(string? rootUrl) {
            if(!Uri.TryCreate(rootUrl, UriKind.Absolute, out var rootUri)) {
                throw new ArgumentException("Invalid root URL format", nameof(rootUrl));
            }

            // Create the root domain node
            var domainUrl = $"{rootUri.Scheme}://{rootUri.Host}";
            RootDomain = new SiteNode(domainUrl, NodeType.Domain);

            // Create the external links category
            ExternalLinks = new SiteNode("external_links", NodeType.CategoryFolder, "External Links");

            AllNodes = new Dictionary<string, SiteNode> {
                { domainUrl, RootDomain },
                { "external_links", ExternalLinks }
            };

            ProcessedUrls = [];
            UrlsToProcess = new Queue<string?>();

            // Queue the initial URL for processing
            UrlsToProcess.Enqueue(rootUrl);
        }

        public void AddPage(string? url, string? parentUrl = null) {
            if(url != null && AllNodes.ContainsKey(url)) return;

            // Create a new page node
            var node = new SiteNode(url, NodeType.Page);
            if(url == null) {
                return;
            }

            AllNodes.Add(url, node);

            // Determine the parent node
            if(parentUrl != null && AllNodes.TryGetValue(parentUrl, out var parent)) {
                parent.AddChild(node);
            }
            else {
                // If no parent is specified or found, add to the root domain
                RootDomain.AddChild(node);
            }

            // Queue for processing if not already processed
            if(!ProcessedUrls.Contains(url)) {
                UrlsToProcess.Enqueue(url);
            }
        }

        public void AddExternalLink(string? url, string? sourceUrl) {
            if(url != null && AllNodes.ContainsKey(url)) return;

            if(!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                return;
            }

            // Create an external domain node if it doesn't exist
            var domainUrl = $"{uri.Scheme}://{uri.Host}";
            if(!AllNodes.TryGetValue(domainUrl, out var domainNode)) {
                domainNode = new SiteNode(domainUrl, NodeType.ExternalDomain);
                AllNodes.Add(domainUrl, domainNode);
                ExternalLinks.AddChild(domainNode);
            }

            // Create the external page node
            var node = new SiteNode(url, NodeType.Page);
            AllNodes.Add(url, node);
            domainNode.AddChild(node);
        }

        public void AddAsset(string? assetUrl, string? parentUrl) {
            if(parentUrl != null && AllNodes.TryGetValue(parentUrl, out var parent)) {
                parent.AddAsset(assetUrl);
            }
        }

        public void AddForm(string? formAction, string? parentUrl) {
            if(parentUrl != null && AllNodes.TryGetValue(parentUrl, out var parent)) {
                parent.AddForm(formAction);
            }
        }

        public void MarkAsProcessed(string? url) {
            ProcessedUrls.Add(url);

            if(url != null && AllNodes.TryGetValue(url, out _)) {
            }
        }

        public bool HasUrlsToProcess => UrlsToProcess.Count > 0;

        public string? GetNextUrlToProcess() {
            return HasUrlsToProcess ? UrlsToProcess.Dequeue() : null;
        }

        public bool ShouldProcessUrl(string? url) {
            if(ProcessedUrls.Contains(url)) {
                return false;
            }

            if(!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                return false;
            }

// Collection of form action URLs discovered during crawling
// Will be used for form analysis and security testing features
            // Check if it's an internal URL (same domain as root)
            var host = uri.Host;
            if(RootDomain.Url == null) {
                return false;
            }

            var rootHost = new Uri(RootDomain.Url).Host;

            return host.Equals(rootHost, StringComparison.OrdinalIgnoreCase);

        }
    }
}
