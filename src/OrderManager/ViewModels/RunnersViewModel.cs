using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using OrderManager.Models;
using OrderManager.Mvvm;
using OrderManager.Services;

namespace OrderManager.ViewModels;

/// <summary>跑手管理页：跑手列表的增删改。</summary>
public sealed class RunnersViewModel : ObservableObject
{
    private Runner? _selectedRunner;
    private bool _isEditing;
    private long _editingId;
    private string _editName = "";
    private string _editRate = "";
    private string _editNote = "";
    private bool _editActive = true;
    private string _statusText = "";

    public RunnersViewModel()
    {
        NewRunnerCommand = new RelayCommand(BeginNew);
        EditRunnerCommand = new RelayCommand(parameter =>
        {
            if (parameter is Runner runner) BeginEdit(runner);
        });
        DeleteRunnerCommand = new RelayCommand(parameter =>
        {
            if (parameter is Runner runner) Delete(runner);
        });
        SaveRunnerCommand = new RelayCommand(Save);
        CancelEditCommand = new RelayCommand(() => IsEditing = false);
    }

    public ObservableCollection<Runner> Runners { get; } = new();

    public ICommand NewRunnerCommand { get; }
    public ICommand EditRunnerCommand { get; }
    public ICommand DeleteRunnerCommand { get; }
    public ICommand SaveRunnerCommand { get; }
    public ICommand CancelEditCommand { get; }

    public Runner? SelectedRunner
    {
        get => _selectedRunner;
        set => SetProperty(ref _selectedRunner, value);
    }

    public bool HasRunners => Runners.Count > 0;
    public bool ShowNoRunners => Runners.Count == 0;
    public string SummaryText => $"共 {Runners.Count} 位跑手 · 启用 {Runners.Count(r => r.Active)} 位";

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (SetProperty(ref _isEditing, value)) OnPropertyChanged(nameof(IsNotEditing));
        }
    }

    public bool IsNotEditing => !_isEditing;

    public long EditingId => _editingId;
    public string EditorTitle => _editingId == 0 ? "新增跑手" : "编辑跑手";

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    public string EditRate
    {
        get => _editRate;
        set => SetProperty(ref _editRate, value);
    }

    public string EditNote
    {
        get => _editNote;
        set => SetProperty(ref _editNote, value);
    }

    public bool EditActive
    {
        get => _editActive;
        set => SetProperty(ref _editActive, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string XiaoyuHint =>
        $"「{AppState.I.Rules.XiaoyuName}」的订单：助手固定收取 {RateLabel(AppState.I.Rules.RunnerXiaoyuRate)}，其余部分归跑手。";

    public string RuleHint =>
        $"其他跑手默认拿 {RateLabel(AppState.I.Rules.RunnerRate)}，助手分成 {RateLabel(AppState.I.Rules.RunnerAssistantRate)}；比例可在「设置」页统一调整。";

    /// <summary>重新读取跑手列表。</summary>
    public void Refresh()
    {
        long previousId = _selectedRunner?.Id ?? 0;
        Runners.Clear();
        foreach (var runner in AppState.I.Db.ListRunners()) Runners.Add(runner);

        OnPropertyChanged(nameof(HasRunners));
        OnPropertyChanged(nameof(ShowNoRunners));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(XiaoyuHint));
        OnPropertyChanged(nameof(RuleHint));

        _selectedRunner = previousId == 0 ? null : Runners.FirstOrDefault(r => r.Id == previousId);
        OnPropertyChanged(nameof(SelectedRunner));
    }

    private void BeginNew()
    {
        _editingId = 0;
        EditName = "";
        EditRate = RateText(AppState.I.Rules.RunnerRate);
        EditNote = "";
        EditActive = true;
        StatusText = "";
        OnPropertyChanged(nameof(EditingId));
        OnPropertyChanged(nameof(EditorTitle));
        IsEditing = true;
    }

    private void BeginEdit(Runner runner)
    {
        _editingId = runner.Id;
        EditName = runner.Name;
        EditRate = RateText(runner.Rate);
        EditNote = runner.Note;
        EditActive = runner.Active;
        StatusText = "";
        OnPropertyChanged(nameof(EditingId));
        OnPropertyChanged(nameof(EditorTitle));
        IsEditing = true;
    }

    private void Save()
    {
        bool isNew = _editingId == 0;
        var name = EditName.Trim();
        if (name.Length == 0)
        {
            StatusText = "请填写跑手姓名";
            return;
        }

        var rawRate = EditRate.Trim().TrimEnd('%', ' ');
        double percent;
        if (!double.TryParse(rawRate, NumberStyles.Float, CultureInfo.CurrentCulture, out percent)
            && !double.TryParse(rawRate, NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
        {
            StatusText = "分成比例请填写 0 到 100 之间的数字";
            return;
        }
        if (percent < 0 || percent > 100)
        {
            StatusText = "分成比例需在 0 到 100 之间";
            return;
        }
        if (Runners.Any(r => r.Id != _editingId && string.Equals(r.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase)))
        {
            StatusText = $"「{name}」已存在，请换一个姓名";
            return;
        }

        var runner = new Runner
        {
            Id = _editingId,
            Name = name,
            Rate = Math.Round(percent / 100.0, 4),
            Note = EditNote.Trim(),
            Active = EditActive,
        };
        if (isNew) AppState.I.Db.InsertRunner(runner);
        else AppState.I.Db.UpdateRunner(runner);

        IsEditing = false;
        Refresh();
        AppState.I.NotifyChanged();
        StatusText = isNew ? $"已新增跑手「{name}」" : $"已保存跑手「{name}」";
        Notifier.Info("跑手已保存");
    }

    private void Delete(Runner runner)
    {
        var answer = MessageBox.Show(
            $"确定删除跑手「{runner.Name}」吗？删除后不可恢复。",
            "删除跑手", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        AppState.I.Db.DeleteRunner(runner.Id);
        if (_editingId == runner.Id) IsEditing = false;
        Refresh();
        AppState.I.NotifyChanged();
        StatusText = $"已删除跑手「{runner.Name}」";
        Notifier.Info("跑手已删除");
    }

    private static string RateText(double rate) => (rate * 100).ToString("0.##", CultureInfo.InvariantCulture);
    private static string RateLabel(double rate) => RateText(rate) + "%";
}
