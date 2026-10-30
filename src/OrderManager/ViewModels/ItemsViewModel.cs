using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using OrderManager.Models;
using OrderManager.Mvvm;
using OrderManager.Services;

namespace OrderManager.ViewModels;

/// <summary>价目表的一个分类分组（可折叠）。</summary>
public sealed class ItemGroup : ObservableObject
{
    private bool _isExpanded = true;

    public string Category { get; init; } = "";
    public ObservableCollection<Item> Items { get; } = new();
    public string SummaryText => $"共 {Items.Count} 项";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

/// <summary>价目表页：按分类分组展示与增删改查。</summary>
public sealed class ItemsViewModel : ObservableObject
{
    private static readonly string[] CategoryItems = { "托管服务", "任务活动", "地图满探索" };

    private string _keyword = "";
    private bool _isEditing;
    private long _editingId;
    private string _editName = "";
    private string _editCategory = CategoryItems[0];
    private string _editPrice = "";
    private string _editNote = "";
    private string _statusText = "";

    public ItemsViewModel()
    {
        NewItemCommand = new RelayCommand(BeginNew);
        EditItemCommand = new RelayCommand(parameter =>
        {
            if (parameter is Item item) BeginEdit(item);
        });
        DeleteItemCommand = new RelayCommand(parameter =>
        {
            if (parameter is Item item) Delete(item);
        });
        SaveItemCommand = new RelayCommand(Save);
        CancelEditCommand = new RelayCommand(() => IsEditing = false);
    }

    public ObservableCollection<ItemGroup> Groups { get; } = new();

    public ICommand NewItemCommand { get; }
    public ICommand EditItemCommand { get; }
    public ICommand DeleteItemCommand { get; }
    public ICommand SaveItemCommand { get; }
    public ICommand CancelEditCommand { get; }

    public IReadOnlyList<string> Categories => CategoryItems;

    public string Keyword
    {
        get => _keyword;
        set
        {
            if (SetProperty(ref _keyword, value ?? "")) Refresh();
        }
    }

    public bool HasGroups => Groups.Count > 0;
    public bool ShowNoItems => Groups.Count == 0;
    public string TotalText => $"共 {Groups.Sum(g => g.Items.Count)} 项价目";

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
    public string EditorTitle => _editingId == 0 ? "新增价目" : "编辑价目";

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    public string EditCategory
    {
        get => _editCategory;
        set => SetProperty(ref _editCategory, value ?? "");
    }

    public string EditPrice
    {
        get => _editPrice;
        set => SetProperty(ref _editPrice, value);
    }

    public string EditNote
    {
        get => _editNote;
        set => SetProperty(ref _editNote, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>按当前搜索词重新分组。</summary>
    public void Refresh()
    {
        var items = AppState.I.Db.ListItems();
        var keyword = _keyword.Trim();
        if (keyword.Length > 0)
        {
            items = items.Where(i =>
                    Contains(i.Name, keyword) || Contains(i.Category, keyword)
                    || Contains(i.Note, keyword))
                .ToList();
        }

        Groups.Clear();
        foreach (var group in items
                     .GroupBy(i => string.IsNullOrWhiteSpace(i.Category) ? "未分类" : i.Category)
                     .OrderBy(g => CategoryRank(g.Key))
                     .ThenBy(g => g.Key, StringComparer.CurrentCulture))
        {
            var model = new ItemGroup { Category = group.Key };
            foreach (var item in group.OrderBy(i => i.Id)) model.Items.Add(item);
            Groups.Add(model);
        }

        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(ShowNoItems));
        OnPropertyChanged(nameof(TotalText));
    }

    private void BeginNew()
    {
        _editingId = 0;
        EditName = "";
        EditCategory = _editCategory.Length > 0 ? _editCategory : CategoryItems[0];
        EditPrice = "";
        EditNote = "";
        StatusText = "";
        OnPropertyChanged(nameof(EditingId));
        OnPropertyChanged(nameof(EditorTitle));
        IsEditing = true;
    }

    private void BeginEdit(Item item)
    {
        _editingId = item.Id;
        EditName = item.Name;
        EditCategory = string.IsNullOrWhiteSpace(item.Category) ? CategoryItems[0] : item.Category;
        EditPrice = item.Price == 0 ? "" : item.Price.ToString("0.##", CultureInfo.InvariantCulture);
        EditNote = item.Note;
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
            StatusText = "请填写价目名称";
            return;
        }

        var category = EditCategory.Trim();
        if (category.Length == 0)
        {
            StatusText = "请选择或填写分类";
            return;
        }

        double price = 0;
        var rawPrice = EditPrice.Trim();
        if (rawPrice.Length > 0)
        {
            if (!double.TryParse(rawPrice, NumberStyles.Float, CultureInfo.CurrentCulture, out price)
                && !double.TryParse(rawPrice, NumberStyles.Float, CultureInfo.InvariantCulture, out price))
            {
                StatusText = "价格请填写数字，例如 30";
                return;
            }
            if (price < 0)
            {
                StatusText = "价格不能为负数";
                return;
            }
        }

        var item = new Item
        {
            Id = _editingId,
            Name = name,
            Category = category,
            Price = Math.Round(price, 2),
            Note = EditNote.Trim(),
        };
        if (isNew) AppState.I.Db.InsertItem(item);
        else AppState.I.Db.UpdateItem(item);

        IsEditing = false;
        Refresh();
        AppState.I.NotifyChanged();
        StatusText = isNew ? $"已新增价目「{name}」" : $"已保存价目「{name}」";
        Notifier.Info("价目已保存");
    }

    private void Delete(Item item)
    {
        var answer = MessageBox.Show(
            $"确定删除价目「{item.Name}」吗？删除后不可恢复。",
            "删除价目", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        AppState.I.Db.DeleteItem(item.Id);
        if (_editingId == item.Id) IsEditing = false;
        Refresh();
        AppState.I.NotifyChanged();
        StatusText = $"已删除价目「{item.Name}」";
        Notifier.Info("价目已删除");
    }

    private static bool Contains(string? text, string keyword) =>
        !string.IsNullOrWhiteSpace(text) && text.Contains(keyword, StringComparison.CurrentCultureIgnoreCase);

    private static int CategoryRank(string category) => category switch
    {
        "托管服务" => 0,
        "任务活动" => 1,
        "地图满探索" => 2,
        _ => 9,
    };
}
