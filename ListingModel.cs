namespace RetainerRecall;

public sealed record Stock(int Inventory, int Slot, uint ItemId, byte Flags, int Quantity, int StackSize);
public sealed record Offer(int Slot, uint ItemId, byte Flags, int Quantity, long Price);
public sealed record ListingFrame(ulong Character, ulong Retainer, nint Window, Stock[] Stock, Offer[] Offers, int Capacity);
public sealed record SaleDialog(nint Address, int Quantity, int Price, int MaxQuantity, int MaxPrice);

public sealed class SessionPrices
{
    private ulong character;
    private readonly Dictionary<uint, (int Price, double Order)> prices = [];
    private double sequence;
    public int Count => prices.Count;
    public void SetCharacter(ulong value) { if (value == character && value != 0) return; character = value; prices.Clear(); }
    public void Clear() { character = 0; prices.Clear(); }
    public bool TryGet(uint item, out int price) { var found = prices.TryGetValue(item, out var value); price = value.Price; return found; }
    public void Remember(uint item, int price, double? order = null)
    {
        var stamp = order ?? ++sequence;
        if (character == 0 || item == 0 || price is <= 0 or > 999999999 || !double.IsFinite(stamp)) return;
        if (prices.TryGetValue(item, out var previous) && previous.Order > stamp) return;
        prices[item] = (price, stamp);
    }
}

public static class StackLimitPolicy
{
    public static int ReadMarketbuddy(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        var enabled = root.GetProperty("UseMaxStackSize").GetBoolean();
        var count = root.GetProperty("MaximumStackSize").GetInt32();
        if (count is < 1 or > 9999) throw new InvalidOperationException("Marketbuddyの数量が範囲外です");
        return enabled ? count : 99;
    }
}

public static class ListingRules
{
    public static Stock? Next(IEnumerable<Stock> stocks, uint item, bool retainer, int limit) => stocks
        .Where(x => x.ItemId == item && x.Quantity > 0 && (x.Inventory >= 10000) == retainer)
        .OrderByDescending(x => x.Quantity >= Math.Min(limit, x.StackSize))
        .ThenByDescending(x => x.Quantity).ThenBy(x => x.Inventory).ThenBy(x => x.Slot).FirstOrDefault();
    public static int Quantity(Stock stock, int limit) => Math.Min(stock.Quantity, Math.Min(stock.StackSize, limit));
    public static bool SameSession(ListingFrame a, ListingFrame b) => a.Character == b.Character && a.Retainer == b.Retainer && a.Window == b.Window;
    public static bool HasExpectedAddition(ListingFrame before, ListingFrame after, Stock item, int quantity, int price) =>
        after.Offers.Length == before.Offers.Length + 1 && before.Offers.All(after.Offers.Contains) &&
        after.Offers.Except(before.Offers).Single() is var added && added.ItemId == item.ItemId && added.Flags == item.Flags && added.Quantity == quantity && added.Price == price;
    public static bool HasExpectedStockChange(ListingFrame before, ListingFrame after, Stock source, int quantity)
    {
        var expected = before.Stock.Select(x => x == source ? x with { Quantity = x.Quantity - quantity } : x).Where(x => x.Quantity > 0);
        return expected.SequenceEqual(after.Stock);
    }
    public static uint? ConfirmedPriceItem(ListingFrame before, ListingFrame after, int quantity, int price)
    {
        if (!SameSession(before, after)) return null;
        var changed = after.Offers.Except(before.Offers).Where(x => x.Quantity == quantity && x.Price == price).ToArray();
        return changed.Length == 1 ? changed[0].ItemId : null;
    }
}

public interface IListingPort
{
    ListingFrame? Read();
    bool Interference { get; }
    void OpenMenu(Stock source);
    bool SelectSale(Stock source);
    SaleDialog? Dialog(Stock source);
    void Fill(Stock source, SaleDialog dialog, int quantity, int price);
    void Confirm(Stock source, SaleDialog dialog, int quantity, int price);
}

public sealed class ListingRun(IListingPort port)
{
    public enum Stage { Idle, Delay, Menu, Dialog, Verify, Acknowledge }
    public Stage State { get; private set; }
    public bool Running => State != Stage.Idle;
    public string Status { get; private set; } = "連続出品: 待機中";
    public int Completed { get; private set; }
    private ListingFrame? before;
    private Stock? source;
    private uint item;
    private bool retainer;
    private int price, limit, quantity;
    private double next, deadline, delay, restartAfter;
    private nint dialogAddress;
    public void Start(uint itemId, bool fromRetainer, int savedPrice, int stackLimit, double seconds, double now)
    {
        if (Running) return;
        if (now < restartAfter) { Status = "前回の出品結果を待機中です"; return; }
        before = port.Read();
        if (before == null || port.Interference) { Stop("販売リストを開き、他の操作を終了してください"); return; }
        if (savedPrice is <= 0 or > 999999999 || stackLimit is < 1 or > 9999) { Stop("価格または数量の設定が不正です"); return; }
        item = itemId; retainer = fromRetainer; price = savedPrice; limit = stackLimit;
        delay = double.IsFinite(seconds) ? Math.Clamp(seconds, 0.1, 30) : 1.5;
        Completed = 0; State = Stage.Delay; next = now + delay;
        Status = $"連続出品: 単価{price:N0}ギル / 上限{limit}個";
    }
    public void Stop(string reason = "連続出品を停止しました")
    {
        if (State == Stage.Acknowledge) restartAfter = Math.Max(restartAfter, deadline);
        State = Stage.Idle; before = null; source = null; Status = reason;
    }
    public void Tick(double now)
    {
        if (!Running || before == null) return;
        var current = port.Read();
        if (current == null || !ListingRules.SameSession(before, current)) { Stop("画面・キャラクター・リテイナーが変わったため出品を停止しました"); return; }
        if (port.Interference) { Stop("別の画面操作を検出したため出品を停止しました"); return; }
        if (State == Stage.Acknowledge)
        {
            var added = ListingRules.HasExpectedAddition(before, current, source!, quantity, price);
            var removed = ListingRules.HasExpectedStockChange(before, current, source!, quantity);
            if (added && removed)
            {
                Completed++; before = current; source = null; State = Stage.Delay; next = now + delay;
                Status = $"{Completed}件出品済み / 単価{price:N0}ギル";
            }
            else if ((!added && !before.Offers.SequenceEqual(current.Offers)) || (!removed && !before.Stock.SequenceEqual(current.Stock)))
                Stop("想定外の出品・所持品変更を検出したため停止しました");
            else if (now >= deadline) Stop("出品結果を確認できず停止しました。再送はしません");
            return;
        }
        if (!before.Offers.SequenceEqual(current.Offers) || !before.Stock.SequenceEqual(current.Stock)) { Stop("出品・所持品が変更されたため停止しました"); return; }
        if (now < next) return;
        if (State != Stage.Delay && now >= deadline) { Stop("出品画面の操作がタイムアウトしました"); return; }
        switch (State)
        {
            case Stage.Delay:
                if (current.Offers.Length >= current.Capacity) { Stop($"出品枠が埋まりました（{Completed}件出品）"); return; }
                source = ListingRules.Next(current.Stock, item, retainer, limit);
                if (source == null) { Stop($"対象アイテムの出品が完了しました（{Completed}件）"); return; }
                quantity = ListingRules.Quantity(source, limit);
                State = Stage.Menu; deadline = now + 10; next = now + 0.2;
                port.OpenMenu(source);
                break;
            case Stage.Menu:
                if (port.SelectSale(source!)) { State = Stage.Dialog; deadline = now + 10; next = now + 0.2; }
                break;
            case Stage.Dialog:
                var dialog = port.Dialog(source!);
                if (dialog == null) return;
                if (quantity > dialog.MaxQuantity || price > dialog.MaxPrice) { Stop("ゲームの出品可能範囲を超えるため停止しました"); return; }
                dialogAddress = dialog.Address;
                port.Fill(source!, dialog, quantity, price);
                State = Stage.Verify; next = now + 0.3;
                break;
            case Stage.Verify:
                var ready = port.Dialog(source!);
                if (ready == null || ready.Address != dialogAddress || ready.Quantity != quantity || ready.Price != price)
                { Stop("出品価格・数量の一致を確認できないため停止しました"); return; }
                State = Stage.Acknowledge; deadline = now + 15;
                port.Confirm(source!, ready, quantity, price);
                break;
        }
    }
}
