using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Playnite.SDK;
using GalgameMetadata.Shared;

namespace GalgameMetadata.Playnite
{
    /// <summary>What the user picked in the manual selection window.</summary>
    public class ManualSelection
    {
        public ScrapedGame Game { get; set; }
        public string BackgroundUrl { get; set; }
    }

    /// <summary>
    /// Runs a blocking task on a worker thread while still pumping the UI dispatcher,
    /// which is required because the worker asks the UI thread to show a modal window.
    /// </summary>
    public static class PumpedRun
    {
        public static T Run<T>(Dispatcher dispatcher, Func<T> work)
        {
            if (dispatcher == null || !dispatcher.CheckAccess())
            {
                return Task.Run(work).GetAwaiter().GetResult();
            }

            T result = default(T);
            Exception error = null;
            var frame = new DispatcherFrame();
            var task = Task.Run(() =>
            {
                try
                {
                    result = work();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    dispatcher.BeginInvoke(new Action(() => { frame.Continue = false; }));
                }
            });

            Dispatcher.PushFrame(frame);
            task.GetAwaiter().GetResult();
            if (error != null) throw error;
            return result;
        }
    }

    /// <summary>Search plus manual confirmation, modelled after the VNDB Nexus provider.</summary>
    public static class InteractiveResolver
    {
        public static ScrapedGame Resolve(IPlayniteAPI api, ISiteSource source, string gameName,
            IEnumerable<string> existingUrls, FetchOptions options, CancellationToken cancel, ILogger log)
        {
            var dispatcher = api == null || api.MainView == null ? null : api.MainView.UIDispatcher;
            return ResolveCore(dispatcher, source, gameName, existingUrls, options, cancel, log);
        }

        internal static ScrapedGame ResolveCore(Dispatcher dispatcher, ISiteSource source, string gameName,
            IEnumerable<string> existingUrls, FetchOptions options, CancellationToken cancel, ILogger log)
        {
            var query = gameName == null ? string.Empty : gameName.Trim();

            // An existing link to the site is authoritative, but it only preselects the candidate:
            // the user confirms on every scrape so a wrong match can be corrected next time.
            var hits = new List<SearchHit>();
            string linkId = null;
            if (existingUrls != null)
            {
                foreach (var url in existingUrls)
                {
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    string id;
                    if (source.TryParseId(url, out id))
                    {
                        linkId = id;
                        break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(linkId))
            {
                hits.Add(new SearchHit
                {
                    Id = linkId,
                    Title = query.Length == 0 ? source.PageUrl(linkId) : query,
                    Url = source.PageUrl(linkId)
                });
            }

            if (query.Length > 0)
            {
                try
                {
                    var found = source.SearchAsync(query, 15, options, cancel).GetAwaiter().GetResult();
                    if (found != null)
                    {
                        foreach (var hit in found)
                        {
                            if (string.IsNullOrWhiteSpace(hit.Id)) continue;
                            if (hits.Exists(h => string.Equals(h.Id, hit.Id, StringComparison.Ordinal))) continue;
                            hits.Add(hit);
                        }
                    }
                }
                catch (ScrapeException)
                {
                    // A failed search is only fatal when there is no existing link to fall back on.
                    if (hits.Count == 0) throw;
                }
            }

            if (hits.Count == 0)
                throw new ScrapeException(string.Format("{0} 没有搜索到“{1}”的结果。", source.Name, query));

            var preselectId = linkId;
            if (string.IsNullOrWhiteSpace(preselectId))
            {
                var best = Text.BestIndex(hits, h => new[] { h.Title, h.AltTitle }, query, 0.45);
                preselectId = best >= 0 ? hits[best].Id : (hits.Count == 1 ? hits[0].Id : null);
            }

            var selection = (ManualSelection)null;
            var pickerFailed = false;
            try
            {
                selection = ManualPicker.Pick(dispatcher, source, query, hits, options, cancel, preselectId, log);
            }
            catch (Exception ex)
            {
                // If the window cannot be shown (no UI thread, closing Playnite, ...) fall back to automatic matching
                // instead of failing the whole download.
                pickerFailed = true;
                if (log != null) log.Error(ex, string.Format("{0}: manual selection window failed, using automatic matching.", source.SiteKey));
            }

            if (pickerFailed)
            {
                return SourceResolver.ResolveAsync(source, query, existingUrls, options, cancel).GetAwaiter().GetResult();
            }

            if (selection == null || selection.Game == null)
            {
                // User skipped this source (or closed the window): give Playnite no data instead of guessing.
                return null;
            }

            var game = selection.Game;
            if (string.IsNullOrWhiteSpace(selection.BackgroundUrl))
            {
                game.BannerUrl = null;
                if (game.Screenshots != null) game.Screenshots.Clear();
            }
            else
            {
                game.BannerUrl = selection.BackgroundUrl;
            }

            if (log != null)
            {
                log.Info(string.Format("{0}: user picked \"{1}\" for \"{2}\".", source.SiteKey, game.Name, query));
            }
            return game;
        }
    }

    /// <summary>Modal window: pick the right search result on the left, pick a background on the right.</summary>
    internal static class ManualPicker
    {
        /// <summary>Test hook: invoked on the UI thread right after the window was created.</summary>
        internal static Action<SelectionWindow> OnWindowCreated { get; set; }

        public static ManualSelection Pick(Dispatcher dispatcher, ISiteSource source, string gameName,
            List<SearchHit> hits, FetchOptions options, CancellationToken cancel, string preselectId, ILogger log)
        {
            if (hits == null || hits.Count == 0) return null;

            ManualSelection selection = null;
            Action show = delegate
            {
                var window = new SelectionWindow(source, gameName, hits, options, cancel, preselectId, log);
                var created = OnWindowCreated;
                if (created != null) created(window);
                var ok = window.ShowDialog();
                if (ok == true) selection = window.Selection;
            };

            if (dispatcher == null || dispatcher.CheckAccess()) show();
            else dispatcher.Invoke(show);
            return selection;
        }
    }

    internal class SelectionWindow : Window
    {
        private sealed class Candidate
        {
            public SearchHit Hit;
            public ScrapedGame Game;
            public bool Loading;
            public bool Loaded;
        }

        private readonly ISiteSource _source;
        private readonly string _gameName;
        private readonly List<SearchHit> _hits;
        private readonly FetchOptions _options;
        private readonly CancellationToken _cancel;
        private readonly ILogger _log;

        private readonly List<Candidate> _candidates = new List<Candidate>();
        private readonly ListBox _list = new ListBox();
        private readonly WrapPanel _backgrounds = new WrapPanel { Margin = new Thickness(0) };
        private readonly TextBlock _info = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = Theme.Text
        };
        private readonly TextBlock _status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Foreground = Theme.TextDim,
            VerticalAlignment = VerticalAlignment.Center
        };
        private readonly Button _ok = new Button
        {
            Content = "使用这个条目",
            Width = 130,
            Height = 30,
            IsEnabled = false,
            Margin = new Thickness(8, 0, 0, 0),
            Style = Theme.PrimaryButton
        };
        private readonly CheckBox _useBackground = new CheckBox
        {
            Content = "下载背景图（可在右侧点选）",
            IsChecked = true,
            Foreground = Theme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            Style = Theme.DarkCheckBox
        };
        private readonly Dictionary<string, BitmapImage> _imageCache = new Dictionary<string, BitmapImage>();

        private string _selectedBackground;
        private Border _selectedBorder;
        private int _current = -1;

        public ManualSelection Selection { get; private set; }

        public SelectionWindow(ISiteSource source, string gameName, List<SearchHit> hits,
            FetchOptions options, CancellationToken cancel, string preselectId, ILogger log)
        {
            _source = source;
            _gameName = gameName;
            _hits = hits;
            _options = options;
            _cancel = cancel;
            _log = log;

            Title = source.Name + " - 选择匹配的条目";
            Width = 1000;
            Height = 700;
            MinWidth = 760;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            Background = Theme.Window;
            ShowInTaskbar = false;
            UseLayoutRounding = true;

            // Without WindowChrome Windows paints its own light DWM frame on the top edge of a borderless
            // resizable window, which is the white strip above the title bar.
            var chrome = new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            };
            WindowChrome.SetWindowChrome(this, chrome);

            Content = new Border
            {
                Background = Theme.Window,
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(1),
                Child = BuildContent(gameName)
            };

            Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Theme.ScrollBar);

            foreach (var hit in hits)
            {
                _candidates.Add(new Candidate { Hit = hit });
            }

            var preselect = 0;
            if (!string.IsNullOrEmpty(preselectId))
            {
                var index = hits.FindIndex(h => string.Equals(h.Id, preselectId, StringComparison.Ordinal));
                if (index >= 0) preselect = index;
            }
            _list.SelectedIndex = preselect;

            Loaded += delegate
            {
                if (_list.SelectedIndex < 0) _list.SelectedIndex = 0;
                LoadCandidate(_list.SelectedIndex);
            };
        }

        private FrameworkElement BuildContent(string gameName)
        {
            var root = new DockPanel();

            var header = new Border
            {
                Background = Theme.Header,
                Padding = new Thickness(16, 10, 8, 10),
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };

            var headerGrid = new DockPanel();
            var close = new Button
            {
                Content = "✕",
                Width = 34,
                Height = 26,
                Style = Theme.CloseButton,
                ToolTip = "关闭（不选这个来源）"
            };
            close.Click += delegate { DialogResult = false; };
            DockPanel.SetDock(close, Dock.Right);
            headerGrid.Children.Add(close);

            var headerPanel = new StackPanel();
            headerPanel.Children.Add(new TextBlock
            {
                Text = "从 " + _source.Name + " 选择要使用的条目",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = Theme.Text
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = "Playnite 里的游戏名：" + gameName + "    数据源：" + _source.HomeUrl,
                Opacity = 0.7,
                Foreground = Theme.TextDim,
                Margin = new Thickness(0, 3, 0, 0)
            });
            headerGrid.Children.Add(headerPanel);
            header.Child = headerGrid;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var footer = new Border
            {
                Background = Theme.Footer,
                Padding = new Thickness(16, 10, 16, 10),
                BorderBrush = Theme.Border,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            var footerPanel = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelButton = new Button { Content = "跳过这个来源", Width = 120, Height = 30, Style = Theme.Button };
            cancelButton.Click += delegate { DialogResult = false; };
            _ok.Click += delegate { Confirm(); };
            buttons.Children.Add(_useBackground);
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(_ok);
            buttons.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(buttons, Dock.Right);
            footerPanel.Children.Add(buttons);
            footerPanel.Children.Add(_status);
            footer.Child = footerPanel;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var split = new Grid();
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _list.Background = Theme.List;
            _list.Foreground = Theme.Text;
            _list.BorderThickness = new Thickness(0, 0, 1, 0);
            _list.BorderBrush = Theme.Border;
            _list.ItemContainerStyle = Theme.ListItem;
            _list.SelectionChanged += delegate { LoadCandidate(_list.SelectedIndex); };
            foreach (var hit in _hits)
            {
                _list.Items.Add(BuildCandidateItem(hit));
            }
            Grid.SetColumn(_list, 0);
            split.Children.Add(_list);

            var right = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
            DockPanel.SetDock(_info, Dock.Top);
            right.Children.Add(_info);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _backgrounds
            };
            right.Children.Add(scroll);
            Grid.SetColumn(right, 1);
            split.Children.Add(right);

            root.Children.Add(split);
            return root;
        }

        private ListBoxItem BuildCandidateItem(SearchHit hit)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 4, 6) };

            var image = new Image { Width = 62, Height = 88, Stretch = Stretch.UniformToFill };
            var frame = new Border
            {
                Width = 62,
                Height = 88,
                Child = image,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22))
            };
            panel.Children.Add(frame);
            if (!string.IsNullOrWhiteSpace(hit.CoverUrl)) LoadImage(image, hit.CoverUrl);

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 272 };
            texts.Children.Add(new TextBlock
            {
                Text = Text.FirstNonEmpty(hit.Title, hit.AltTitle, "(无标题)"),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 272
            });
            if (!string.IsNullOrWhiteSpace(hit.AltTitle))
            {
                texts.Children.Add(new TextBlock
                {
                    Text = hit.AltTitle,
                    Opacity = 0.65,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 272
                });
            }
            var meta = new List<string>();
            if (!string.IsNullOrWhiteSpace(hit.Developer)) meta.Add(hit.Developer);
            if (hit.Year.HasValue) meta.Add(hit.Year.Value.ToString());
            var titleScore = Math.Max(Text.Similarity(_gameName, hit.Title),
                Text.Similarity(_gameName, Text.FirstNonEmpty(hit.AltTitle, hit.Title)));
            meta.Add(string.Format("匹配度 {0:0}%", titleScore * 100));
            texts.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", meta.Where(m => !string.IsNullOrEmpty(m)).ToArray()),
                Opacity = 0.6,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 272
            });
            panel.Children.Add(texts);

            return new ListBoxItem { Content = panel, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        }

        private void LoadCandidate(int index)
        {
            if (index < 0 || index >= _candidates.Count) return;
            _current = index;
            var candidate = _candidates[index];
            if (candidate.Loaded)
            {
                FillBackgrounds(candidate);
                return;
            }
            if (candidate.Loading) return;

            candidate.Loading = true;
            _ok.IsEnabled = false;
            _status.Text = "正在获取该条目的资料和背景图…";

            Task.Run(delegate
            {
                try
                {
                    var game = _source.GetAsync(candidate.Hit, _options, _cancel).GetAwaiter().GetResult();
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        candidate.Loading = false;
                        candidate.Loaded = true;
                        candidate.Game = game;
                        if (_current == index) FillBackgrounds(candidate);
                    }));
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        candidate.Loading = false;
                        if (_current == index)
                        {
                            _status.Text = "获取失败：" + ex.Message;
                            _ok.IsEnabled = false;
                        }
                    }));
                }
            });
        }

        private void FillBackgrounds(Candidate candidate)
        {
            var game = candidate.Game;
            _backgrounds.Children.Clear();
            _selectedBackground = null;
            _selectedBorder = null;
            _ok.IsEnabled = false;

            if (game == null)
            {
                _status.Text = "没有取到资料。";
                return;
            }

            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(game.Name)) details.Add(game.Name);
            if (!string.IsNullOrWhiteSpace(game.OriginalName) && game.OriginalName != game.Name) details.Add("原名：" + game.OriginalName);
            if (game.Developers != null && game.Developers.Count > 0) details.Add("开发商：" + string.Join("、", game.Developers.Distinct().ToArray()));
            if (game.Publishers != null && game.Publishers.Count > 0) details.Add("发行商：" + string.Join("、", game.Publishers.Distinct().ToArray()));
            if (game.Year.HasValue)
            {
                details.Add("发售：" + (game.Month.HasValue
                    ? string.Format("{0}-{1:00}", game.Year.Value, game.Month.Value)
                    : game.Year.Value.ToString()));
            }
            if (game.Tags != null && game.Tags.Count > 0) details.Add("标签：" + string.Join("、", game.Tags.Take(10).ToArray()));
            _info.Text = string.Join("\n", details.ToArray());

            var urls = new List<string>();
            if (!string.IsNullOrWhiteSpace(game.BannerUrl)) urls.Add(game.BannerUrl);
            if (game.Screenshots != null)
            {
                foreach (var shot in game.Screenshots)
                {
                    if (!string.IsNullOrWhiteSpace(shot) && !urls.Contains(shot)) urls.Add(shot);
                }
            }

            if (urls.Count == 0)
            {
                _status.Text = "该站点没有提供背景图片，将只下载文字资料和封面。";
                _ok.IsEnabled = true;
                _selectedBackground = null;
                _useBackground.IsChecked = false;
                return;
            }

            _useBackground.IsChecked = true;
            foreach (var url in urls)
            {
                _backgrounds.Children.Add(BuildThumbnail(url));
            }
            _status.Text = string.Format("共 {0} 张背景图，点选一张后按“使用这个条目”。", urls.Count);
            _ok.IsEnabled = true;
        }

        private FrameworkElement BuildThumbnail(string url)
        {
            var image = new Image { Width = 300, Height = 169, Stretch = Stretch.UniformToFill };
            var border = new Border
            {
                Width = 308,
                Height = 177,
                Margin = new Thickness(0, 0, 8, 8),
                BorderThickness = new Thickness(2),
                BorderBrush = Theme.Border,
                Background = Theme.Thumb,
                Child = image,
                Tag = url,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = url
            };
            border.MouseLeftButtonUp += delegate
            {
                if (_selectedBorder != null) Highlight(_selectedBorder, false);
                _selectedBorder = border;
                _selectedBackground = url;
                Highlight(border, true);
            };
            LoadImage(image, url);
            if (_selectedBorder == null)
            {
                // First image is the default choice.
                _selectedBorder = border;
                _selectedBackground = url;
                Highlight(border, true);
            }
            return border;
        }

        private static void Highlight(Border border, bool selected)
        {
            border.BorderBrush = selected ? Theme.Accent : Theme.Border;
            border.BorderThickness = new Thickness(selected ? 3 : 2);
        }

        private void LoadImage(Image target, string url)
        {
            BitmapImage cached;
            if (_imageCache.TryGetValue(url, out cached))
            {
                target.Source = cached;
                return;
            }

            Task.Run(delegate
            {
                try
                {
                    var bytes = Images.TryDownload(url, _source.HomeUrl, _cancel).GetAwaiter().GetResult();
                    if (bytes == null || bytes.Length == 0) return;
                    var bitmap = new BitmapImage();
                    using (var stream = new MemoryStream(bytes))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze();
                    }
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _imageCache[url] = bitmap;
                        target.Source = bitmap;
                    }));
                }
                catch
                {
                    // A missing thumbnail is not worth interrupting the user.
                }
            });
        }

        private void Confirm()
        {
            var candidate = _current >= 0 && _current < _candidates.Count ? _candidates[_current] : null;
            if (candidate == null || candidate.Game == null) return;

            Selection = new ManualSelection
            {
                Game = candidate.Game,
                BackgroundUrl = _useBackground.IsChecked == true ? _selectedBackground : null
            };
            DialogResult = true;
        }
    }

    /// <summary>Dark palette + control templates for the manual selection window (no XAML).</summary>
    internal static class Theme
    {
        public static readonly Brush Window = Brush(0x1b, 0x1c, 0x1f);
        public static readonly Brush Header = Brush(0x24, 0x26, 0x2b);
        public static readonly Brush Footer = Brush(0x20, 0x22, 0x26);
        public static readonly Brush List = Brush(0x1e, 0x1f, 0x23);
        public static readonly Brush Thumb = Brush(0x26, 0x28, 0x2d);
        public static readonly Brush Border = Brush(0x3a, 0x3c, 0x42);
        public static readonly Brush Text = Brush(0xe8, 0xe8, 0xea);
        public static readonly Brush TextDim = Brush(0xa2, 0xa4, 0xac);
        public static readonly Brush Accent = Brush(0x3b, 0x9d, 0xff);
        public static readonly Brush ItemHover = Brush(0x2b, 0x2d, 0x33);
        public static readonly Brush ItemSelected = Brush(0x31, 0x3f, 0x59);

        public static readonly Style Button = CreateButton(
            Color.FromRgb(0x2f, 0x31, 0x38), Color.FromRgb(0x3a, 0x3d, 0x45),
            Color.FromRgb(0x46, 0x48, 0x50), Color.FromRgb(0xe8, 0xe8, 0xea));

        public static readonly Style PrimaryButton = CreateButton(
            Color.FromRgb(0x0e, 0x5c, 0x9e), Color.FromRgb(0x13, 0x72, 0xc4),
            Color.FromRgb(0x2f, 0x8b, 0xd8), Color.FromRgb(0xff, 0xff, 0xff));

        public static readonly Style CloseButton = CreateCloseButton();

        public static readonly Style DarkCheckBox = CreateCheckBox();

        public static readonly Style ListItem = CreateListItem();

        public static readonly Style ScrollBar = CreateScrollBar();

        private static Brush Brush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private static Style CreateScrollBar()
        {
            // Built from XAML source at runtime because the compiler cannot see the
            // PART_Track / Thumb structure otherwise.
            const string markup =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'" +
                " xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>" +
                "<Setter Property='Width' Value='11'/>" +
                "<Setter Property='MinWidth' Value='11'/>" +
                "<Setter Property='Background' Value='Transparent'/>" +
                "<Setter Property='Template'><Setter.Value>" +
                "<ControlTemplate TargetType='ScrollBar'>" +
                "<Grid Background='Transparent'>" +
                "<Track x:Name='PART_Track' IsDirectionReversed='True'>" +
                "<Track.DecreaseRepeatButton>" +
                "<RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False' IsTabStop='False'/>" +
                "</Track.DecreaseRepeatButton>" +
                "<Track.Thumb>" +
                "<Thumb MinHeight='28'>" +
                "<Thumb.Template>" +
                "<ControlTemplate TargetType='Thumb'>" +
                "<Border Background='#4C4F57' CornerRadius='5' Margin='2,0,2,0'/>" +
                "</ControlTemplate>" +
                "</Thumb.Template>" +
                "</Thumb>" +
                "</Track.Thumb>" +
                "<Track.IncreaseRepeatButton>" +
                "<RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False' IsTabStop='False'/>" +
                "</Track.IncreaseRepeatButton>" +
                "</Track>" +
                "</Grid>" +
                "</ControlTemplate>" +
                "</Setter.Value></Setter>" +
                "</Style>";

            var style = (Style)System.Windows.Markup.XamlReader.Parse(markup);
            style.Seal();
            return style;
        }

        private static Style CreateButton(Color background, Color hover, Color border, Color foreground)
        {
            var style = new Style(typeof(System.Windows.Controls.Button));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(foreground)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(background)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(border)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 4, 12, 4)));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));

            var template = new ControlTemplate(typeof(System.Windows.Controls.Button));
            var borderElement = new FrameworkElementFactory(typeof(System.Windows.Controls.Border), "bd");
            borderElement.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(3));
            borderElement.SetBinding(System.Windows.Controls.Border.BackgroundProperty, TemplateBinding("Background"));
            borderElement.SetBinding(System.Windows.Controls.Border.BorderBrushProperty, TemplateBinding("BorderBrush"));
            borderElement.SetBinding(System.Windows.Controls.Border.BorderThicknessProperty, TemplateBinding("BorderThickness"));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderElement.AppendChild(presenter);
            template.VisualTree = borderElement;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, new SolidColorBrush(hover), "bd"));
            template.Triggers.Add(hoverTrigger);

            var pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85, "bd"));
            template.Triggers.Add(pressedTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4, "bd"));
            template.Triggers.Add(disabledTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Style CreateCloseButton()
        {
            var style = new Style(typeof(System.Windows.Controls.Button));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            var template = CreateFlatTemplate();
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xc4, 0x2b, 0x1c)), "bd"));
            template.Triggers.Add(hoverTrigger);
            return style;
        }

        private static ControlTemplate CreateFlatTemplate()
        {
            var template = new ControlTemplate(typeof(System.Windows.Controls.Button));
            var borderElement = new FrameworkElementFactory(typeof(System.Windows.Controls.Border), "bd");
            borderElement.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(3));
            borderElement.SetBinding(System.Windows.Controls.Border.BackgroundProperty, TemplateBinding("Background"));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderElement.AppendChild(presenter);
            template.VisualTree = borderElement;
            return template;
        }

        private static Style CreateCheckBox()
        {
            var style = new Style(typeof(System.Windows.Controls.CheckBox));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));

            var template = new ControlTemplate(typeof(System.Windows.Controls.CheckBox));
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var box = new FrameworkElementFactory(typeof(System.Windows.Controls.Border), "box");
            box.SetValue(FrameworkElement.WidthProperty, 14.0);
            box.SetValue(FrameworkElement.HeightProperty, 14.0);
            box.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(2));
            box.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
            box.SetValue(System.Windows.Controls.Border.BorderBrushProperty, Border);
            box.SetValue(System.Windows.Controls.Border.BackgroundProperty, Thumb);
            box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 7, 0));
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var mark = new FrameworkElementFactory(typeof(TextBlock), "mark");
            mark.SetValue(TextBlock.TextProperty, "✓");
            mark.SetValue(TextBlock.FontSizeProperty, 10.0);
            mark.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(mark);
            panel.AppendChild(box);

            var label = new FrameworkElementFactory(typeof(ContentPresenter));
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(label);
            template.VisualTree = panel;

            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "mark"));
            checkedTrigger.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, Accent, "box"));
            checkedTrigger.Setters.Add(new Setter(System.Windows.Controls.Border.BorderBrushProperty, Accent, "box"));
            template.Triggers.Add(checkedTrigger);

            var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5, "box"));
            template.Triggers.Add(disabledTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Style CreateListItem()
        {
            var style = new Style(typeof(System.Windows.Controls.ListBoxItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Text));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));

            var template = new ControlTemplate(typeof(System.Windows.Controls.ListBoxItem));
            var borderElement = new FrameworkElementFactory(typeof(System.Windows.Controls.Border), "bd");
            borderElement.SetBinding(System.Windows.Controls.Border.BackgroundProperty, TemplateBinding("Background"));
            borderElement.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(4, 2, 4, 2));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            borderElement.AppendChild(presenter);
            template.VisualTree = borderElement;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, ItemHover));
            style.Triggers.Add(hoverTrigger);

            var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, ItemSelected));
            style.Triggers.Add(selectedTrigger);

            return style;
        }

        private static Binding TemplateBinding(string property)
        {
            return new Binding(property) { RelativeSource = RelativeSource.TemplatedParent };
        }
    }
}
