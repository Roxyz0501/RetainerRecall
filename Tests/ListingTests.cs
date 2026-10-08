using RetainerRecall;

static class ListingTests
{
    public static void Run(Action<bool, string> check)
    {
        var prices = new SessionPrices(); prices.SetCharacter(10);
        check(!prices.TryGet(100, out _), "no price after login");
        prices.Remember(100, 250); prices.Remember(100, 275);
        check(prices.TryGet(100, out var price) && price == 275 && prices.Count == 1, "last price shared by item ID regardless of quality");
        prices.SetCharacter(10); check(prices.TryGet(100, out _), "same session retains price");
        prices.SetCharacter(11); check(!prices.TryGet(100, out _), "character switch clears prices");
        prices.Remember(100, 5); prices.SetCharacter(0); prices.SetCharacter(11); check(prices.Count == 0, "relogin same character clears prices");
        prices.Remember(100, 0); prices.Remember(101, -1); check(prices.Count == 0, "invalid prices ignored");
        prices.Remember(100, 300, 20); prices.Remember(100, 200, 10);
        check(prices.TryGet(100, out price) && price == 300, "late older confirmation cannot replace newest price");
        check(StackLimitPolicy.ReadMarketbuddy("{\"UseMaxStackSize\":true,\"MaximumStackSize\":2}") == 2, "read actual Marketbuddy setting names");
        check(StackLimitPolicy.ReadMarketbuddy("{\"UseMaxStackSize\":false,\"MaximumStackSize\":2}") == 99, "Marketbuddy disabled limit defaults to 99");
        foreach (var json in new[] { "{}", "{", "{\"UseMaxStackSize\":true,\"MaximumStackSize\":0}", "{\"UseMaxStackSize\":true,\"MaximumStackSize\":10000}" })
        {
            var rejected = false; try { StackLimitPolicy.ReadMarketbuddy(json); } catch { rejected = true; }
            check(rejected, "invalid Marketbuddy config fails closed " + json);
        }
        var small = new Stock(0, 0, 100, 0, 20, 999);
        var full = new Stock(0, 1, 100, 1, 130, 999);
        var other = new Stock(0, 2, 101, 0, 999, 999);
        check(ListingRules.Next([small, full, other], 100, false, 99) == full, "prefer stack with 99 available across NQ HQ");
        check(ListingRules.Quantity(full, 99) == 99, "default 99 quantity");
        check(ListingRules.Quantity(small, 99) == 20, "remainder quantity");
        check(ListingRules.Quantity(full, 2) == 2, "Marketbuddy limit of two");
        check(ListingRules.Quantity(full with { StackSize = 1, Quantity = 1 }, 99) == 1, "nonstackable quantity one");
        check(ListingRules.Next([full, full with { Inventory = 10000 }], 100, true, 99)?.Inventory == 10000, "retainer inventory scope");
        var bagGear = new Stock(0, 0, 100, 0, 1, 1);
        var armouryGear = new Stock(3202, 5, 100, 1, 1, 1);
        check(ListingRules.Next([armouryGear], 100, false, 99) == armouryGear, "player search includes armoury equipment");
        check(ListingRules.Next([armouryGear], 100, true, 99) == null, "retainer search excludes player armoury");

        (PostingPort port, ListingRun run) Create()
        {
            var port = new PostingPort(); return (port, new(port));
        }
        void Start(ListingRun run) => run.Start(100, false, 275, 99, 1, 0);
        void Send(ListingRun run) { run.Tick(1); run.Tick(1.3); run.Tick(1.6); run.Tick(2); }
        var (p, r) = Create(); Start(r);
        r.Tick(0.9); check(p.Opens == 0, "listing initial delay");
        r.Tick(1); check(p.Opens == 1 && p.Selects == 0 && p.Confirms == 0, "listing opens context menu first");
        r.Tick(1.3); check(p.Selects == 1 && p.Confirms == 0, "listing selects native menu before filling");
        r.Tick(1.6); check(p.Fills == 1 && p.Confirms == 0 && p.Quantity == 99 && p.Price == 275, "fill saved price and quantity without confirming");
        r.Tick(2); check(p.Confirms == 1 && r.State == ListingRun.Stage.Acknowledge, "verify then confirm once");
        r.Tick(3); check(p.Confirms == 1, "no duplicate confirmation while awaiting server");
        var baseline = p.Frame!;
        p.Frame = baseline with { Offers = [new(0, 100, 0, 99, 275)] }; r.Tick(3.1);
        check(r.Completed == 0, "listing addition alone is insufficient");
        p.Frame = p.Frame with { Stock = [PostingPort.Item with { Quantity = 21 }] }; r.Tick(3.2);
        check(r.Completed == 1 && r.Running, "listing source decrement acknowledgement");
        check(ListingRules.ConfirmedPriceItem(baseline, p.Frame, 99, 275) == 100, "successful confirmation learns base item ID");
        check(ListingRules.ConfirmedPriceItem(baseline, baseline, 99, 275) == null, "cancelled or unchanged listing does not learn price");
        check(ListingRules.ConfirmedPriceItem(baseline, p.Frame with { Character = 2 }, 99, 275) == null, "cannot learn another character price");
        r.Tick(4.19); check(p.Opens == 1, "listing delay starts after acknowledgement");
        r.Tick(4.21); r.Tick(4.5); r.Tick(4.8); r.Tick(5.2);
        check(p.Confirms == 2 && p.Quantity == 21, "automatically lists remaining partial stack");
        p.Frame = p.Frame with { Offers = [..p.Frame.Offers, new(1, 100, 0, 21, 275)], Stock = [] }; r.Tick(5.3); r.Tick(6.4);
        check(!r.Running && r.Completed == 2, "complete when all matching items are exhausted");

        (p, r) = Create(); r.Start(100, false, 275, 99, 0, 0);
        r.Tick(0.09); check(p.Opens == 0, "listing minimum delay enforced");
        r.Tick(0.1); check(p.Opens == 1 && p.Selects == 0, "listing supports 0.1 second delay with menu wait");
        r.Tick(0.31); r.Tick(0.52); r.Tick(0.83); r.Tick(0.94);
        check(p.Confirms == 1 && p.Opens == 1 && r.State == ListingRun.Stage.Acknowledge, "minimum delay still waits for acknowledgement without retry");

        (p, r) = Create(); p.Frame = p.Frame! with { Stock = [bagGear, armouryGear] }; Start(r); Send(r);
        check(p.LastSource == bagGear && p.Quantity == 1, "equipment listing begins with player bag item");
        p.Frame = p.Frame with { Stock = [armouryGear], Offers = [new(0, 100, 0, 1, 275)] }; r.Tick(2.1);
        r.Tick(3.2); r.Tick(3.5); r.Tick(3.8); r.Tick(4.2);
        check(p.LastSource == armouryGear && p.Confirms == 2 && p.Quantity == 1, "batch continues into armoury with shared HQ price");
        p.Frame = p.Frame with { Stock = [], Offers = [..p.Frame.Offers, new(1, 100, 1, 1, 275)] }; r.Tick(4.3); r.Tick(5.4);
        check(!r.Running && r.Completed == 2, "armoury listing waits for source and market updates");

        (p, r) = Create(); p.Frame = p.Frame! with { Capacity = 1, Offers = [new(0, 500, 0, 1, 50)] }; Start(r); r.Tick(1);
        check(!r.Running && p.Opens == 0, "full listing slots prevent opening a menu");
        (p, r) = Create(); Start(r); Send(r); r.Tick(17);
        check(!r.Running && p.Confirms == 1, "listing acknowledgement timeout has no retry");
        (p, r) = Create(); Start(r); p.MenuReady = false; r.Tick(1); r.Tick(2); r.Tick(11);
        check(!r.Running && p.Selects == 0 && p.Confirms == 0, "missing menu times out safely");
        (p, r) = Create(); Start(r); r.Tick(1); r.Tick(1.3); p.DialogReady = false; r.Tick(12);
        check(!r.Running && p.Confirms == 0, "missing sale dialog times out");
        (p, r) = Create(); Start(r); r.Tick(1); r.Tick(1.3); r.Tick(1.6); p.Price = 999; r.Tick(2);
        check(!r.Running && p.Confirms == 0, "price interference prevents confirmation");
        (p, r) = Create(); Start(r); r.Tick(1); r.Tick(1.3); r.Tick(1.6); p.Quantity = 1; r.Tick(2);
        check(!r.Running && p.Confirms == 0, "quantity interference prevents confirmation");
        (p, r) = Create(); Start(r); r.Tick(1); r.Tick(1.3); p.MaxPrice = 100; r.Tick(1.6);
        check(!r.Running && p.Fills == 0, "game price limit respected");
        (p, r) = Create(); Start(r); r.Tick(1); r.Tick(1.3); p.MaxQuantity = 10; r.Tick(1.6);
        check(!r.Running && p.Fills == 0, "game quantity limit respected");
        (p, r) = Create(); Start(r); r.Tick(1); p.Frame = p.Frame! with { Stock = [] }; r.Tick(1.3);
        check(!r.Running && p.Selects == 0, "changed source stops before menu selection");
        (p, r) = Create(); Start(r); Send(r); p.Frame = p.Frame! with { Offers = [new(0, 100, 0, 99, 999)] }; r.Tick(2.1);
        check(!r.Running && r.Completed == 0, "unexpected server price stops subsequent listings");
        foreach (var changed in new[] { "character", "retainer", "window" })
        {
            (p, r) = Create(); Start(r);
            p.Frame = changed switch { "character" => p.Frame! with { Character = 2 }, "retainer" => p.Frame! with { Retainer = 2 }, _ => p.Frame! with { Window = 2 } };
            r.Tick(1); check(!r.Running && p.Opens == 0, "listing " + changed + " switch stops");
        }
        (p, r) = Create(); Start(r); p.Interference = true; r.Tick(1); check(!r.Running && p.Opens == 0, "unexpected dialog stops listing");
        (p, r) = Create(); Start(r); Send(r); r.Stop(); Start(r); check(!r.Running, "cancelled confirmation cannot be immediately restarted");
        (p, r) = Create(); r.Start(100, false, 0, 99, 1, 0); check(!r.Running, "zero saved price cannot start listing");
        (p, r) = Create(); r.Start(100, false, 275, 0, 1, 0); check(!r.Running, "invalid stack limit cannot start listing");
    }

    sealed class PostingPort : IListingPort
    {
        public static readonly Stock Item = new(0, 0, 100, 0, 120, 999);
        public ListingFrame? Frame = new(1, 1, 1, [Item], [], 20);
        public bool Interference { get; set; }
        public int Opens, Selects, Fills, Confirms, Quantity, Price;
        public int MaxQuantity = 999, MaxPrice = 999999999;
        public bool MenuReady = true, DialogReady = true;
        public Stock? LastSource;
        public ListingFrame? Read() => Frame;
        public void OpenMenu(Stock source) { LastSource = source; Opens++; }
        public bool SelectSale(Stock source) { if (!MenuReady) return false; Selects++; return true; }
        public SaleDialog? Dialog(Stock source) => DialogReady ? new(1, Quantity, Price, MaxQuantity, MaxPrice) : null;
        public void Fill(Stock source, SaleDialog dialog, int quantity, int price) { Quantity = quantity; Price = price; Fills++; }
        public void Confirm(Stock source, SaleDialog dialog, int quantity, int price) => Confirms++;
    }
}
