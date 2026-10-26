using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GalgameMetadata.Shared;
using GalgameMetadata.Ymgal;
using GalgameMetadata.Kungal;
using GalgameMetadata.Hikarinagi;
using GalgameMetadata.Shionlib;
using GalgameMetadata.Touchgal;

namespace GalgameMetadata.Harness
{
    /// <summary>Console harness: runs the real scraping code outside Playnite.</summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var query = args.Length > 0 ? args[0] : "千恋＊万花";
            var only = args.Length > 1 ? args[1] : null;

            var sources = new List<ISiteSource>
            {
                new YmgalSource(),
                new KungalSource(),
                new HikarinagiSource(),
                new ShionlibSource(),
                new TouchgalSource()
            };

            var options = new FetchOptions
            {
                PreferChineseTitle = true,
                PreferChineseDescription = true,
                IncludeNsfw = true,
                MaxTags = 15,
                DownloadCover = false,
                DownloadScreenshots = false,
                AcceptBestGuess = false
            };

            var failures = 0;
            foreach (var source in sources)
            {
                if (only != null && !string.Equals(only, source.SiteKey, StringComparison.OrdinalIgnoreCase)) continue;
                Console.WriteLine();
                Console.WriteLine("=== " + source.Name + " ===");
                try
                {
                    var started = DateTime.UtcNow;
                    var hits = source.SearchAsync(query, 5, options, CancellationToken.None).GetAwaiter().GetResult();
                    Console.WriteLine(string.Format("search hits: {0} in {1} ms", hits.Count, (int)(DateTime.UtcNow - started).TotalMilliseconds));
                    for (var i = 0; i < hits.Count && i < 5; i++)
                    {
                        Console.WriteLine(string.Format("  [{0}] id={1} title={2} alt={3} dev={4} year={5}",
                            i, hits[i].Id, hits[i].Title, hits[i].AltTitle, hits[i].Developer, hits[i].Year));
                    }
                    if (hits.Count == 0) { failures++; continue; }

                    var detailStart = DateTime.UtcNow;
                    var game = source.GetAsync(hits[0], options, CancellationToken.None).GetAwaiter().GetResult();
                    PrintGame(game, (int)(DateTime.UtcNow - detailStart).TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine("FAILED: " + ex.GetType().Name + ": " + ex.Message);
                    if (ex.InnerException != null) Console.WriteLine("  inner: " + ex.InnerException.Message);
                }
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ALL SOURCES OK" : failures + " source(s) failed");
            return failures == 0 ? 0 : 1;
        }

        private static void PrintGame(ScrapedGame g, int ms)
        {
            Console.WriteLine(string.Format("detail in {0} ms", ms));
            Console.WriteLine("  name        : " + g.Name);
            Console.WriteLine("  original    : " + g.OriginalName);
            Console.WriteLine("  chinese     : " + g.ChineseName);
            Console.WriteLine("  aliases     : " + string.Join(" | ", g.Aliases));
            Console.WriteLine("  release     : " + g.Year + "-" + g.Month + "-" + g.Day);
            Console.WriteLine("  developers  : " + string.Join(" | ", g.Developers));
            Console.WriteLine("  publishers  : " + string.Join(" | ", g.Publishers));
            Console.WriteLine("  genres      : " + string.Join(" | ", g.Genres));
            Console.WriteLine("  platforms   : " + string.Join(" | ", g.Platforms));
            Console.WriteLine("  features    : " + string.Join(" | ", g.Features));
            Console.WriteLine("  score       : " + g.CommunityScore + " (raw " + g.RawScore + ")");
            Console.WriteLine("  age         : " + g.AgeRating + "  nsfw=" + g.Nsfw);
            Console.WriteLine("  tags (" + g.Tags.Count + ")   : " + string.Join(" | ", g.Tags));
            Console.WriteLine("  cover       : " + g.CoverUrl);
            Console.WriteLine("  banner      : " + g.BannerUrl);
            Console.WriteLine("  shots (" + g.Screenshots.Count + ")   : " + (g.Screenshots.Count > 0 ? g.Screenshots[0] : "-"));
            Console.WriteLine("  site        : " + g.SiteUrl);
            Console.WriteLine("  links       : " + string.Join(" | ", g.Links.ConvertAll(l => l.Name + "=" + l.Url)));
            var desc = g.Description ?? string.Empty;
            Console.WriteLine("  description : " + (desc.Length > 220 ? desc.Substring(0, 220) + "..." : desc).Replace("\r", " ").Replace("\n", " "));
        }
    }
}
