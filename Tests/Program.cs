using RetainerRecall;

var passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
(FakePort p, RecallRun r) Create() { var p = new FakePort(); return (p, new(p)); }
var (p, r) = Create();
r.Start(Destination.Player, 1.5, 0); r.Tick(1.4); Check(p.Moves == 0, "initial delay");
r.Tick(1.5); r.Tick(2); Check(p.Moves == 1, "one operation in flight");
p.S = p.S! with { Listings = [FakePort.B] }; r.Tick(2.1); Check(r.Completed == 0, "source removal alone is insufficient");
p.CountValue = 3; r.Tick(2.2); Check(r.Completed == 1 && r.Running, "destination acknowledgement");
r.Tick(3.69); Check(p.Moves == 1, "delay starts after acknowledgement");
r.Tick(3.71); r.Tick(3.95); Check(p.Moves == 2 && p.Last == FakePort.B, "next listing selected");
p.S = p.S with { Listings = [] }; p.CountValue = 7; r.Tick(4); Check(!r.Running && r.Completed == 2, "all items complete");
(p, r) = Create(); r.Start(Destination.Retainer, 0, 0); r.Tick(0.09); Check(p.Opens == 0, "minimum delay enforced"); r.Tick(0.1); Check(p.Opens == 1 && p.Moves == 0, "recall supports 0.1 second delay with menu wait"); r.Tick(0.31); Check(p.Target == Destination.Retainer, "retainer destination");
r.Tick(16); Check(!r.Running && p.Moves == 1, "timeout never retries");
(p, r) = Create(); r.Start(Destination.Player, double.NaN, 0); r.Tick(1); Check(p.Moves == 0, "invalid delay sanitized"); r.Tick(1.5); r.Tick(1.8); Check(p.Moves == 1, "sanitized delay executes");
(p, r) = Create(); p.Space = false; r.Start(Destination.Player, 1, 0); r.Tick(1); Check(!r.Running && p.Moves == 0, "full inventory stops");
(p, r) = Create(); p.CountValue = -1; r.Start(Destination.Player, 1, 0); r.Tick(1); Check(!r.Running && p.Moves == 0, "unloaded destination stops");
(p, r) = Create(); r.Start(Destination.Player, 1, 0); p.S = null; r.Tick(1); Check(!r.Running && p.Moves == 0, "closed window stops");
foreach (var field in new[] { "retainer", "character", "window" })
{
    (p, r) = Create(); r.Start(Destination.Player, 1, 0);
    p.S = field switch { "retainer" => p.S! with { RetainerId = 2 }, "character" => p.S! with { CharacterId = 2 }, _ => p.S! with { Window = 2 } };
    r.Tick(1); Check(!r.Running && p.Moves == 0, field + " change stops");
}
(p, r) = Create(); r.Start(Destination.Player, 1, 0); p.S = p.S! with { Listings = [FakePort.B] }; r.Tick(1); Check(!r.Running && p.Moves == 0, "external list changes stop");
(p, r) = Create(); r.Start(Destination.Player, 1, 0); r.Tick(1); r.Tick(1.3); p.S = p.S! with { Listings = [FakePort.A] }; r.Tick(2); Check(!r.Running && p.Moves == 1, "unexpected change in flight stops");
(p, r) = Create(); r.Start(Destination.Player, 1, 0); p.IsBusy = true; r.Tick(1); Check(!r.Running && p.Moves == 0, "other menu stops");
(p, r) = Create(); p.IsBusy = true; r.Start(Destination.Player, 1, 0); Check(!r.Running, "busy start rejected");
(p, r) = Create(); p.S = p.S! with { Listings = [] }; r.Start(Destination.Player, 1, 0); Check(!r.Running, "empty list rejected");
(p, r) = Create(); r.Start(Destination.Player, 1, 0); r.Stop(); r.Tick(2); Check(p.Moves == 0, "manual cancellation");
(p, r) = Create(); r.Start(Destination.Player, 1, 0); r.Start(Destination.Retainer, 1, 0); r.Tick(1); r.Tick(1.3); Check(p.Target == Destination.Player && p.Moves == 1, "duplicate start ignored");
(p, r) = Create(); r.Start(Destination.Player, 1, 0); r.Tick(1); r.Tick(1.3); r.Stop(); r.Start(Destination.Player, 1, 2); r.Tick(3); Check(!r.Running && p.Moves == 1, "cancelled in-flight operation prevents immediate restart");
r.Start(Destination.Player, 1, 17); Check(r.Running, "restart allowed after prior deadline");
(p, r) = Create(); r.Start(Destination.Player, 1, 0); r.Tick(1); Check(p.Opens == 1 && p.Moves == 0, "recall opens menu before selection");
p.MenuReady = false; r.Tick(2); r.Tick(6); Check(!r.Running && p.Moves == 0 && p.Opens == 1, "missing recall menu times out without action");
(p, r) = Create(); r.Start(Destination.Player, 1, 0); r.Tick(1); p.Space = false; r.Tick(1.3); Check(!r.Running && p.Moves == 0, "recheck destination before menu selection");
(p, r) = Create(); r.Start(Destination.Player, 1, 0); r.Tick(1); p.S = p.S! with { Listings = [FakePort.B] }; r.Tick(1.3); Check(!r.Running && p.Moves == 0, "source mutation before menu selection stops");
ListingTests.Run(Check);
LocalizationTests.Run(Check);
Console.WriteLine($"{passed} checks passed");

sealed class FakePort : IRecallPort
{
    public static readonly Listing A = new(0, 100, 3, 0), B = new(1, 101, 4, 1);
    public Snapshot? S = new(1, 1, 1, [A, B]);
    public bool IsBusy { get; set; }
    public bool Space = true;
    public long CountValue;
    public int Moves;
    public int Opens;
    public bool MenuReady = true;
    public Listing? Last;
    public Destination Target;
    public Snapshot? Read() => S;
    public bool HasSpace(Destination target) => Space;
    public long Count(Destination target, Listing listing) => CountValue;
    public void OpenRecallMenu(Listing listing) { Opens++; }
    public bool SelectRecall(Destination target, Listing listing) { if (!MenuReady) return false; Moves++; Last = listing; Target = target; return true; }
    public void ResetMenu() { }
}
