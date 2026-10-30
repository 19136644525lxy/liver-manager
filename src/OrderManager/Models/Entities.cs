namespace OrderManager.Models;

public class Runner
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public double Rate { get; set; } = 0.8;
    public string Note { get; set; } = "";
    public bool Active { get; set; } = true;

    public string RateText => $"{Rate * 100:0.#}%";
}

public class Item
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public double Price { get; set; }
    public string Note { get; set; } = "";

    public string PriceText => Price == 0 ? "—" : $"¥{Price:0.##}";
}

public class Checkin
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string Date { get; set; } = "";
}
