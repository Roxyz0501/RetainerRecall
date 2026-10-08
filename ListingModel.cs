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
        if (count is < 1 or > 9999) throw new LocalizedException(L.M("MarketRange"));
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
    public Text Status { get; private set; } = L.M("ListingIdle");
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
        if (now < restartAfter) { Status = L.M("ListingPending"); return; }
        before = port.Read();
        if (before == null || port.Interference) { Stop(L.M("OpenSaleList")); return; }
        if (savedPrice is <= 0 or > 999999999 || stackLimit is < 1 or > 9999) { Stop(L.M("InvalidPriceQuantity")); return; }
        item = itemId; retainer = fromRetainer; price = savedPrice; limit = stackLimit;
        delay = double.IsFinite(seconds) ? Math.Clamp(seconds, 0.1, 30) : 1.5;
        Completed = 0; State = Stage.Delay; next = now + delay;
        Status = L.M("ListingStart", price, limit);
    }
    public void Stop(Text? reason = null)
    {
        if (State == Stage.Acknowledge) restartAfter = Math.Max(restartAfter, deadline);
        State = Stage.Idle; before = null; source = null; Status = reason ?? L.M("ListingStopped");
    }
    public void Tick(double now)
    {
        if (!Running || before == null) return;
        var current = port.Read();
        if (current == null || !ListingRules.SameSession(before, current)) { Stop(L.M("ListingSessionChanged")); return; }
        if (port.Interference) { Stop(L.M("ListingInterference")); return; }
        if (State == Stage.Acknowledge)
        {
            var added = ListingRules.HasExpectedAddition(before, current, source!, quantity, price);
            var removed = ListingRules.HasExpectedStockChange(before, current, source!, quantity);
            if (added && removed)
            {
                Completed++; before = current; source = null; State = Stage.Delay; next = now + delay;
                Status = L.M("ListingProgress", Completed, price);
            }
            else if ((!added && !before.Offers.SequenceEqual(current.Offers)) || (!removed && !before.Stock.SequenceEqual(current.Stock)))
                Stop(L.M("UnexpectedStock"));
            else if (now >= deadline) Stop(L.M("ListingTimeout"));
            return;
        }
        if (!before.Offers.SequenceEqual(current.Offers) || !before.Stock.SequenceEqual(current.Stock)) { Stop(L.M("StockChanged")); return; }
        if (now < next) return;
        if (State != Stage.Delay && now >= deadline) { Stop(L.M("DialogTimeout")); return; }
        switch (State)
        {
            case Stage.Delay:
                if (current.Offers.Length >= current.Capacity) { Stop(L.M("SlotsFull", Completed)); return; }
                source = ListingRules.Next(current.Stock, item, retainer, limit);
                if (source == null) { Stop(L.M("ListingDone", Completed)); return; }
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
                if (quantity > dialog.MaxQuantity || price > dialog.MaxPrice) { Stop(L.M("GameLimit")); return; }
                dialogAddress = dialog.Address;
                port.Fill(source!, dialog, quantity, price);
                State = Stage.Verify; next = now + 0.3;
                break;
            case Stage.Verify:
                var ready = port.Dialog(source!);
                if (ready == null || ready.Address != dialogAddress || ready.Quantity != quantity || ready.Price != price)
                { Stop(L.M("VerifyFailed")); return; }
                State = Stage.Acknowledge; deadline = now + 15;
                port.Confirm(source!, ready, quantity, price);
                break;
        }
    }
}
