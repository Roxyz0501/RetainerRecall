namespace RetainerRecall;

public enum Destination { Player, Retainer }
public sealed record Listing(int Slot, uint ItemId, int Quantity, byte Flags);
public sealed record Snapshot(ulong RetainerId, ulong CharacterId, nint Window, Listing[] Listings);
public interface IRecallPort
{
    Snapshot? Read();
    bool IsBusy { get; }
    bool HasSpace(Destination destination);
    long Count(Destination destination, Listing listing);
    void OpenRecallMenu(Listing listing);
    bool SelectRecall(Destination destination, Listing listing);
    void ResetMenu();
}

// One in-flight operation, no retries; both source and destination must acknowledge it.
public sealed class RecallRun(IRecallPort port)
{
    public bool Running { get; private set; }
    public Text Status { get; private set; } = L.M("Idle");
    public int Completed { get; private set; }
    private Snapshot? expected;
    private Listing? pending;
    private Destination destination;
    private long beforeCount;
    private bool waitingMenu;
    private double nextAt, deadline, delay, restartAfter;

    public void Start(Destination target, double seconds, double now)
    {
        if (Running) return;
        if (now < restartAfter) { Status = L.M("RecallPending"); return; }
        var snapshot = port.Read();
        if (snapshot == null || port.IsBusy) { Stop(L.M("OpenSaleList")); return; }
        if (snapshot.Listings.Length == 0) { Stop(L.M("NoListings")); return; }
        expected = snapshot;
        destination = target;
        delay = double.IsFinite(seconds) ? Math.Clamp(seconds, 0.1, 30) : 1.5;
        pending = null;
        waitingMenu = false;
        Completed = 0;
        nextAt = now + delay;
        Running = true;
        Status = L.M("RecallStart");
    }

    public void Stop(Text? reason = null)
    {
        if (pending != null) restartAfter = Math.Max(restartAfter, deadline);
        Running = false;
        pending = null;
        expected = null;
        waitingMenu = false;
        port.ResetMenu();
        Status = reason ?? L.M("Stopped");
    }

    public void Tick(double now)
    {
        if (!Running || expected == null) return;
        var current = port.Read();
        if (current == null || current.RetainerId != expected.RetainerId || current.CharacterId != expected.CharacterId || current.Window != expected.Window)
        { Stop(L.M("RecallSessionChanged")); return; }
        if (port.IsBusy) { Stop(L.M("RecallInterference")); return; }
        if (pending != null)
        {
            if (waitingMenu)
            {
                if (!current.Listings.SequenceEqual(expected.Listings)) { Stop(L.M("MenuStockChanged")); return; }
                if (now >= deadline) { Stop(L.M("RecallMenuTimeout")); return; }
                if (now < nextAt) return;
                if (!port.HasSpace(destination)) { Stop(L.M("SpaceLost")); return; }
                if (port.Count(destination, pending) != beforeCount) { Stop(L.M("DestinationChanged")); return; }
                if (port.SelectRecall(destination, pending)) { waitingMenu = false; deadline = now + 15; }
                return;
            }
            var remaining = expected.Listings.Where(x => x != pending).ToArray();
            if (current.Listings.SequenceEqual(remaining) && port.Count(destination, pending) == beforeCount + pending.Quantity)
            {
                Completed++;
                expected = current;
                pending = null;
                port.ResetMenu();
                nextAt = now + delay;
                Status = L.M("RecallProgress", Completed, remaining.Length);
                if (remaining.Length == 0) Stop(L.M("RecallDone", Completed));
                return;
            }
            if (!current.Listings.SequenceEqual(expected.Listings) && !current.Listings.SequenceEqual(remaining))
            { Stop(L.M("RecallUnexpected")); return; }
            if (now >= deadline) Stop(L.M("RecallTimeout"));
            return;
        }
        if (!current.Listings.SequenceEqual(expected.Listings)) { Stop(L.M("RecallStockChanged")); return; }
        if (now < nextAt) return;
        if (!port.HasSpace(destination)) { Stop(L.M("NoDestination")); return; }
        pending = current.Listings[0];
        beforeCount = port.Count(destination, pending);
        if (beforeCount < 0) { Stop(L.M("DestinationUnreadable")); return; }
        deadline = now + 5;
        nextAt = now + 0.2;
        waitingMenu = true;
        Status = L.M("RecallMenuProgress", Completed);
        port.OpenRecallMenu(pending);
    }
}
