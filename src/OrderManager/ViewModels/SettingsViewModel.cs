using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using OrderManager.Models;
using OrderManager.Mvvm;
using OrderManager.Services;

namespace OrderManager.ViewModels;

/// <summary>设置页：店铺信息、分成规则、外观壁纸与数据备份/导出。</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private string _shopName = "";
    private string _ownerName = "";

    private string _newRulesStartDate = "";
    private string _highThreshold = "";
    private string _highRateOld = "";
    private string _highRateNew = "";
    private string _lowRateOld = "";
    private string _lowRateNew = "";
    private string _runnerAssistantRate = "";
    private string _xiaoyuRate = "";
    private string _runnerRate = "";
    private string _myRate = "";
    private bool _disableAssistantShare;
    private string _xiaoyuName = "";

    private string _statusText = "";

    // ===== 外观：待保存的副本 + 界面控件值，拖动滑块时即时写回 AppState =====
    private Appearance _appearanceDraft = new();
    private double _overlayOpacity = 78;
    private double _blurRadius;
    private StretchOption? _selectedStretchOption;
    private bool _suppressAppearanceSave;

    // ===== 壁纸（Wallpaper Engine）=====
    private WallpaperItem? _selectedWallpaper;
    private string _wallpaperSearch = "";
    private string _weStatusText = "";
    private string _weFollowHint = "";
    private bool _weFollowCurrent;
    private bool _suppressWallpaperSync;

    public SettingsViewModel()
    {
        SaveProfileCommand = new RelayCommand(SaveProfile);
        SaveRulesCommand = new RelayCommand(SaveRules);
        ResetRulesCommand = new RelayCommand(ResetRules);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        BackupDatabaseCommand = new RelayCommand(BackupDatabase);
        ExportOrdersCommand = new RelayCommand(ExportOrders);

        SelectBackgroundCommand = new RelayCommand(SelectBackground);
        ClearBackgroundCommand = new RelayCommand(ClearBackground);
        ResetAppearanceCommand = new RelayCommand(ResetAppearance);

        // 壁纸列表用默认视图承载搜索过滤，避免每次搜索都重建 69 个缩略图对象
        WallpapersView = CollectionViewSource.GetDefaultView(Wallpapers);
        WallpapersView.Filter = FilterWallpaper;
        _weStatusText = WallpaperEngine.StatusText;
        RescanWallpapersCommand = new RelayCommand(RescanWallpapers);
    }

    public ICommand SaveProfileCommand { get; }
    public ICommand SaveRulesCommand { get; }
    public ICommand ResetRulesCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand BackupDatabaseCommand { get; }
    public ICommand ExportOrdersCommand { get; }

    // ===== 外观相关命令 =====
    public ICommand SelectBackgroundCommand { get; }
    public ICommand ClearBackgroundCommand { get; }
    public ICommand ResetAppearanceCommand { get; }

    // ===== 壁纸相关命令 =====
    public ICommand RescanWallpapersCommand { get; }

    public string DataDir => AppState.I.DataDir;
    public string DbPathText => AppState.I.Db.DbPath;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string ShopName
    {
        get => _shopName;
        set => SetProperty(ref _shopName, value);
    }

    public string OwnerName
    {
        get => _ownerName;
        set => SetProperty(ref _ownerName, value);
    }

    public string NewRulesStartDate
    {
        get => _newRulesStartDate;
        set => SetProperty(ref _newRulesStartDate, value);
    }

    public string HighThreshold
    {
        get => _highThreshold;
        set => SetProperty(ref _highThreshold, value);
    }

    public string HighRateOld
    {
        get => _highRateOld;
        set => SetProperty(ref _highRateOld, value);
    }

    public string HighRateNew
    {
        get => _highRateNew;
        set => SetProperty(ref _highRateNew, value);
    }

    public string LowRateOld
    {
        get => _lowRateOld;
        set => SetProperty(ref _lowRateOld, value);
    }

    public string LowRateNew
    {
        get => _lowRateNew;
        set => SetProperty(ref _lowRateNew, value);
    }

    public string RunnerAssistantRate
    {
        get => _runnerAssistantRate;
        set => SetProperty(ref _runnerAssistantRate, value);
    }

    public string XiaoyuRate
    {
        get => _xiaoyuRate;
        set => SetProperty(ref _xiaoyuRate, value);
    }

    public string RunnerRate
    {
        get => _runnerRate;
        set => SetProperty(ref _runnerRate, value);
    }

    public string MyRate
    {
        get => _myRate;
        set => SetProperty(ref _myRate, value);
    }

    public bool DisableAssistantShare
    {
        get => _disableAssistantShare;
        set => SetProperty(ref _disableAssistantShare, value);
    }

    public string XiaoyuName
    {
        get => _xiaoyuName;
        set => SetProperty(ref _xiaoyuName, value);
    }

    // ===== 外观 =====

    /// <summary>背景图显示文本：未设置时显示「未设置」，否则显示文件名。</summary>
    public string BackgroundImageText
    {
        get
        {
            var path = _appearanceDraft.BackgroundPath;
            return string.IsNullOrWhiteSpace(path) ? "未设置" : Path.GetFileName(path);
        }
    }

    /// <summary>是否已设置背景图（控制「清除背景」按钮的可用状态）。</summary>
    public bool HasBackground => !string.IsNullOrWhiteSpace(_appearanceDraft.BackgroundPath);

    /// <summary>遮罩浓度（0-100），拖动时实时保存并立刻刷新窗口背景。</summary>
    public double OverlayOpacity
    {
        get => _overlayOpacity;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), 0d, 100d);
            if (Math.Abs(_overlayOpacity - clamped) < 0.001) return;
            _overlayOpacity = clamped;
            _appearanceDraft.OverlayOpacity = (int)clamped;
            OnPropertyChanged(nameof(OverlayOpacity));
            OnPropertyChanged(nameof(OverlayOpacityText));
            if (!_suppressAppearanceSave) SaveAppearance();
        }
    }

    /// <summary>遮罩浓度的百分比文案。</summary>
    public string OverlayOpacityText => ((int)_overlayOpacity).ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>背景图模糊程度（0-30），拖动时实时保存。</summary>
    public double BlurRadius
    {
        get => _blurRadius;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), 0d, 30d);
            if (Math.Abs(_blurRadius - clamped) < 0.001) return;
            _blurRadius = clamped;
            _appearanceDraft.BlurRadius = (int)clamped;
            OnPropertyChanged(nameof(BlurRadius));
            OnPropertyChanged(nameof(BlurRadiusText));
            if (!_suppressAppearanceSave) SaveAppearance();
        }
    }

    /// <summary>模糊程度的数值文案。</summary>
    public string BlurRadiusText => ((int)_blurRadius).ToString(CultureInfo.InvariantCulture);

    /// <summary>「图片铺法」下拉选项：中文文案 → Appearance.Stretch 取值。</summary>
    public IReadOnlyList<StretchOption> StretchOptions { get; } = new[]
    {
        new StretchOption("填满", "UniformToFill"),
        new StretchOption("适应", "Uniform"),
        new StretchOption("拉伸", "Fill"),
        new StretchOption("平铺", "Tile"),
    };

    /// <summary>当前选中的图片铺法，切换后立即保存。</summary>
    public StretchOption? SelectedStretchOption
    {
        get => _selectedStretchOption;
        set
        {
            if (ReferenceEquals(_selectedStretchOption, value)) return;
            _selectedStretchOption = value;
            OnPropertyChanged(nameof(SelectedStretchOption));
            if (value is null || _suppressAppearanceSave) return;
            if (_appearanceDraft.Stretch == value.Value) return;
            _appearanceDraft.Stretch = value.Value;
            SaveAppearance();
        }
    }

    // ===== 壁纸（Wallpaper Engine）=====

    /// <summary>通透模式：卡片近乎透明，只留文字和 1px 轮廓，让壁纸整块透出来。</summary>
    public bool GlassMode
    {
        get => _appearanceDraft.GlassMode;
        set
        {
            if (_appearanceDraft.GlassMode == value) return;
            _appearanceDraft.GlassMode = value;
            OnPropertyChanged(nameof(GlassMode));
            SaveAppearance();
        }
    }

    /// <summary>动态壁纸（实验）：场景类壁纸按帧播放，让粒子/图层动画动起来。默认关闭。</summary>
    public bool LiveWallpaper
    {
        get => _appearanceDraft.LiveWallpaper;
        set
        {
            if (_appearanceDraft.LiveWallpaper == value) return;
            _appearanceDraft.LiveWallpaper = value;
            OnPropertyChanged(nameof(LiveWallpaper));
            SaveAppearance();
        }
    }

    /// <summary>订阅壁纸列表；列表控件的搜索过滤由 WallpapersView 负责。</summary>
    /// <summary>动态壁纸清晰度选项（渲染比例，100 = 按窗口像素 1:1）。</summary>
    public IReadOnlyList<string> LiveQualityOptions { get; } = new[] { "清晰（按窗口 1:1）", "标准（85%）", "流畅（70%）" };

    public string SelectedLiveQuality
    {
        get => _appearanceDraft.LiveQuality switch
        {
            <= 70 => "流畅（70%）",
            <= 85 => "标准（85%）",
            _ => "清晰（按窗口 1:1）",
        };
        set
        {
            var quality = value switch
            {
                "流畅（70%）" => 70,
                "标准（85%）" => 85,
                _ => 100,
            };
            if (_appearanceDraft.LiveQuality == quality) return;
            _appearanceDraft.LiveQuality = quality;
            OnPropertyChanged(nameof(SelectedLiveQuality));
            SaveAppearance();
        }
    }
    public ObservableCollection<WallpaperItem> Wallpapers { get; } = new();

    /// <summary>挂到列表控件上的视图：搜索时只刷新过滤条件，不重建缩略图。</summary>
    public ICollectionView WallpapersView { get; }

    /// <summary>当前选中的订阅壁纸；一选中就写进外观并立刻生效。</summary>
    public WallpaperItem? SelectedWallpaper
    {
        get => _selectedWallpaper;
        set
        {
            if (ReferenceEquals(_selectedWallpaper, value)) return;
            _selectedWallpaper = value;
            OnPropertyChanged(nameof(SelectedWallpaper));
            if (value is null || _suppressWallpaperSync) return;

            // 手动点选壁纸就取消「跟随当前壁纸」，否则两个设置会互相打架
            if (_weFollowCurrent) WeFollowCurrent = false;

            _appearanceDraft.Source = BackgroundSource.WallpaperEngine;
            ApplyWallpaperFields(value);

            // 换成壁纸时默认打开通透模式：卡片退成一层淡白，壁纸整块透出来
            if (!_appearanceDraft.GlassMode)
            {
                _appearanceDraft.GlassMode = true;
                OnPropertyChanged(nameof(GlassMode));
            }

            SaveAppearance();
            PrefetchHiResPreview(value);
        }
    }

    /// <summary>壁纸搜索关键词，输入时即时过滤列表。</summary>
    public string WallpaperSearch
    {
        get => _wallpaperSearch;
        set
        {
            var text = value ?? "";
            if (!SetProperty(ref _wallpaperSearch, text)) return;
            FilterWallpapers();
        }
    }

    /// <summary>Wallpaper Engine 扫描状态（卡片里的一行灰字）。</summary>
    public string WeStatusText
    {
        get => _weStatusText;
        set => SetProperty(ref _weStatusText, value ?? "");
    }

    /// <summary>「跟随当前壁纸」开启后的提示；为空时界面上隐藏。</summary>
    public string WeFollowHint
    {
        get => _weFollowHint;
        set => SetProperty(ref _weFollowHint, value ?? "");
    }

    /// <summary>背景来源：静态图片。</summary>
    public bool IsImageSource
    {
        get => !_appearanceDraft.FromWallpaperEngine;
        set
        {
            if (!value || !_appearanceDraft.FromWallpaperEngine) return;

            _appearanceDraft.Source = BackgroundSource.Image;
            if (_weFollowCurrent)
            {
                _weFollowCurrent = false;
                _appearanceDraft.WeFollowCurrent = false;
                OnPropertyChanged(nameof(WeFollowCurrent));
                WeFollowHint = "";
            }

            SaveAppearance();
        }
    }

    /// <summary>背景来源：订阅壁纸（Wallpaper Engine）。</summary>
    public bool IsWallpaperEngineSource
    {
        get => _appearanceDraft.FromWallpaperEngine;
        set
        {
            if (!value) return;
            UseWallpaperEngineSource();
        }
    }

    /// <summary>当前壁纸是不是视频（控制「视频壁纸不支持模糊」提示）。</summary>
    public bool IsVideoWallpaper =>
        _appearanceDraft.FromWallpaperEngine
        && (string.Equals(_appearanceDraft.WeKind, "video", StringComparison.OrdinalIgnoreCase)
            || _appearanceDraft.HasVideo);

    /// <summary>是否随 Windows 一起启动（写当前用户的注册表 Run 项，不需要管理员权限）。</summary>
    public bool AutoStart
    {
        get => StartupManager.IsEnabled;
        set
        {
            if (value == StartupManager.IsEnabled) return;

            if (!StartupManager.SetEnabled(value))
            {
                OnPropertyChanged(nameof(AutoStart));
                Notifier.Info("设置开机自启失败，可能被安全软件拦截了");
                return;
            }

            OnPropertyChanged(nameof(AutoStart));
            Notifier.Info(value ? "已设置：开机自动启动" : "已取消开机自动启动");
        }
    }

    /// <summary>跟随 Wallpaper Engine 当前播放的壁纸，开启后主窗口每次切回都会重新读取。</summary>
    public bool WeFollowCurrent
    {
        get => _weFollowCurrent;
        set
        {
            if (_weFollowCurrent == value) return;
            _weFollowCurrent = value;
            OnPropertyChanged(nameof(WeFollowCurrent));
            _appearanceDraft.WeFollowCurrent = value;

            if (value)
            {
                _appearanceDraft.Source = BackgroundSource.WallpaperEngine;
                var current = WallpaperEngine.ResolveFromPath(WallpaperEngine.CurrentWallpaperPath());
                if (current is not null)
                {
                    var item = _selectedWallpaper;
                    if (item is null || !SamePath(item.ProjectPath, current.ProjectPath))
                    {
                        item = null;
                        foreach (var candidate in Wallpapers)
                        {
                            if (!SamePath(candidate.ProjectPath, current.ProjectPath)) continue;
                            item = candidate;
                            break;
                        }
                        SetSelectedWallpaperSilently(item);
                    }

                    ApplyWallpaperFields(item ?? new WallpaperItem(current));
                    WeFollowHint = "每次切回窗口会重新读取当前桌面壁纸（需要 Wallpaper Engine 正在运行）";
                }
                else
                {
                    WeFollowHint = "没读到当前壁纸，确认 Wallpaper Engine 正在运行";
                }
            }
            else
            {
                WeFollowHint = "";
            }

            SaveAppearance();
        }
    }

    /// <summary>列表为空时的提示文案（区分「没装 WE」与「搜不到」）。</summary>
    public string EmptyWallpaperText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_wallpaperSearch)) return "没有匹配的壁纸，换个关键词试试";
            if (!WallpaperEngine.IsAvailable) return "没检测到 Wallpaper Engine 的订阅目录";
            return "未找到订阅壁纸，可以点「重新扫描」再试一次";
        }
    }

    /// <summary>过滤后列表是否为空（空列表时界面显示提示文字）。</summary>
    public bool HasNoWallpapers => WallpapersView.IsEmpty;

    /// <summary>快捷键说明（只读列表，供设置页展示）。</summary>
    public IReadOnlyList<ShortcutItem> ShortcutItems { get; } = new[]
    {
        new ShortcutItem("Ctrl + N", "新增订单"),
        new ShortcutItem("Ctrl + 1 ~ 7", "切换页面：概览、订单、日历、报表、跑手、价目、设置"),
        new ShortcutItem("Ctrl + F", "搜索订单"),
        new ShortcutItem("F5", "刷新当前页"),
        new ShortcutItem("Esc", "关闭弹窗"),
        new ShortcutItem("Ctrl + S", "保存（编辑订单窗口）"),
    };

    /// <summary>把 AppState 中的档案与规则读回界面。</summary>
    public void Load()
    {
        ShopName = AppState.I.Profile.ShopName;
        OwnerName = AppState.I.Profile.OwnerName;

        var rules = AppState.I.Rules;
        NewRulesStartDate = rules.NewRulesStartDate;
        HighThreshold = NumberText(rules.PersonalHighThreshold);
        HighRateOld = PercentText(rules.PersonalHighRateOld);
        HighRateNew = PercentText(rules.PersonalHighRateNew);
        LowRateOld = PercentText(rules.PersonalLowRateOld);
        LowRateNew = PercentText(rules.PersonalLowRateNew);
        RunnerAssistantRate = PercentText(rules.RunnerAssistantRate);
        XiaoyuRate = PercentText(rules.RunnerXiaoyuRate);
        RunnerRate = PercentText(rules.RunnerRate);
        MyRate = PercentText(rules.MyRate);
        DisableAssistantShare = rules.DisableAssistantShare;
        XiaoyuName = rules.XiaoyuName;

        OnPropertyChanged(nameof(DataDir));
        OnPropertyChanged(nameof(DbPathText));

        LoadAppearance();
        ReloadWallpapers();
    }

    private void SaveProfile()
    {
        var shop = ShopName.Trim();
        if (shop.Length == 0)
        {
            StatusText = "店铺名称不能为空";
            return;
        }

        AppState.I.Profile.ShopName = shop;
        AppState.I.Profile.OwnerName = OwnerName.Trim();
        AppState.I.SaveProfile();
        ShopName = shop;
        StatusText = "店铺信息已保存";
    }

    private void SaveRules()
    {
        try
        {
            var startDate = ParseDateText(NewRulesStartDate, "新规则生效日期");
            var threshold = ParseAmount(HighThreshold, "高额订单门槛");
            var highOld = ParsePercent(HighRateOld, "高额订单旧规则分成");
            var highNew = ParsePercent(HighRateNew, "高额订单新规则分成");
            var lowOld = ParsePercent(LowRateOld, "低额订单旧规则分成");
            var lowNew = ParsePercent(LowRateNew, "低额订单新规则分成");
            var assistantRate = ParsePercent(RunnerAssistantRate, "助手分成");
            var xiaoyuRate = ParsePercent(XiaoyuRate, "小予单助手分成");
            var runnerRate = ParsePercent(RunnerRate, "跑手默认分成");
            var myRate = ParsePercent(MyRate, "我的分成");
            var xiaoyuName = XiaoyuName.Trim();
            if (xiaoyuName.Length == 0)
            {
                StatusText = "「小予」的称呼不能为空";
                return;
            }

            var rules = AppState.I.Rules;
            rules.NewRulesStartDate = startDate;
            rules.PersonalHighThreshold = threshold;
            rules.PersonalHighRateOld = highOld;
            rules.PersonalHighRateNew = highNew;
            rules.PersonalLowRateOld = lowOld;
            rules.PersonalLowRateNew = lowNew;
            rules.RunnerAssistantRate = assistantRate;
            rules.RunnerXiaoyuRate = xiaoyuRate;
            rules.RunnerRate = runnerRate;
            rules.MyRate = myRate;
            rules.DisableAssistantShare = DisableAssistantShare;
            rules.XiaoyuName = xiaoyuName;

            AppState.I.SaveRules();
            Load();
            StatusText = "分成规则已保存，各页面金额已按新规则重新计算";
        }
        catch (FormatException ex)
        {
            StatusText = "保存失败：" + ex.Message;
        }
    }

    private void ResetRules()
    {
        var answer = MessageBox.Show(
            "确定把分成规则恢复为默认值吗？\n恢复后所有页面的分成金额会按默认规则重新计算。",
            "恢复默认分成规则", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var defaults = new AppRules();
        var rules = AppState.I.Rules;
        rules.NewRulesStartDate = defaults.NewRulesStartDate;
        rules.PersonalHighThreshold = defaults.PersonalHighThreshold;
        rules.PersonalHighRateOld = defaults.PersonalHighRateOld;
        rules.PersonalHighRateNew = defaults.PersonalHighRateNew;
        rules.PersonalLowRateOld = defaults.PersonalLowRateOld;
        rules.PersonalLowRateNew = defaults.PersonalLowRateNew;
        rules.RunnerAssistantRate = defaults.RunnerAssistantRate;
        rules.RunnerXiaoyuRate = defaults.RunnerXiaoyuRate;
        rules.RunnerRate = defaults.RunnerRate;
        rules.MyRate = defaults.MyRate;
        rules.DisableAssistantShare = defaults.DisableAssistantShare;
        rules.XiaoyuName = defaults.XiaoyuName;

        AppState.I.SaveRules();
        Load();
        StatusText = "已恢复默认分成规则";
    }

    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppState.I.DataDir,
                UseShellExecute = true,
            });
            StatusText = "已打开数据文件夹";
        }
        catch (Exception ex)
        {
            StatusText = "打开文件夹失败：" + ex.Message;
        }
    }

    private void BackupDatabase()
    {
        try
        {
            var source = AppState.I.Db.DbPath;
            if (!File.Exists(source))
            {
                StatusText = "数据库文件尚未生成，请先录入数据";
                return;
            }

            var targetDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
                targetDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
                targetDir = AppState.I.DataDir;

            var target = Path.Combine(targetDir, $"代肝管家备份_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            File.Copy(source, target, true);
            StatusText = "数据库已备份到：" + target;
            MessageBox.Show("数据库已备份到：\n" + target, "备份完成",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = "备份失败：" + ex.Message;
        }
    }

    private void ExportOrders()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出全部订单",
                FileName = $"订单导出_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Filter = "CSV 文件 (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
            };
            if (dialog.ShowDialog() != true) return;

            var orders = AppState.I.LoadAllOrders();
            var builder = new StringBuilder();
            builder.AppendLine("订单ID,下单日期,老板,电话,项目,数量,金额,接单方式,跑手,状态,托管开始,托管结束,助手分成,跑手分成,我的分成,备注");
            foreach (var order in orders)
            {
                builder.AppendLine(string.Join(",", new[]
                {
                    Csv(order.Id.ToString(CultureInfo.InvariantCulture)),
                    Csv(order.OrderDate),
                    Csv(order.BossName),
                    Csv(order.Phone),
                    Csv(order.Item),
                    Csv(order.Quantity.ToString(CultureInfo.InvariantCulture)),
                    Csv(order.Amount.ToString("0.00", CultureInfo.InvariantCulture)),
                    Csv(order.Personal ? "个人接单" : "跑手接单"),
                    Csv(order.RunnerName),
                    Csv(order.Status),
                    Csv(order.StartDate),
                    Csv(order.EndDate),
                    Csv(order.AssistantShare.ToString("0.00", CultureInfo.InvariantCulture)),
                    Csv(order.RunnerShare.ToString("0.00", CultureInfo.InvariantCulture)),
                    Csv(order.MineShare.ToString("0.00", CultureInfo.InvariantCulture)),
                    Csv(order.Note),
                }));
            }

            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            StatusText = $"已导出 {orders.Count} 条订单到 {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = "导出失败：" + ex.Message;
        }
    }

    // ===== 外观操作 =====

    /// <summary>把 AppState 中的外观读回界面（期间不触发保存）。</summary>
    private void LoadAppearance()
    {
        _appearanceDraft = AppState.I.Appearance.Clone();
        _suppressAppearanceSave = true;
        try
        {
            _overlayOpacity = Math.Clamp(_appearanceDraft.OverlayOpacity, 0, 100);
            _blurRadius = Math.Clamp(_appearanceDraft.BlurRadius, 0, 30);

            // 未知取值统一回落到第一项（填满），避免下拉框空白
            _selectedStretchOption = StretchOptions[0];
            foreach (var option in StretchOptions)
            {
                if (option.Value == _appearanceDraft.Stretch)
                {
                    _selectedStretchOption = option;
                    break;
                }
            }
        }
        finally
        {
            _suppressAppearanceSave = false;
        }

        _weFollowCurrent = _appearanceDraft.WeFollowCurrent;
        OnPropertyChanged(nameof(OverlayOpacity));
        OnPropertyChanged(nameof(OverlayOpacityText));
        OnPropertyChanged(nameof(BlurRadius));
        OnPropertyChanged(nameof(BlurRadiusText));
        OnPropertyChanged(nameof(SelectedStretchOption));
        OnPropertyChanged(nameof(BackgroundImageText));
        OnPropertyChanged(nameof(HasBackground));
        OnPropertyChanged(nameof(IsImageSource));
        OnPropertyChanged(nameof(IsWallpaperEngineSource));
        OnPropertyChanged(nameof(IsVideoWallpaper));
        OnPropertyChanged(nameof(WeFollowCurrent));
        WeFollowHint = _weFollowCurrent
            ? "每次切回窗口会重新读取当前桌面壁纸（需要 Wallpaper Engine 正在运行）"
            : "";
    }

    /// <summary>保存待保存的外观（窗口立即换背景）并刷新所有外观相关绑定。</summary>
    private void SaveAppearance()
    {
        AppState.I.SaveAppearance(_appearanceDraft);
        OnPropertyChanged(nameof(BackgroundImageText));
        OnPropertyChanged(nameof(HasBackground));
        OnPropertyChanged(nameof(IsImageSource));
        OnPropertyChanged(nameof(IsWallpaperEngineSource));
        OnPropertyChanged(nameof(IsVideoWallpaper));
        OnPropertyChanged(nameof(GlassMode));
    }

    /// <summary>
    /// 场景类壁纸本地只有 190~260px 的方形缩略图，铺满窗口必然发糊；
    /// 这里在后台去 Steam 创意工坊拉一张高清预览图（通常 1920 宽），拿到后再刷一次外观。
    /// </summary>
    private void PrefetchHiResPreview(WallpaperItem item)
    {
        var source = item.Source;
        if (source is null || source.HasVideo) return;

        var id = source.Id;
        var dataDir = AppState.I.DataDir;
        if (string.IsNullOrWhiteSpace(id) || WallpaperEngine.CachedHiResPreview(dataDir, id) is not null) return;

        System.Threading.Tasks.Task.Run(() =>
        {
            var path = WallpaperEngine.EnsureHiResPreview(id, dataDir);
            if (string.IsNullOrEmpty(path)) return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            dispatcher?.Invoke(() =>
            {
                if (!string.Equals(_selectedWallpaper?.Source?.Id, id, StringComparison.Ordinal)) return;
                _appearanceDraft.WeHiResPath = path;
                SaveAppearance();
            });
        });
    }

    /// <summary>选择背景图：复制进 data\images 并立即生效。</summary>
    private void SelectBackground()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择背景图片",
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog() != true) return;

            _appearanceDraft.BackgroundPath = AppState.I.ImportBackgroundImage(dialog.FileName);
            SaveAppearance();
            Notifier.Info("背景图已更新");
            StatusText = "背景图已更新";
        }
        catch (Exception ex)
        {
            StatusText = "设置背景图失败：" + ex.Message;
        }
    }

    /// <summary>清除背景图（data\images 中的原文件保留，方便下次恢复）。</summary>
    private void ClearBackground()
    {
        if (string.IsNullOrWhiteSpace(_appearanceDraft.BackgroundPath))
        {
            Notifier.Info("当前没有自定义背景图");
            return;
        }

        _appearanceDraft.BackgroundPath = "";
        SaveAppearance();
        Notifier.Info("背景图已清除");
        StatusText = "背景图已清除";
    }

    /// <summary>恢复默认外观：只重置蒙层/铺法/模糊，保留背景图。</summary>
    private void ResetAppearance()
    {
        _appearanceDraft.OverlayOpacity = 78;
        _appearanceDraft.Stretch = "UniformToFill";
        _appearanceDraft.BlurRadius = 0;

        AppState.I.SaveAppearance(_appearanceDraft);
        LoadAppearance();
        Notifier.Info("已恢复默认外观");
        StatusText = "已恢复默认外观";
    }

    // ===== 壁纸（Wallpaper Engine）操作 =====

    /// <summary>重新扫描订阅壁纸（用户新订阅壁纸后手动触发）。</summary>
    private void RescanWallpapers()
    {
        ReloadWallpapers(true);
        StatusText = WallpaperEngine.IsAvailable
            ? "已重新扫描 Wallpaper Engine 订阅壁纸"
            : "没检测到 Wallpaper Engine 的订阅目录";
    }

    /// <summary>读取订阅壁纸列表并同步界面；rescan=true 时强制重新扫盘。</summary>
    private void ReloadWallpapers(bool rescan = false)
    {
        // 重建列表时 ListBox 会把选中项清掉，这里先记住要恢复的壁纸路径
        var restorePath = _selectedWallpaper?.ProjectPath;
        if (string.IsNullOrWhiteSpace(restorePath)) restorePath = _appearanceDraft.WeProjectPath;

        _suppressWallpaperSync = true;
        try
        {
            var list = rescan ? WallpaperEngine.Reload() : WallpaperEngine.List();
            Wallpapers.Clear();
            foreach (var wallpaper in list) Wallpapers.Add(new WallpaperItem(wallpaper));

            WallpapersView.Refresh();

            WallpaperItem? restore = null;
            if (!string.IsNullOrWhiteSpace(restorePath))
            {
                foreach (var item in Wallpapers)
                {
                    if (!SamePath(item.ProjectPath, restorePath)) continue;
                    restore = item;
                    break;
                }
            }
            SetSelectedWallpaperSilently(restore);

            WeStatusText = WallpaperEngine.StatusText;
        }
        catch (Exception ex)
        {
            WeStatusText = "读取订阅壁纸失败：" + ex.Message;
        }
        finally
        {
            _suppressWallpaperSync = false;
        }

        OnPropertyChanged(nameof(HasNoWallpapers));
        OnPropertyChanged(nameof(EmptyWallpaperText));
    }

    /// <summary>切到「使用订阅壁纸」：优先当前选中项，其次列表第一项。</summary>
    private void UseWallpaperEngineSource()
    {
        _appearanceDraft.Source = BackgroundSource.WallpaperEngine;

        var item = _selectedWallpaper ?? Wallpapers.FirstOrDefault();
        if (item is not null)
        {
            SetSelectedWallpaperSilently(item);
            ApplyWallpaperFields(item);
        }
        else
        {
            WeStatusText = WallpaperEngine.IsAvailable
                ? "已切到订阅壁纸，但没找到可用的壁纸，点「重新扫描」再试一次"
                : "没检测到 Wallpaper Engine 的订阅目录，先安装并订阅壁纸再回来试";
        }

        SaveAppearance();
    }

    /// <summary>把壁纸信息写进外观草稿（不保存，由调用方统一保存）。</summary>
    private void ApplyWallpaperFields(WallpaperItem item)
    {
        var source = item.Source;
        _appearanceDraft.WeProjectPath = source.ProjectPath;
        _appearanceDraft.WePreviewPath = source.PreviewPath;
        _appearanceDraft.WeMediaPath = source.MediaPath;
        _appearanceDraft.WeKind = source.Kind;
        _appearanceDraft.WeTitle = source.DisplayTitle;
    }

    /// <summary>只更新选中字段并通知界面，不触发保存（重建列表、跟随当前壁纸时用）。</summary>
    private void SetSelectedWallpaperSilently(WallpaperItem? item)
    {
        if (ReferenceEquals(_selectedWallpaper, item)) return;
        _selectedWallpaper = item;
        OnPropertyChanged(nameof(SelectedWallpaper));
    }

    /// <summary>按搜索词重新过滤壁纸列表。</summary>
    private void FilterWallpapers()
    {
        WallpapersView.Refresh();
        OnPropertyChanged(nameof(HasNoWallpapers));
        OnPropertyChanged(nameof(EmptyWallpaperText));
    }

    private bool FilterWallpaper(object item)
    {
        if (item is not WallpaperItem wallpaper) return false;
        var keyword = (_wallpaperSearch ?? "").Trim();
        if (keyword.Length == 0) return true;
        return wallpaper.Title.Contains(keyword, StringComparison.CurrentCultureIgnoreCase)
               || wallpaper.KindText.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool SamePath(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Csv(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    private static string NumberText(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string PercentText(double value) => (value * 100).ToString("0.##", CultureInfo.InvariantCulture);

    private static string ParseDateText(string text, string label)
    {
        var raw = text.Trim();
        if (!DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new FormatException($"{label}请按 2026-06-24 的格式填写");
        return raw;
    }

    private static double ParseAmount(string text, string label)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new FormatException($"{label}请填写不小于 0 的数字");
        return value;
    }

    private static double ParsePercent(string text, string label)
    {
        var raw = text.Trim().TrimEnd('%', ' ');
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || value < 0 || value > 100)
            throw new FormatException($"{label}请填写 0 到 100 之间的百分数，例如 20");
        return value / 100.0;
    }

    /// <summary>「图片铺法」下拉项：显示文案与 Appearance.Stretch 取值的映射。</summary>
    public sealed class StretchOption
    {
        public StretchOption(string label, string value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }

        public string Value { get; }

        public override string ToString() => Label;
    }

    /// <summary>快捷键说明行。</summary>
    public sealed class ShortcutItem
    {
        public ShortcutItem(string key, string description)
        {
            Key = key;
            Description = description;
        }

        public string Key { get; }

        public string Description { get; }
    }

    /// <summary>壁纸列表项：包一层 WeWallpaper，界面只读这几个属性。</summary>
    public sealed class WallpaperItem
    {
        public WallpaperItem(WeWallpaper source)
        {
            Source = source;
        }

        public WeWallpaper Source { get; }

        public string Title => Source.DisplayTitle;

        public string KindText => Source.KindText;

        /// <summary>懒加载缩略图，可直接绑到 Image.Source；同一对象只解码一次。</summary>
        public ImageSource? Thumbnail => Source.Thumbnail;

        public string ProjectPath => Source.ProjectPath;

        public bool HasVideo => Source.HasVideo;

        public override string ToString() => Title + " · " + KindText;
    }
}
