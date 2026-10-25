using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using GalgameMetadata.Shared;

namespace GalgameMetadata.Playnite
{
    /// <summary>Settings shared by all five metadata plugins.</summary>
    public class PluginSettings : ISettings
    {
        /// <summary>"chinese" or "original".</summary>
        public string TitleLanguage { get; set; }
        /// <summary>"chinese" or "original".</summary>
        public string DescriptionLanguage { get; set; }
        public bool IncludeNsfwResults { get; set; }
        public bool DownloadCover { get; set; }
        public bool DownloadScreenshots { get; set; }
        public int MaxTags { get; set; }
        public bool AcceptBestGuess { get; set; }
        /// <summary>Ask the user to confirm the match and pick a background image before downloading.</summary>
        public bool AskManualSelection { get; set; }
        /// <summary>Personal API token, only used by the TouchGAL provider.</summary>
        public string TouchgalApiToken { get; set; }

        public PluginSettings()
        {
            TitleLanguage = "chinese";
            DescriptionLanguage = "chinese";
            IncludeNsfwResults = true;
            DownloadCover = true;
            DownloadScreenshots = true;
            MaxTags = 15;
            AcceptBestGuess = false;
            AskManualSelection = true;
        }

        public bool VerifySettings(out List<string> issues)
        {
            issues = new List<string>();
            if (MaxTags < 0) MaxTags = 0;
            if (MaxTags > 100) MaxTags = 100;
            return true;
        }

        public void BeginEdit()
        {
        }

        public void EndEdit()
        {
        }

        public void CancelEdit()
        {
        }

        public FetchOptions ToFetchOptions()
        {
            return new FetchOptions
            {
                PreferChineseTitle = !string.Equals(TitleLanguage, "original", StringComparison.OrdinalIgnoreCase),
                PreferChineseDescription = !string.Equals(DescriptionLanguage, "original", StringComparison.OrdinalIgnoreCase),
                IncludeNsfw = IncludeNsfwResults,
                MaxTags = MaxTags,
                DownloadCover = DownloadCover,
                DownloadScreenshots = DownloadScreenshots,
                AcceptBestGuess = AcceptBestGuess
            };
        }
    }

    /// <summary>Settings UI built in code, so no XAML/BAML build step is required.</summary>
    public class SettingsView : UserControl
    {
        private readonly Action _save;

        public SettingsView(PluginSettings settings, string siteName, string siteUrl)
            : this(settings, siteName, siteUrl, false, null)
        {
        }

        public SettingsView(PluginSettings settings, string siteName, string siteUrl, bool showApiToken)
            : this(settings, siteName, siteUrl, showApiToken, null)
        {
        }

        public SettingsView(PluginSettings settings, string siteName, string siteUrl, bool showApiToken, Action save)
        {
            _save = save;
            var panel = new StackPanel { Margin = new Thickness(14) };

            panel.Children.Add(new TextBlock
            {
                Text = siteName + " 元数据设置",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "数据来源：" + siteUrl,
                Opacity = 0.7,
                Margin = new Thickness(0, 0, 0, 12)
            });

            panel.Children.Add(ComboRow("标题语言", settings.TitleLanguage, new[]
            {
                new KeyValuePair<string, string>("chinese", "有中文时用中文"),
                new KeyValuePair<string, string>("original", "使用原标题")
            }, value => { settings.TitleLanguage = value; Save(); }));

            panel.Children.Add(ComboRow("简介语言", settings.DescriptionLanguage, new[]
            {
                new KeyValuePair<string, string>("chinese", "有中文时用中文"),
                new KeyValuePair<string, string>("original", "使用原文")
            }, value => { settings.DescriptionLanguage = value; Save(); }));

            panel.Children.Add(CheckRow("在搜索结果中包含成人（R18）作品", settings.IncludeNsfwResults,
                value => { settings.IncludeNsfwResults = value; Save(); }));

            panel.Children.Add(CheckRow("下载封面图片", settings.DownloadCover,
                value => { settings.DownloadCover = value; Save(); }));

            panel.Children.Add(CheckRow("下载背景图片", settings.DownloadScreenshots,
                value => { settings.DownloadScreenshots = value; Save(); }));

            panel.Children.Add(CheckRow("没有精确匹配时接受最相近的结果", settings.AcceptBestGuess,
                value => { settings.AcceptBestGuess = value; Save(); }));

            panel.Children.Add(CheckRow("每次刮削时手动确认匹配结果并选择背景图", settings.AskManualSelection,
                value => { settings.AskManualSelection = value; Save(); }));

            panel.Children.Add(new TextBlock
            {
                Text = "开启后，每次下载元数据都会先弹出窗口：上方确认是哪一个条目，下方从该站点的全部背景图里点选一张。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                Margin = new Thickness(0, 4, 0, 0)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "最大标签数量（0 = 不限制）",
                Margin = new Thickness(0, 10, 0, 2)
            });
            var tags = new TextBox
            {
                Text = settings.MaxTags.ToString(),
                Width = 60,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            tags.TextChanged += delegate
            {
                int parsed;
                if (int.TryParse(tags.Text, out parsed) && parsed >= 0 && parsed <= 100) { settings.MaxTags = parsed; Save(); }
            };
            panel.Children.Add(tags);

            if (showApiToken)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "TouchGAL API 令牌",
                    Margin = new Thickness(0, 14, 0, 2)
                });
                var token = new TextBox
                {
                    Text = settings.TouchgalApiToken ?? string.Empty,
                    Width = 320,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                token.TextChanged += delegate { settings.TouchgalApiToken = token.Text; Save(); };
                panel.Children.Add(token);
                panel.Children.Add(new TextBlock
                {
                    Text = "TouchGAL 的网页与网页 API 有 Cloudflare 挑战，普通请求会被 403 拦截，因此本插件改用官方开发者 API。" +
                           "请登录 https://developer.touchgal.com 创建 API 令牌并粘贴到这里。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                    Margin = new Thickness(0, 6, 0, 0)
                });
            }

            panel.Children.Add(new TextBlock
            {
                Text = "默认用游戏名在该站搜索。建议把该游戏的站点页面链接加到 Playnite 游戏的“链接”里，" +
                       "这样之后刮削会按 ID 精确匹配（例如 https://www.ymgal.games/ga22374）。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                Margin = new Thickness(0, 14, 0, 0)
            });

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panel
            };
        }

        private void Save()
        {
            if (_save == null) return;
            try
            {
                _save();
            }
            catch
            {
                // Saving settings is best effort; Playnite also persists them when the dialog closes.
            }
        }

        private static FrameworkElement CheckRow(string text, bool value, Action<bool> setter)
        {
            var box = new CheckBox
            {
                Content = text,
                IsChecked = value,
                Margin = new Thickness(0, 8, 0, 0)
            };
            box.Checked += delegate { setter(true); };
            box.Unchecked += delegate { setter(false); };
            return box;
        }

        private static FrameworkElement ComboRow(string label, string value,
            IEnumerable<KeyValuePair<string, string>> options, Action<string> setter)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });

            var combo = new ComboBox { Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
            var items = options.ToList();
            foreach (var option in items) combo.Items.Add(option.Value);
            var index = items.FindIndex(o => string.Equals(o.Key, value, StringComparison.OrdinalIgnoreCase));
            combo.SelectedIndex = index < 0 ? 0 : index;
            combo.SelectionChanged += delegate
            {
                var selected = combo.SelectedIndex;
                if (selected >= 0 && selected < items.Count) setter(items[selected].Key);
            };
            panel.Children.Add(combo);
            return panel;
        }
    }

    /// <summary>Fields every site provider can return.</summary>
    public static class FieldMap
    {
        public static readonly List<MetadataField> All = new List<MetadataField>
        {
            MetadataField.Name,
            MetadataField.Genres,
            MetadataField.ReleaseDate,
            MetadataField.Developers,
            MetadataField.Publishers,
            MetadataField.Tags,
            MetadataField.Description,
            MetadataField.Links,
            MetadataField.CommunityScore,
            MetadataField.CoverImage,
            MetadataField.BackgroundImage,
            MetadataField.AgeRating,
            MetadataField.Platform,
            MetadataField.Features,
            MetadataField.Series
        };
    }

    /// <summary>Base class with the Playnite boilerplate shared by all five plugins.</summary>
    public abstract class SiteMetadataPlugin : MetadataPlugin
    {
        protected readonly IPlayniteAPI Api;
        protected readonly ISiteSource Source;
        protected readonly PluginSettings Settings;
        protected readonly ILogger Log;

        protected SiteMetadataPlugin(IPlayniteAPI api, ISiteSource source) : base(api)
        {
            Api = api;
            Source = source;
            Log = LogManager.GetLogger("GalgameMetadata");
            Settings = LoadPluginSettings<PluginSettings>() ?? new PluginSettings();
            Properties = new MetadataPluginProperties { HasSettings = true };
        }

        public override string Name
        {
            get { return Source.Name; }
        }

        public override List<MetadataField> SupportedFields
        {
            get { return FieldMap.All; }
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override UserControl GetSettingsView(bool firstRunView)
        {
            return new SettingsView(Settings, Source.Name, Source.HomeUrl, ShowApiToken, delegate { SavePluginSettings(Settings); });
        }

        /// <summary>Only the TouchGAL provider needs a user supplied API token.</summary>
        protected virtual bool ShowApiToken
        {
            get { return false; }
        }

        public override OnDemandMetadataProvider GetMetadataProvider(MetadataRequestOptions options)
        {
            return new SiteProvider(Source, Settings, options, Api, Log);
        }
    }

    /// <summary>Resolves one Playnite game against one site and exposes the fields Playnite asks for.</summary>
    public class SiteProvider : OnDemandMetadataProvider
    {
        private readonly ISiteSource _source;
        private readonly PluginSettings _settings;
        private readonly MetadataRequestOptions _options;
        private readonly IPlayniteAPI _api;
        private readonly ILogger _log;

        private ScrapedGame _game;
        private Exception _error;
        private bool _loaded;

        public SiteProvider(ISiteSource source, PluginSettings settings, MetadataRequestOptions options,
            IPlayniteAPI api, ILogger log)
        {
            _source = source;
            _settings = settings;
            _options = options;
            _api = api;
            _log = log;
        }

        public override List<MetadataField> AvailableFields
        {
            get { return FieldMap.All; }
        }

        private ScrapedGame Load(CancellationToken cancel)
        {
            if (_loaded) return _game;
            _loaded = true;

            var name = _options.GameData == null ? null : _options.GameData.Name;
            var urls = _options.GameData == null || _options.GameData.Links == null
                ? null
                : _options.GameData.Links.Select(l => l.Url).ToList();

            try
            {
                var options = _settings.ToFetchOptions();
                var interactive = _settings.AskManualSelection && !_options.IsBackgroundDownload && _api != null;
                if (interactive)
                {
                    // Let the user pick the right search result and the screenshot used as background.
                    var api = _api;
                    var source = _source;
                    var log = _log;
                    var dispatcher = api.MainView == null ? null : api.MainView.UIDispatcher;
                    _log.Info(string.Format("{0}: asking the user to pick a result for \"{1}\" (onUiThread={2}).",
                        _source.SiteKey, name, dispatcher != null && dispatcher.CheckAccess()));
                    _game = PumpedRun.Run(dispatcher,
                        () => InteractiveResolver.Resolve(api, source, name, urls, options, cancel, log));
                }
                else
                {
                    _game = Task.Run(() => SourceResolver.ResolveAsync(_source, name, urls, options, cancel))
                        .GetAwaiter().GetResult();
                }

                // null means the user skipped this source on purpose, which is not an error.
                if (_game != null)
                {
                    _log.Info(string.Format("{0}: matched \"{1}\" to {2} ({3}).",
                        _source.SiteKey, name, _game.Name, _game.SiteUrl));
                }
            }
            catch (Exception ex)
            {
                _error = ex;
                _log.Error(ex, string.Format("{0}: download failed for \"{1}\".", _source.SiteKey, name));
                if (!(ex is OperationCanceledException)) ReportError(ex.Message);
            }
            return _game;
        }

        private void ReportError(string message)
        {
            if (_options.IsBackgroundDownload || _api == null) return;
            try
            {
                _api.Notifications.Add(new NotificationMessage(
                    _source.SiteKey + "-error",
                    _source.Name + ": " + message,
                    NotificationType.Error));
            }
            catch
            {
                // Notifications are best effort only.
            }
        }

        private static IEnumerable<MetadataProperty> Names(IEnumerable<string> values)
        {
            if (values == null) yield break;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in values)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var value = raw.Trim();
                if (value.Length == 0 || !seen.Add(value)) continue;
                yield return new MetadataNameProperty(value);
            }
        }

        private IEnumerable<MetadataProperty> Limited(IEnumerable<string> values)
        {
            var max = _settings.MaxTags;
            var list = values == null ? new List<string>() : values.ToList();
            if (max > 0 && list.Count > max) list = list.Take(max).ToList();
            return Names(list);
        }

        private MetadataFile Image(string url, bool enabled)
        {
            if (!enabled || string.IsNullOrWhiteSpace(url)) return null;
            byte[] bytes = null;
            try
            {
                bytes = Images.TryDownload(url, _source.HomeUrl, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                // Fall back to letting Playnite fetch the URL on its own.
            }

            var fileName = UrlUtil.FileNameFromUrl(url);
            if (bytes != null && bytes.Length > 0) return new MetadataFile(fileName, bytes, url);
            return new MetadataFile(url);
        }

        public override string GetName(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            return game == null ? null : Text.FirstNonEmpty(game.Name, game.ChineseName, game.OriginalName);
        }

        public override IEnumerable<MetadataProperty> GetGenres(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            return game == null ? null : Names(game.Genres);
        }

        public override ReleaseDate? GetReleaseDate(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            if (game == null || !game.Year.HasValue) return null;
            if (game.Month.HasValue && game.Day.HasValue)
                return new ReleaseDate(game.Year.Value, game.Month.Value, game.Day.Value);
            if (game.Month.HasValue) return new ReleaseDate(game.Year.Value, game.Month.Value);
            return new ReleaseDate(game.Year.Value);
        }

        public override IEnumerable<MetadataProperty> GetDevelopers(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            return game == null ? null : Names(game.Developers);
        }

        public override IEnumerable<MetadataProperty> GetPublishers(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            return game == null ? null : Names(game.Publishers);
        }

        public override IEnumerable<MetadataProperty> GetTags(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            return game == null ? null : Limited(game.Tags);
        }

        public override string GetDescription(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            // Playnite shows the description as HTML and ignores raw newlines, so emit <br> instead of "\n".
            return game == null ? null : Text.ToHtml(game.Description);
        }

        public override int? GetCommunityScore(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            return game == null ? null : game.CommunityScore;
        }

        public override MetadataFile GetCoverImage(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            if (game == null) return null;
            return Image(game.CoverUrl, _settings.DownloadCover);
        }

        public override MetadataFile GetBackgroundImage(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            if (game == null) return null;
            var url = !string.IsNullOrWhiteSpace(game.BannerUrl)
                ? game.BannerUrl
                : (game.Screenshots != null && game.Screenshots.Count > 0 ? game.Screenshots[0] : null);
            return Image(url, _settings.DownloadScreenshots);
        }

        public override IEnumerable<Link> GetLinks(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            if (game == null) yield break;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var link in game.Links)
            {
                if (link == null || string.IsNullOrWhiteSpace(link.Url)) continue;
                if (!seen.Add(link.Url)) continue;
                yield return new Link(string.IsNullOrWhiteSpace(link.Name) ? _source.Name : link.Name, link.Url);
            }
        }

        public override IEnumerable<MetadataProperty> GetAgeRatings(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            if (game == null || string.IsNullOrWhiteSpace(game.AgeRating)) return null;
            return Names(new[] { game.AgeRating });
        }

        public override IEnumerable<MetadataProperty> GetPlatforms(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            if (game == null) yield break;
            foreach (var platform in game.Platforms)
            {
                if (string.IsNullOrWhiteSpace(platform)) continue;
                yield return new MetadataSpecProperty(platform);
            }
        }

        public override IEnumerable<MetadataProperty> GetFeatures(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            return game == null ? null : Names(game.Features);
        }

        public override IEnumerable<MetadataProperty> GetSeries(GetMetadataFieldArgs args)
        {
            var game = Load(args.CancelToken);
            if (game == null || string.IsNullOrWhiteSpace(game.Series)) return null;
            return Names(new[] { game.Series });
        }

        public override void Dispose()
        {
            _game = null;
            _error = null;
            base.Dispose();
        }
    }
}
