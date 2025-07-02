using SentinelDAST.Models;

namespace SentinelDAST.Services {
    public class CrawlProgressEventArgs(int pagesProcessed, int totalPages, string? currentUrl) : EventArgs {
        public int PagesProcessed { get; } = pagesProcessed;
        public int TotalPages { get; } = totalPages;
        public string? CurrentUrl { get; } = currentUrl;
        public int Percentage { get; } = totalPages > 0 ? (int)((double)pagesProcessed / totalPages * 100) : 0;
    }

    public class CrawlCompletedEventArgs(
        SiteMap? siteMap,
        int pagesProcessed,
        int totalLinks,
        int totalAssets,
        TimeSpan duration,
        bool cancelled)
        : EventArgs {
        public SiteMap? SiteMap { get; } = siteMap;
        public int PagesProcessed { get; } = pagesProcessed;
        public int TotalLinks { get; } = totalLinks;
        public int TotalAssets { get; } = totalAssets;
        public TimeSpan Duration { get; } = duration;
        public bool Cancelled { get; } = cancelled;
    }

    public class PageCrawledEventArgs(string? url, CrawlResult result, SiteNode? node) : EventArgs {
        public string? Url { get; } = url;
        public CrawlResult Result { get; } = result;
        public SiteNode? Node { get; } = node;
    }
}
