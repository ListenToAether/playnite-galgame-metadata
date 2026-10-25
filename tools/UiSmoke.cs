using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GalgameMetadata.Shared;
using GalgameMetadata.Playnite;
using GalgameMetadata.Hikarinagi;
using GalgameMetadata.Kungal;

namespace GalgameMetadata.UiSmoke
{
    internal class FakeSource : ISiteSource
    {
        public string Name { get { return "Fake"; } }
        public string SiteKey { get { return "Fake"; } }
        public string HomeUrl { get { return "https://example.com/"; } }
        public bool TryParseId(string url, out string id) { id = null; return false; }
        public string PageUrl(string id) { return "https://example.com/game/" + id; }

        public Task<List<SearchHit>> SearchAsync(string query, int limit, FetchOptions options, CancellationToken cancel)
        {
            return Task.FromResult(new List<SearchHit>());
        }

        public Task<ScrapedGame> GetAsync(SearchHit hit, FetchOptions options, CancellationToken cancel)
        {
            var game = new ScrapedGame
            {
                Name = "测试游戏",
                OriginalName = "テストゲーム",
                Year = 2016,
                Month = 7,
                Day = 29
            };
            game.Developers.Add("测试社");
            game.Publishers.Add("测试发行商");
            game.Tags.Add("测试标签一");
            game.Tags.Add("测试标签二");
            if (hit != null && hit.Id == "with-bg")
            {
                game.BannerUrl = "https://example.invalid/banner.jpg";
                game.Screenshots.Add("https://example.invalid/shot1.jpg");
                game.Screenshots.Add("https://example.invalid/shot2.jpg");
            }
            return Task.FromResult(game);
        }
    }

    internal static class UiSmoke
    {
        [STAThread]
        private static int Main()
        {
            var failed = 0;
            try
            {
                var settings = new PluginSettings();
                var plain = new SettingsView(settings, "Ymgal", "https://www.ymgal.games/", false, delegate { });
                plain.Measure(new Size(700, 900));
                plain.Arrange(new Rect(0, 0, 700, 900));
                Console.WriteLine("SettingsView ok");

                var hits = new List<SearchHit>
                {
                    new SearchHit { Id = "plain", Title = "测试游戏", AltTitle = "テストゲーム", Developer = "测试社", Year = 2016 },
                    new SearchHit { Id = "with-bg", Title = "测试游戏二", Developer = "测试社二", Year = 2017 }
                };
                var window = new SelectionWindow(new FakeSource(), "测试游戏", hits, new FetchOptions(),
                    CancellationToken.None, "with-bg", null);
                window.Measure(new Size(1000, 700));
                window.Arrange(new Rect(0, 0, 1000, 700));
                Console.WriteLine("SelectionWindow title: " + window.Title);

                var texts = new List<string>();
                Collect(window.Content, texts);
                Console.WriteLine("visible texts: " + texts.Count);
                for (var i = 0; i < texts.Count && i < 24; i++)
                {
                    Console.WriteLine("  - " + texts[i]);
                }
                Console.WriteLine("UI smoke OK");

                // Descriptions are plain text with newlines; Playnite renders HTML, so ToHtml must escape
                // and turn every line break into <br>.
                var html = Text.ToHtml("第一段\r\n第二段\n\n第三段 & <test>");
                Console.WriteLine("ToHtml: " + html.Replace("\n", "\\n"));
                if (html != "第一段<br>\n第二段<br>\n<br>\n第三段 &amp; &lt;test&gt;")
                {
                    throw new InvalidOperationException("ToHtml output is wrong: " + html);
                }

                TestPumping();
            }
            catch (Exception ex)
            {
                failed = 1;
                Console.WriteLine("FAILED: " + ex);
            }
            return failed;
        }

        private static void TestPumping()
        {
            var host = new Window
            {
                Title = "smoke-host",
                Width = 200,
                Height = 120,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Opacity = 0
            };
            host.Content = new TextBlock { Text = "host" };
            var dispatcher = Dispatcher.CurrentDispatcher;

            host.Loaded += delegate
            {
                // Called on the UI thread, so PumpedRun has to pump the dispatcher while the
                // worker thread asks it to show a window (the same way Playnite does it).
                var value = PumpedRun.Run(dispatcher, delegate
                {
                    var picked = "none";
                    dispatcher.Invoke(new Action(delegate
                    {
                        var popup = new Window { Title = "popup", Width = 200, Height = 100 };
                        popup.Show();
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                        timer.Tick += delegate
                        {
                            timer.Stop();
                            popup.Close();
                        };
                        timer.Start();
                        picked = "closed";
                    }));
                    return "pumped:" + picked;
                });
                Console.WriteLine("pumping test: " + value);

                // Real network round trip: search Hikarinagi, let the window load the candidate and
                // confirm automatically, to prove the whole "pick a result + pick a background" flow.
                Task.Run(delegate
                {
                    foreach (var source in new ISiteSource[] { new HikarinagiSource(), new KungalSource() })
                    {
                        var report = "no result";
                        try
                        {
                            report = RealPickerTest(dispatcher, source, source.Name);
                        }
                        catch (Exception ex)
                        {
                            report = "FAILED: " + ex.Message;
                        }
                        Console.WriteLine("real picker [" + source.Name + "]: " + report);
                    }
                    foreach (var source in new ISiteSource[] { new HikarinagiSource(), new KungalSource() })
                    {
                        var report = "no result";
                        try
                        {
                            report = LinkPreselectTest(dispatcher, source);
                        }
                        catch (Exception ex)
                        {
                            report = "FAILED: " + ex.Message;
                        }
                        Console.WriteLine("link preselect [" + source.Name + "]: " + report);
                    }
                    dispatcher.BeginInvoke(new Action(delegate
                    {
                        host.Close();
                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                    }));
                });
            };

            host.Show();
            Dispatcher.Run();
            Console.WriteLine("dispatcher stopped");
        }

        private static string RealPickerTest(Dispatcher dispatcher, ISiteSource sourceArg, string label)
        {
            var options = new FetchOptions();
            var cancel = CancellationToken.None;
            var hits = sourceArg.SearchAsync("千恋＊万花", 5, options, cancel).GetAwaiter().GetResult();
            if (hits == null || hits.Count == 0) return "no search hits (network problem?)";

            ManualSelection selection = null;
            Exception failure = null;
            var thumbCount = 0;
            dispatcher.Invoke(new Action(delegate
            {
                try
                {
                    var window = new SelectionWindow(sourceArg, "千恋＊万花", hits, options, cancel, hits[0].Id, null);
                    var ok = (Button)typeof(SelectionWindow)
                        .GetField("_ok", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                    var confirm = typeof(SelectionWindow)
                        .GetMethod("Confirm", BindingFlags.NonPublic | BindingFlags.Instance);
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                    var ticks = 0;
                    var readyTicks = -1;
                    timer.Tick += delegate
                    {
                        ticks++;
                        if (ok.IsEnabled && readyTicks < 0) readyTicks = ticks;
                        if (readyTicks > 0 && ticks >= readyTicks + 14)
                        {
                            timer.Stop();
                            Capture(window, sourceArg.SiteKey);
                            confirm.Invoke(window, null);
                        }
                        else if (ticks > 120)
                        {
                            timer.Stop();
                            window.Close();
                        }
                    };
                    timer.Start();
                    window.ShowDialog();
                    selection = window.Selection;
                    var panel = (WrapPanel)typeof(SelectionWindow)
                        .GetField("_backgrounds", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                    thumbCount = panel.Children.Count;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            }));

            if (failure != null) throw failure;
            if (selection == null || selection.Game == null) return "window cancelled or timed out";
            return string.Format("game={0} | background={1} | tags={2} | links={3} | screenshots={4} | thumbs={5}",
                selection.Game.Name,
                selection.BackgroundUrl == null ? "(none)" : selection.BackgroundUrl,
                selection.Game.Tags.Count,
                selection.Game.Links.Count,
                selection.Game.Screenshots.Count,
                thumbCount);
        }

        /// <summary>
        /// A game that already has a link to the site must still show the window (the link only preselects
        /// the candidate), so the user can correct a wrong match on every scrape.
        /// </summary>
        private static string LinkPreselectTest(Dispatcher dispatcher, ISiteSource source)
        {
            var options = new FetchOptions();
            var cancel = CancellationToken.None;
            var hits = source.SearchAsync("千恋＊万花", 5, options, cancel).GetAwaiter().GetResult();
            if (hits == null || hits.Count == 0) return "no search hits (network problem?)";

            var id = hits[0].Id;
            var url = source.PageUrl(id);
            var windowShown = false;
            ManualPicker.OnWindowCreated = delegate(SelectionWindow w)
            {
                windowShown = true;
                var confirm = typeof(SelectionWindow).GetMethod("Confirm", BindingFlags.NonPublic | BindingFlags.Instance);
                var okField = typeof(SelectionWindow).GetField("_ok", BindingFlags.NonPublic | BindingFlags.Instance);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                var ticks = 0;
                timer.Tick += delegate
                {
                    ticks++;
                    var ok = (Button)okField.GetValue(w);
                    if (ok.IsEnabled && ticks >= 6)
                    {
                        timer.Stop();
                        Capture(w, "link-" + source.SiteKey);
                        confirm.Invoke(w, null);
                    }
                    else if (ticks > 100)
                    {
                        timer.Stop();
                        w.DialogResult = false;
                    }
                };
                timer.Start();
            };

            ScrapedGame picked;
            try
            {
                picked = InteractiveResolver.ResolveCore(dispatcher, source, "千恋＊万花", new[] { url }, options, cancel, null);
            }
            finally
            {
                ManualPicker.OnWindowCreated = null;
            }

            if (!windowShown) return "FAILED: window did not open even though a link existed";
            if (picked == null) return "window opened but nothing was confirmed";
            return string.Format("windowShown={0} | linkId={1} | matched={2} | siteUrl={3}",
                windowShown, id, picked.Name, picked.SiteUrl);
        }

        private static void Capture(Window window, string tag)
        {
            try
            {
                var width = (int)window.ActualWidth;
                var height = (int)window.ActualHeight;
                if (width <= 0 || height <= 0) return;
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "picker-dark-" + tag + ".png");
                using (var stream = File.Create(path))
                {
                    encoder.Save(stream);
                }
                Console.WriteLine("screenshot (" + tag + "): " + path);
            }
            catch (Exception ex)
            {
                Console.WriteLine("screenshot failed: " + ex.Message);
            }
        }

        private static void Collect(object node, List<string> texts)
        {
            var element = node as FrameworkElement;
            if (element == null) return;

            var text = element as TextBlock;
            if (text != null && !string.IsNullOrWhiteSpace(text.Text)) texts.Add(text.Text);

            var check = element as CheckBox;
            if (check != null && check.Content is string) texts.Add("[勾选] " + check.Content);

            var button = element as Button;
            if (button != null && button.Content is string) texts.Add("[按钮] " + button.Content);

            var panel = element as Panel;
            if (panel != null)
            {
                foreach (UIElement child in panel.Children) Collect(child, texts);
            }

            var border = element as Border;
            if (border != null && border.Child != null) Collect(border.Child, texts);

            var scroll = element as ScrollViewer;
            if (scroll != null && scroll.Content != null) Collect(scroll.Content, texts);

            var list = element as ListBox;
            if (list != null)
            {
                foreach (var entry in list.Items) Collect(entry, texts);
            }

            var listItem = element as ListBoxItem;
            if (listItem != null && listItem.Content != null) Collect(listItem.Content, texts);
        }
    }
}
