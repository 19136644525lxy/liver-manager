using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrderManager.Models;
using OrderManager.Services;
using OrderManager.Ui;
using OrderManager.Views;

namespace OrderManager;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new();
    private readonly HashSet<UserControl> _shownPages = new();
    private UserControl? _currentPage;
    private DateTime _lastSwitchAt = DateTime.MinValue;

    // 底部提示条
    private DispatcherTimer? _toastTimer;

    // 自绘标题栏的拖动状态（WindowStyle=None 时系统不再负责拖动）
    private bool _dragging;
    private Point _grabOffset;

    // 最大化状态下先按下、移动超过阈值才还原（避免"点一下标题栏就掉出最大化"）
    private bool _pendingRestore;
    private Point _pressPoint;
    private double _restoreRatio = 0.5;
    private double _restoreGrabY = 28;

    // 普通状态下的窗口位置，最大化后还原时用
    private Rect _normalBounds = Rect.Empty;

    // 场景壁纸后台渲染（同尺寸只渲染一次；换壁纸/换尺寸会取消旧的）
    private CancellationTokenSource? _sceneRenderToken;

    // 动态壁纸（实验）：后台逐帧渲染，位图直接写进 BackdropImage
    private CancellationTokenSource? _liveToken;
    private WriteableBitmap? _liveBitmap;
    private string? _sceneRenderingId;
    private int _sceneRenderingWidth;
    private int _sceneRenderingHeight;
    private string? _sceneRenderId;
    private string? _lastScenePath;
    private DispatcherTimer? _resizeTimer;

    public MainWindow()
    {
        InitializeComponent();

        // 换背景图 / 改遮罩浓度等外观设置后即时生效
        AppState.I.AppearanceChanged += (_, _) => ApplyAppearance();

        // 各页面操作成功后的底部提示
        Notifier.Show += ShowToast;

        // 滚轮顺滑滚动
        SmoothScroller.Attach(this);

        Loaded += OnLoaded;
        StateChanged += (_, _) => UpdateFrame();

        // 视频壁纸循环播放
        BackdropVideo.MediaEnded += BackdropVideo_MediaEnded;

        // 勾了"跟随当前壁纸"时，每次切回窗口重新取一次 Wallpaper Engine 当前壁纸
        Activated += (_, _) => RefreshFollowedWallpaper();

        // 窗口尺寸变化后按新尺寸重渲染场景壁纸（防抖，避免拖动窗口时反复渲染）
        SizeChanged += OnBackdropSizeChanged;
    }

    private void OnBackdropSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var appearance = AppState.I.Appearance;
        if (!appearance.FromWallpaperEngine) return;

        var wallpaper = WallpaperEngine.Find(appearance.WeProjectPath);
        if (wallpaper is null || !string.Equals(wallpaper.Kind, "scene", StringComparison.OrdinalIgnoreCase)) return;

        if (_resizeTimer is null)
        {
            _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            _resizeTimer.Tick += (_, _) =>
            {
                _resizeTimer!.Stop();
                ApplyAppearance();
            };
        }

        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void UpdateFrame()
    {
        // 最大化时 WindowChrome 会给窗口加一圈不可见的 resize 边框，补回内边距避免内容被裁
        RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);

        // 最大化/还原按钮图标与提示跟着状态变化
        if (MaxBtn is not null)
        {
            MaxBtn.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            MaxBtn.ToolTip = WindowState == WindowState.Maximized ? "向下还原" : "最大化";
        }
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState != MouseButtonState.Pressed) return;

        var cursor = GetCursor();
        _pressPoint = cursor;
        if (WindowState == WindowState.Maximized)
        {
            // 先记下还原所需的相对位置，真正移动鼠标超过阈值后再还原
            _pendingRestore = true;
            var posInWindow = e.GetPosition(this);
            _restoreRatio = ActualWidth > 0 ? posInWindow.X / ActualWidth : 0.5;
            _restoreGrabY = Math.Min(posInWindow.Y, 30);
        }
        else
        {
            _pendingRestore = false;
            _grabOffset = e.GetPosition(this);
        }

        _dragging = true;
        if (sender is IInputElement element) element.CaptureMouse();
    }

    private void TopBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var cursor = GetCursor();

        if (_pendingRestore)
        {
            if (Math.Abs(cursor.X - _pressPoint.X) < 5 && Math.Abs(cursor.Y - _pressPoint.Y) < 5) return;

            // 最大化状态下按住标题栏拖动：先还原，并让光标停在原来的相对位置
            var restoreWidth = RestoreBounds.Width > 0 ? RestoreBounds.Width : Width;
            WindowState = WindowState.Normal;
            _grabOffset = new Point(restoreWidth * _restoreRatio, _restoreGrabY);
            _pendingRestore = false;
        }

        Left = cursor.X - _grabOffset.X;
        Top = Math.Max(0, cursor.Y - _grabOffset.Y);
    }

    private void TopBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        _pendingRestore = false;
        if (sender is IInputElement element) element.ReleaseMouseCapture();
        if (WindowState == WindowState.Normal) rememberNormalBounds();
    }

    /// <summary>记住普通尺寸下的位置，供"还原"按钮/双击标题栏回到原来的位置。</summary>
    private void rememberNormalBounds()
    {
        if (WindowState != WindowState.Normal) return;
        _normalBounds = new Rect(Left, Top, Math.Max(Width, 320), Math.Max(Height, 240));
    }

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            if (!_normalBounds.IsEmpty)
            {
                Left = _normalBounds.Left;
                Top = _normalBounds.Top;
                Width = _normalBounds.Width;
                Height = _normalBounds.Height;
            }
        }
        else
        {
            rememberNormalBounds();
            WindowState = WindowState.Maximized;
        }
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Max_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Services.AppState.I.DataDir;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开文件夹失败：" + ex.Message, "代肝管家", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        Navigate(rb.Content as string ?? "概览");
    }

    private void Navigate(string key)
    {
        PageTitle.Text = key;

        var createWatch = Perf.Start();
        var page = GetOrCreatePage(key);
        var createMs = createWatch.ElapsedMilliseconds;

        if (ReferenceEquals(_currentPage, page)) return;

        if (_currentPage is not null) _currentPage.Visibility = Visibility.Collapsed;

        var firstShow = _shownPages.Add(page);
        var showWatch = Perf.Start();
        page.Visibility = Visibility.Visible;
        page.UpdateLayout(); // 强制把这一页的排版算完，便于量出真实排版耗时
        _currentPage = page;
        var showMs = showWatch.ElapsedMilliseconds;

        var refreshWatch = Perf.Start();
        (page as IRefreshable)?.OnShown();
        var refreshMs = refreshWatch.ElapsedMilliseconds;

        Perf.Mark($"nav {key} create={createMs}ms show={showMs}ms refresh={refreshMs}ms");

        AnimatePageIn(firstShow);

        if (Perf.IsEnabled)
        {
            // 量一下"从点击到这一页真正画出来"的等待时间（含布局与渲染）
            var frameWatch = Perf.Start();
            EventHandler? onFrame = null;
            onFrame = (_, _) =>
            {
                CompositionTarget.Rendering -= onFrame;
                Perf.Mark($"nav {key} first-frame +{frameWatch.ElapsedMilliseconds}ms");
            };
            CompositionTarget.Rendering += onFrame;
        }
    }

    /// <summary>取页面，没有就创建（创建后一直挂在 PageHost 里，切换只改 Visibility）。</summary>
    private UserControl GetOrCreatePage(string key)
    {
        if (_pages.TryGetValue(key, out var page)) return page;

        page = key switch
        {
            "订单管理" => new OrdersView(),
            "托管日历" => new CalendarView(),
            "统计报表" => new ReportsView(),
            "跑手管理" => new RunnersView(),
            "价目表" => new ItemsView(),
            "设置" => new SettingsView(),
            _ => new DashboardView(),
        };
        page.Visibility = Visibility.Collapsed;
        _pages[key] = page;
        PageHost.Children.Add(page);
        return page;
    }

    /// <summary>
    /// 启动后利用空闲时间把其余页面提前创建好（每帧只建一个），
    /// 这样第一次点某个导航也不会卡一下。
    /// </summary>
    private void PrewarmPages()
    {
        var keys = new[] { "订单管理", "托管日历", "统计报表", "跑手管理", "价目表", "设置" };
        var index = 0;

        void Step()
        {
            if (index >= keys.Length) return;
            try
            {
                WarmUp(GetOrCreatePage(keys[index]));
            }
            catch (Exception ex)
            {
                App.LogError("预热页面失败：" + keys[index], ex, null);
            }
            index++;
            if (index < keys.Length)
                Dispatcher.BeginInvoke(new Action(Step), DispatcherPriority.Background);
        }

        Dispatcher.BeginInvoke(new Action(Step), DispatcherPriority.Background);
    }

    /// <summary>
    /// 预热：让页面以"透明但可见"的状态完整走一遍布局和绘制，然后立刻收起来。
    /// 这样第一次点开它时，不用再花 100~200ms 做首次排版。
    /// </summary>
    private void WarmUp(UserControl page)
    {
        if (ReferenceEquals(_currentPage, page)) return;

        page.Opacity = 0;
        page.IsHitTestVisible = false;
        page.Visibility = Visibility.Visible;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!ReferenceEquals(_currentPage, page))
            {
                page.Visibility = Visibility.Collapsed;
                page.Opacity = 1;
                page.IsHitTestVisible = true;
            }
        }), DispatcherPriority.Render);
    }

    /// <summary>
    /// 切页时的过渡：只做位移（Transform 在合成阶段完成，代价极低），
    /// 不再对整页做 Opacity 动画（整页透明动画会让 11 张卡片的阴影每帧重绘，反而更卡）。
    /// 连续快速切页时直接跳过动画，让手感更"跟手"。
    /// </summary>
    private void AnimatePageIn(bool firstShow)
    {
        var now = DateTime.Now;
        var sinceLast = (now - _lastSwitchAt).TotalMilliseconds;
        _lastSwitchAt = now;

        if (!firstShow && sinceLast < 320) return; // 连着点导航：不播动画

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(firstShow ? 12 : 6, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = ease,
            });
    }

    // ===================== 外观（自定义背景图） =====================

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var startupWatch = Perf.Start();
        ApplyAppearance();
        Navigate("概览");
        Perf.Mark($"startup first-page {startupWatch.ElapsedMilliseconds}ms");

        // 启动时窗口淡入，比直接弹出来柔和
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        rememberNormalBounds();

        // 启动后空闲时清一次壁纸缓存（删 30 天没用的、同一张壁纸只留最大的 3 个尺寸）
        Task.Run(ScenePreview.CleanupOnce);

        // 空闲时把其它页面提前建好，第一次点导航也不卡
        PrewarmPages();
        Perf.Mark($"startup prewarm queued at {startupWatch.ElapsedMilliseconds}ms");
    }

    /// <summary>按设置里的外观参数铺背景（静态图片 / Wallpaper Engine 壁纸 / 视频壁纸），并把卡片调成半透明让背景透出来。</summary>
    private void ApplyAppearance()
    {
        var appearance = WallpaperEngine.Resolve(AppState.I.Appearance, AppState.I.DataDir);

        if (!appearance.HasBackground)
        {
            ClearBackdrop();
            return;
        }

        BackdropLayer.Visibility = Visibility.Visible;
        BackdropOverlay.Opacity = Math.Clamp(appearance.OverlayOpacity, 0, 100) / 100.0;

        bool tile = !appearance.FromWallpaperEngine
                    && string.Equals(appearance.Stretch, "Tile", StringComparison.OrdinalIgnoreCase);

        // 动态壁纸（实验）：场景类壁纸按帧播放。
        // 全屏（最大化）时改成静态高清图：整屏逐帧渲染太吃 CPU 会掉帧，静止的高清图反而更清晰
        if (!tile && appearance.FromWallpaperEngine && appearance.LiveWallpaper
            && WindowState != WindowState.Maximized
            && TryStartLiveBackdrop(appearance))
        {
            ApplyGlass(appearance);
            return;
        }

        StopLive();

        if (!tile && appearance.FromWallpaperEngine && appearance.HasVideo)
            ShowVideoBackdrop(appearance);
        else if (!tile && TryShowSceneBackdrop(appearance))
        {
            // 场景壁纸走"离线渲染成高清图"这条路（见 TryShowSceneBackdrop）
        }
        else
            ShowImageBackdrop(appearance, tile);

        ApplyGlass(appearance);
    }

    /// <summary>
    /// 场景壁纸（scene.pkg）：先用已缓存的高清图；没有就先把封面铺上，同时在后台按当前窗口像素尺寸渲染一张，
    /// 渲染完整张换上去。窗口尺寸变了会按新尺寸重渲染，所以任意窗口大小都是清晰的（自适应）。
    /// 返回 false 表示这张壁纸不是场景类（或缺少 scene.pkg），交给普通图片分支处理。
    /// </summary>
    private bool TryShowSceneBackdrop(Appearance appearance)
    {
        var wallpaper = WallpaperEngine.Find(appearance.WeProjectPath);
        if (wallpaper is null) return false;
        if (!string.Equals(wallpaper.Kind, "scene", StringComparison.OrdinalIgnoreCase)) return false;

        var pkg = Path.Combine(wallpaper.DirPath, "scene.pkg");
        if (!File.Exists(pkg)) return false;

        string id = wallpaper.Id;
        var (needW, needH) = CurrentBackdropSize();

        var cached = ScenePreview.FindCached(id, needW, needH);
        if (cached is not null)
        {
            _sceneRenderId = id;
            ShowImageBackdrop(appearance, false, cached);
            return true;
        }

        // 先把封面铺上（总比空白好），再后台真渲染
        ShowImageBackdrop(appearance, false);
        StartSceneRender(appearance, wallpaper, pkg, needW, needH);
        return true;
    }

    private (int Width, int Height) CurrentBackdropSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int pixelW = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int pixelH = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        if (pixelW <= 0 || pixelH <= 0) return (1600, 900);
        return ScenePreview.TargetSize(pixelW, pixelH);
    }

    private void StartSceneRender(Appearance appearance, WeWallpaper wallpaper, string pkg, int width, int height)
    {
        if (string.Equals(_sceneRenderingId, wallpaper.Id, StringComparison.Ordinal)
            && _sceneRenderingWidth == width && _sceneRenderingHeight == height)
        {
            return; // 同一张、同一尺寸正在渲染，别重复开
        }

        _sceneRenderToken?.Cancel();
        var token = new CancellationTokenSource();
        _sceneRenderToken = token;
        _sceneRenderingId = wallpaper.Id;
        _sceneRenderingWidth = width;
        _sceneRenderingHeight = height;

        string id = wallpaper.Id;
        string pkgPath = pkg;
        Notifier.Info("正在按窗口尺寸生成高清壁纸…");
        Task.Run(() =>
        {
            if (token.IsCancellationRequested) return null;
            var result = ScenePreview.Render(id, pkgPath, width, height);
            return result;
        }, token.Token).ContinueWith(task =>
        {
            if (token.IsCancellationRequested) return;
            var path = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (token.IsCancellationRequested) return;
                _sceneRenderingId = null;
                if (path is null) return;
                // 期间用户又换了壁纸，就不要再覆盖
                var current = WallpaperEngine.Find(AppState.I.Appearance.WeProjectPath);
                if (current is null || !string.Equals(current.Id, id, StringComparison.Ordinal)) return;
                _sceneRenderId = id;
                var latest = ScenePreview.Load(path);
                if (latest is null) return;
                BackdropImage.Source = latest;
                BackdropImage.Visibility = Visibility.Visible;
                BackdropTile.Visibility = Visibility.Collapsed;
                _lastScenePath = path;
                Notifier.Info("高清壁纸已就绪");
            }));
        });
    }

    /// <summary>
    /// 动态壁纸（实验）：场景类壁纸按帧在后台渲染，位图直接写进背景 Image。
    /// 返回 true 表示已经接管背景。
    /// </summary>
    private bool TryStartLiveBackdrop(Appearance appearance)
    {
        var wallpaper = WallpaperEngine.Find(appearance.WeProjectPath);
        if (wallpaper is null) return false;
        if (!string.Equals(wallpaper.Kind, "scene", StringComparison.OrdinalIgnoreCase)) return false;

        var pkg = Path.Combine(wallpaper.DirPath, "scene.pkg");
        if (!File.Exists(pkg)) return false;

        StopLive();

        BackdropLayer.Visibility = Visibility.Visible;
        BackdropOverlay.Opacity = Math.Clamp(appearance.OverlayOpacity, 0, 100) / 100.0;
        BackdropTile.Visibility = Visibility.Collapsed;
        StopVideo();
        BackdropImage.Effect = null;
        BackdropImage.Stretch = Stretch.UniformToFill;

        var (width, height) = LiveSize();
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        _liveBitmap = bitmap;
        BackdropImage.Source = bitmap;
        BackdropImage.Visibility = Visibility.Visible;

        var token = new CancellationTokenSource();
        _liveToken = token;
        Task.Run(() => LiveLoop(pkg, width, height, bitmap, token.Token), token.Token);
        return true;
    }

    private (int Width, int Height) LiveSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int pixelW = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int pixelH = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        if (pixelW <= 0 || pixelH <= 0) { pixelW = 1600; pixelH = 900; }

        // 按用户选的清晰度渲染：默认 1:1（窗口像素多少就渲染多少），越清晰越吃 CPU
        var quality = Math.Clamp(AppState.I.Appearance.LiveQuality, 60, 100) / 100.0;
        int width = Math.Clamp((int)Math.Round(pixelW * quality), 720, 2560);
        int height = (int)Math.Round((double)pixelH * width / Math.Max(1, pixelW));
        width = width / 4 * 4;
        height = Math.Max(4, height / 4 * 4);
        return (width, height);
    }

    private void LiveLoop(string pkg, int width, int height, WriteableBitmap bitmap, CancellationToken token)
    {
        try
        {
            using var player = new SceneRender.ScenePlayer(pkg, width, height, ScenePreview.FfmpegPath(), cacheDir: Path.Combine(AppState.I.DataDir, "images", "frames"));
            player.Warm(0);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var pixels = new byte[width * height * 4];
            const double targetFrame = 1.0 / 24;

            while (!token.IsCancellationRequested)
            {
                var begin = watch.Elapsed.TotalSeconds;
                using (var frame = player.Frame(begin))
                {
                    var span = frame.DangerousTryGetSinglePixelMemory(out var memory)
                        ? memory.Span
                        : default;
                    if (span.IsEmpty) break;

                    var src = System.Runtime.InteropServices.MemoryMarshal.AsBytes(span);
                    for (int i = 0; i + 3 < src.Length && i + 3 < pixels.Length; i += 4)
                    {
                        pixels[i] = src[i + 2];
                        pixels[i + 1] = src[i + 1];
                        pixels[i + 2] = src[i];
                        pixels[i + 3] = src[i + 3];
                    }
                }

                var bytes = pixels;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (token.IsCancellationRequested || !ReferenceEquals(_liveBitmap, bitmap)) return;
                    bitmap.WritePixels(new Int32Rect(0, 0, width, height), bytes, width * 4, 0);
                }));

                var spent = watch.Elapsed.TotalSeconds - begin;
                var sleep = targetFrame - spent;
                if (sleep > 0.002) Thread.Sleep(TimeSpan.FromSeconds(sleep));
                else Thread.Sleep(1);
            }
        }
        catch (Exception ex)
        {
            App.LogError("live wallpaper", ex, null);
        }
    }

    private void StopLive()
    {
        _liveToken?.Cancel();
        _liveToken?.Dispose();
        _liveToken = null;
        if (_liveBitmap is not null)
        {
            if (ReferenceEquals(BackdropImage.Source, _liveBitmap)) BackdropImage.Source = null;
            _liveBitmap = null;
        }
    }

    private void ClearBackdrop()
    {
        StopLive();
        BackdropLayer.Visibility = Visibility.Collapsed;
        StopVideo();
        BackdropImage.Source = null;
        BackdropImage.Effect = null;
        BackdropImage.Visibility = Visibility.Collapsed;
        BackdropTile.Visibility = Visibility.Collapsed;
        RestoreSolidBrushes();
    }

    private void ShowImageBackdrop(Appearance appearance, bool tile, string? overridePath = null)
    {
        var bitmap = overridePath is null
            ? LoadBitmap(appearance.ResolvedImagePath)
            : ScenePreview.Load(overridePath) ?? LoadBitmap(appearance.ResolvedImagePath);
        if (bitmap is null)
        {
            // 壁纸元数据刚写入时可能还没有可用的预览图，此时保留上一张背景，避免界面闪成空白
            if (BackdropImage.Source is not null || BackdropVideo.Source is not null) return;
            ClearBackdrop();
            return;
        }

        StopVideo();
        BackdropImage.Visibility = tile ? Visibility.Collapsed : Visibility.Visible;
        BackdropTile.Visibility = tile ? Visibility.Visible : Visibility.Collapsed;

        double blur = Math.Clamp(appearance.BlurRadius, 0, 30);
        BackdropImage.Effect = blur > 0
            ? new System.Windows.Media.Effects.BlurEffect
            {
                Radius = blur,
                KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
            }
            : null;

        // 模糊会让图片边缘透出底色，略微放大盖住
        double scale = blur > 0 ? 1 + blur / 260.0 : 1;
        BackdropScale.ScaleX = scale;
        BackdropScale.ScaleY = scale;

        if (tile)
        {
            BackdropTile.Fill = new ImageBrush(bitmap)
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.Uniform,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 260, 260),
            };
            return;
        }

        BackdropImage.Source = bitmap;
        BackdropImage.Stretch = appearance.Stretch switch
        {
            "Uniform" => Stretch.Uniform,
            "Fill" => Stretch.Fill,
            _ => Stretch.UniformToFill,
        };
    }

    /// <summary>视频壁纸：静音循环播放（不支持模糊和铺贴，否则每帧重绘太费）。</summary>
    private void ShowVideoBackdrop(Appearance appearance)
    {
        var media = appearance.WeMediaPath;
        if (string.IsNullOrWhiteSpace(media) || !File.Exists(media))
        {
            ShowImageBackdrop(appearance, false);
            return;
        }

        BackdropImage.Source = null;
        BackdropImage.Effect = null;
        BackdropImage.Visibility = Visibility.Collapsed;
        BackdropTile.Visibility = Visibility.Collapsed;
        BackdropScale.ScaleX = 1;
        BackdropScale.ScaleY = 1;

        BackdropVideo.Visibility = Visibility.Visible;
        BackdropVideo.Stretch = appearance.Stretch switch
        {
            "Uniform" => Stretch.Uniform,
            "Fill" => Stretch.Fill,
            _ => Stretch.UniformToFill,
        };

        var current = (BackdropVideo.Source as Uri)?.LocalPath;
        if (!string.Equals(current, media, StringComparison.OrdinalIgnoreCase))
        {
            BackdropVideo.Stop();
            BackdropVideo.Source = new Uri(media, UriKind.Absolute);
            BackdropVideo.Position = TimeSpan.Zero;
        }

        BackdropVideo.Play();
    }

    private void StopVideo()
    {
        try
        {
            BackdropVideo.Stop();
            BackdropVideo.Source = null;
        }
        catch
        {
            // 媒体管道偶发异常时忽略
        }

        BackdropVideo.Visibility = Visibility.Collapsed;
    }

    private void BackdropVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        BackdropVideo.Position = TimeSpan.Zero;
        BackdropVideo.Play();
    }

    private void BackdropVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        // 视频解不了（缺解码器 / 文件被 Steam 删了）就退化用缩略图
        StopVideo();
        var appearance = WallpaperEngine.Resolve(AppState.I.Appearance, AppState.I.DataDir);
        if (!string.IsNullOrWhiteSpace(appearance.WePreviewPath) && File.Exists(appearance.WePreviewPath))
            ShowImageBackdrop(appearance, false);
        else if (appearance.HasBackground)
            ShowImageBackdrop(appearance, false);
        else
            ClearBackdrop();
    }

    /// <summary>开启"跟随当前壁纸"时，每次切回窗口都重新读一次 Wallpaper Engine 正在播放的壁纸。</summary>
    private void RefreshFollowedWallpaper()
    {
        var appearance = AppState.I.Appearance;
        if (!appearance.FromWallpaperEngine || !appearance.WeFollowCurrent) return;

        ApplyAppearance();
    }


    /// <summary>
    /// 通透模式：卡片、侧栏、输入框、表格线全部换成半透明白，只留文字和 1px 轮廓，让壁纸整块透出来。
    /// 关掉时把运行时覆盖的刷子删掉，DynamicResource 会自动回落到 Themes\Colors.xaml 的原色。
    /// </summary>
    private static void ApplyGlass(Appearance appearance)
    {
        if (!appearance.GlassMode)
        {
            // 普通模式：卡片接近不透明的白，保留一点背景透出
            SetResourceBrush("BgCard", Color.FromArgb(0xEA, 0xFF, 0xFF, 0xFF));
            SetResourceBrush("BgSidebar", Color.FromArgb(0xEB, 0xFB, 0xFC, 0xFD));
            ClearGlassBrushes();
            return;
        }

        // 通透模式：几乎全透明，只保留极淡的白让文字不飘在黑底上
        SetResourceBrush("BgCard", Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        SetResourceBrush("BgSidebar", Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        SetResourceBrush("BgHover", Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        SetResourceBrush("BgSelected", Color.FromArgb(0x40, 0x2F, 0xA9, 0x8C));
        SetResourceBrush("BgInput", Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        SetResourceBrush("BorderSoft", Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF));
        SetResourceBrush("BorderInput", Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        SetResourceBrush("RowLine", Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        SetResourceBrush("NeutralSoft", Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

        // 状态色标签也跟着变淡（原色太实，通不透的情况下会像贴在壁纸上的实色方块）
        SetResourceBrush("BrandSoft", Color.FromArgb(0x33, 0x2F, 0xA9, 0x8C));
        SetResourceBrush("SuccessSoft", Color.FromArgb(0x33, 0x1F, 0x9E, 0x74));
        SetResourceBrush("WarningSoft", Color.FromArgb(0x33, 0xE8, 0xA3, 0x47));
        SetResourceBrush("DangerSoft", Color.FromArgb(0x33, 0xD9, 0x5F, 0x57));
        SetResourceBrush("InfoSoft", Color.FromArgb(0x33, 0x4C, 0x86, 0xD9));

        // 次要文字在花壁纸上容易发糊，通透模式下加深一档
        SetResourceBrush("TextSecondary", Color.FromArgb(0xFF, 0x46, 0x53, 0x61));
        SetResourceBrush("TextMuted", Color.FromArgb(0xFF, 0x64, 0x71, 0x7F));
    }

    private static void ClearGlassBrushes()
    {
        Application.Current.Resources.Remove("BgHover");
        Application.Current.Resources.Remove("BgSelected");
        Application.Current.Resources.Remove("BgInput");
        Application.Current.Resources.Remove("BorderSoft");
        Application.Current.Resources.Remove("BorderInput");
        Application.Current.Resources.Remove("RowLine");
        Application.Current.Resources.Remove("NeutralSoft");
        Application.Current.Resources.Remove("BrandSoft");
        Application.Current.Resources.Remove("SuccessSoft");
        Application.Current.Resources.Remove("WarningSoft");
        Application.Current.Resources.Remove("DangerSoft");
        Application.Current.Resources.Remove("InfoSoft");
        Application.Current.Resources.Remove("TextSecondary");
        Application.Current.Resources.Remove("TextMuted");
    }

    private static void SetResourceBrush(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }

    /// <summary>去掉运行时覆盖的刷子，恢复 Themes 里原来的不透明配色。</summary>
    private static void RestoreSolidBrushes()
    {
        Application.Current.Resources.Remove("BgCard");
        Application.Current.Resources.Remove("BgSidebar");
        ClearGlassBrushes();
    }

    private static BitmapImage? LoadBitmap(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            // 大图先降采样，滚动和切换页面更跟手
            image.DecodePixelWidth = 2200;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    // ===================== 底部提示条 =====================

    private void ShowToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowToast(message));
            return;
        }

        ToastText.Text = message;
        ToastBorder.Visibility = Visibility.Visible;
        ToastShift.Y = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ToastBorder.BeginAnimation(OpacityProperty,
            new DoubleAnimation(ToastBorder.Opacity, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        ToastShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });

        _toastTimer ??= CreateToastTimer();
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private DispatcherTimer CreateToastTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.9) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(ToastBorder.Opacity, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            fade.Completed += (_, _) => ToastBorder.Visibility = Visibility.Collapsed;
            ToastBorder.BeginAnimation(OpacityProperty, fade);
        };
        return timer;
    }

    // ===================== 快捷键 =====================

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (ctrl)
        {
            // Ctrl+1~7 快速切换页面
            int index = e.Key switch
            {
                >= Key.D1 and <= Key.D7 => e.Key - Key.D1,
                >= Key.NumPad1 and <= Key.NumPad7 => e.Key - Key.NumPad1,
                _ => -1,
            };
            if (index >= 0)
            {
                e.Handled = true;
                SwitchPage(index);
                return;
            }

            if (e.Key == Key.N)
            {
                e.Handled = true;
                NewOrderOnOrdersPage();
                return;
            }

            if (e.Key == Key.F)
            {
                e.Handled = true;
                FocusOrderSearch();
                return;
            }
        }

        if (e.Key == Key.F5)
        {
            e.Handled = true;
            AppState.I.NotifyChanged();
            Notifier.Info("已刷新");
        }
    }

    private readonly string[] _pageKeys = ["概览", "订单管理", "托管日历", "统计报表", "跑手管理", "价目表", "设置"];

    private void SwitchPage(int index)
    {
        if (index < 0 || index >= _pageKeys.Length) return;
        var key = _pageKeys[index];
        var nav = index switch
        {
            0 => NavDashboard,
            1 => NavOrders,
            2 => NavCalendar,
            3 => NavReports,
            4 => NavRunners,
            5 => NavItems,
            _ => NavSettings,
        };
        nav.IsChecked = true;
        Navigate(key);
    }

    private void NewOrderOnOrdersPage()
    {
        EnsureOrdersPage()?.BeginNewOrder();
    }

    private void FocusOrderSearch()
    {
        EnsureOrdersPage()?.FocusSearch();
    }

    /// <summary>确保当前页是订单管理，返回它（用于快捷键跳转后立即执行动作）。</summary>
    private OrdersView? EnsureOrdersPage()
    {
        if (_currentPage is OrdersView view) return view;
        SwitchPage(1);
        return _currentPage as OrdersView;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            // Windows 11：让无边框窗口的四个角跟随系统圆角
            var handle = new WindowInteropHelper(this).Handle;
            int preference = 2; // DWMWCP_ROUND
            _ = DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        }
        catch
        {
            // 老系统没有这个 API，忽略即可
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ---------- 取屏幕光标的 Win32 封装：拖动时以屏幕坐标为基准，避免窗口移动后坐标反复抖动 ----------
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    private static Point GetCursor()
    {
        return GetCursorPos(out var p) ? new Point(p.X, p.Y) : new Point(0, 0);
    }
}

