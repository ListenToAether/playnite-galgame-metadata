using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using GalgameMetadata.Playnite;
using GalgameMetadata.Shared;
using GalgameMetadata.Ymgal;
using GalgameMetadata.Kungal;
using GalgameMetadata.Hikarinagi;
using GalgameMetadata.Shionlib;
using GalgameMetadata.Touchgal;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace GalgameMetadata.Tools
{
    internal sealed class ConsoleLogger : ILogger
    {
        public void Info(string message) { }
        public void Info(Exception exception, string message) { }
        public void Debug(string message) { }
        public void Debug(Exception exception, string message) { }
        public void Warn(string message) { }
        public void Warn(Exception exception, string message) { }
        public void Error(string message) { Console.WriteLine("  LOG ERROR: " + message); }
        public void Error(Exception exception, string message) { Console.WriteLine("  LOG ERROR: " + message + " :: " + exception.Message); }
        public void Trace(string message) { }
        public void Trace(Exception exception, string message) { }
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var query = args.Length > 0 ? args[0] : "千恋＊万花";
            var linkUrl = args.Length > 1 ? args[1] : null;
            var only = args.Length > 2 ? args[2] : null;

            var game = new Game { Name = query };
            if (!string.IsNullOrEmpty(linkUrl))
            {
                game.Links = new ObservableCollection<Link> { new Link("Site", linkUrl) };
            }

            var options = new MetadataRequestOptions(game, false);
            var fieldArgs = new GetMetadataFieldArgs();

            var sources = new ISiteSource[]
            {
                new YmgalSource(),
                new KungalSource(),
                new HikarinagiSource(),
                new ShionlibSource(),
                new TouchgalSource()
            };

            var settings = new PluginSettings();
            var logger = new ConsoleLogger();
            var failed = 0;

            foreach (var source in sources)
            {
                if (only != null && !string.Equals(only, source.SiteKey, StringComparison.OrdinalIgnoreCase)) continue;
                Console.WriteLine("=== " + source.SiteKey + " ===");
                var provider = new SiteProvider(source, settings, options, null, logger);
                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var name = provider.GetName(fieldArgs);
                    watch.Stop();
                    Console.WriteLine("  name        : " + name);
                    Console.WriteLine("  elapsed     : " + watch.ElapsedMilliseconds + " ms");

                    var tags = provider.GetTags(fieldArgs);
                    Console.WriteLine("  tags        : " + Join(tags));
                    Console.WriteLine("  genres      : " + Join(provider.GetGenres(fieldArgs)));
                    Console.WriteLine("  developers  : " + Join(provider.GetDevelopers(fieldArgs)));
                    Console.WriteLine("  publishers  : " + Join(provider.GetPublishers(fieldArgs)));
                    Console.WriteLine("  features    : " + Join(provider.GetFeatures(fieldArgs)));
                    Console.WriteLine("  platforms   : " + Join(provider.GetPlatforms(fieldArgs)));
                    Console.WriteLine("  age         : " + Join(provider.GetAgeRatings(fieldArgs)));
                    Console.WriteLine("  score       : " + provider.GetCommunityScore(fieldArgs));
                    Console.WriteLine("  release     : " + FormatDate(provider.GetReleaseDate(fieldArgs)));
                    var links = provider.GetLinks(fieldArgs).ToList();
                    Console.WriteLine("  links (" + links.Count + ")   : " + string.Join(" | ", links.Select(l => l.Name + "=" + l.Url)));

                    var cover = provider.GetCoverImage(fieldArgs);
                    Console.WriteLine("  cover       : " + Describe(cover));
                    var background = provider.GetBackgroundImage(fieldArgs);
                    Console.WriteLine("  background  : " + Describe(background));
                    var description = provider.GetDescription(fieldArgs);
                    Console.WriteLine("  description : " + (description == null ? "(null)" : description.Length + " chars, " +
                        (description.Contains('\n') ? "multi-line" : "single-line")));
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("  FAILED: " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    provider.Dispose();
                }
                Console.WriteLine();
            }

            return failed;
        }

        private static string Join(IEnumerable<MetadataProperty> values)
        {
            if (values == null) return "(null)";
            var list = values.ToList();
            if (list.Count == 0) return "(empty)";
            return string.Join(" | ", list.Select(v =>
                v is MetadataNameProperty ? ((MetadataNameProperty)v).Name :
                v is MetadataSpecProperty ? "[spec]" + ((MetadataSpecProperty)v).Id :
                v.GetType().Name));
        }

        private static string FormatDate(ReleaseDate? date)
        {
            if (date == null) return "(null)";
            return date.Value.Year + "/" + date.Value.Month + "/" + date.Value.Day;
        }

        private static string Describe(MetadataFile file)
        {
            if (file == null) return "(null)";
            return "path=" + file.Path + " bytes=" + (file.Content == null ? 0 : file.Content.Length) +
                   " fileName=" + file.FileName;
        }
    }
}
