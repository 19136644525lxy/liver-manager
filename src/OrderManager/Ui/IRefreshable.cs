namespace OrderManager.Ui;

/// <summary>
/// 页面被切换到前台时由主窗口调用。
/// 页面自己用 AppState.DataVersion 判断"数据有没有变"，没变就直接返回，
/// 避免每次切页都重新查库、重建列表。
/// </summary>
public interface IRefreshable
{
    void OnShown();
}
