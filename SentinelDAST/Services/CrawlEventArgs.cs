using System;
using SentinelDAST.Models;

namespace SentinelDAST.Services
{
    public class CrawlProgressEventArgs : EventArgs
    {
        public int PagesProcessed { get; }
        public int TotalPages { get; }
        public string CurrentUrl { get; }
        public int Percentage { get; }

        public CrawlProgressEventArgs(int pagesProcessed, int totalPages, string currentUrl)
        {
            PagesProcessed = pagesProcessed;
            TotalPages = totalPages;
            CurrentUrl = currentUrl;
            Percentage = totalPages > 0 ? (int)((double)pagesProcessed / totalPages * 100) : 0;
        }
    }

    public class CrawlCompletedEventArgs : EventArgs
    {
        public SiteMap SiteMap { get; }
        public int PagesProcessed { get; }
        public int TotalLinks { get; }
        public int TotalAssets { get; }
        public TimeSpan Duration { get; }
        public bool Cancelled { get; }

        public CrawlCompletedEventArgs(SiteMap siteMap, int pagesProcessed, int totalLinks, int totalAssets, TimeSpan duration, bool cancelled)
        {
            SiteMap = siteMap;
            PagesProcessed = pagesProcessed;
            TotalLinks = totalLinks;
            TotalAssets = totalAssets;
            Duration = duration;
            Cancelled = cancelled;
        }
    }

    public class PageCrawledEventArgs : EventArgs
    {
        public string Url { get; }
        public CrawlResult Result { get; }
        public SiteNode Node { get; }

        public PageCrawledEventArgs(string url, CrawlResult result, SiteNode node)
        {
            Url = url;
            Result = result;
            Node = node;
        }
    }
}
