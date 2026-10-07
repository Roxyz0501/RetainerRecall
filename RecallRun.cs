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
    void Move(Destination destination, Listing listing);
}

// One in-flight operation, no retries; both source and destination must acknowledge it.
public sealed class RecallRun(IRecallPort port)
{
    public bool Running { get; private set; }
    public string Status { get; private set; } = "待機中";
    public int Completed { get; private set; }
    private Snapshot? expected;
    private Listing? pending;
    private Destination destination;
    private long beforeCount;
    private double nextAt, deadline, delay, restartAfter;

    public void Start(Destination target, double seconds, double now)
    {
        if (Running) return;
        if (now < restartAfter) { Status = "前回の回収処理の終了を待っています。しばらくしてから再実行してください"; return; }
        var snapshot = port.Read();
        if (snapshot == null || port.IsBusy) { Stop("販売リストを開き、他の操作を終了してください"); return; }
        if (snapshot.Listings.Length == 0) { Stop("出品中のアイテムはありません"); return; }
        expected = snapshot;
        destination = target;
        delay = double.IsFinite(seconds) ? Math.Clamp(seconds, 0.5, 30) : 1.5;
        pending = null;
        Completed = 0;
        nextAt = now + delay;
        Running = true;
        Status = "回収開始を待機中";
    }

    public void Stop(string reason = "停止しました")
    {
        if (pending != null) restartAfter = Math.Max(restartAfter, deadline);
        Running = false;
        pending = null;
        expected = null;
        Status = reason;
    }

    public void Tick(double now)
    {
        if (!Running || expected == null) return;
        var current = port.Read();
        if (current == null || current.RetainerId != expected.RetainerId || current.CharacterId != expected.CharacterId || current.Window != expected.Window)
        { Stop("画面・キャラクター・リテイナーが変わったため停止しました"); return; }
        if (port.IsBusy) { Stop("他のメニュー操作を検出したため停止しました"); return; }
        if (pending != null)
        {
            var remaining = expected.Listings.Where(x => x != pending).ToArray();
            if (current.Listings.SequenceEqual(remaining) && port.Count(destination, pending) == beforeCount + pending.Quantity)
            {
                Completed++;
                expected = current;
                pending = null;
                nextAt = now + delay;
                Status = $"{Completed}件回収済み / 残り{remaining.Length}件";
                if (remaining.Length == 0) Stop($"完了: {Completed}件を回収しました");
                return;
            }
            if (!current.Listings.SequenceEqual(expected.Listings) && !current.Listings.SequenceEqual(remaining))
            { Stop("出品内容の変更を検出したため停止しました"); return; }
            if (now >= deadline) Stop("回収結果を確認できず停止しました。自動再試行はしません");
            return;
        }
        if (!current.Listings.SequenceEqual(expected.Listings)) { Stop("出品内容が変更されたため停止しました"); return; }
        if (now < nextAt) return;
        if (!port.HasSpace(destination)) { Stop("移動先に空き枠がないか、所持品を読み込めないため停止しました"); return; }
        pending = current.Listings[0];
        beforeCount = port.Count(destination, pending);
        if (beforeCount < 0) { Stop("移動先の所持品を読み込めません"); return; }
        deadline = now + 15;
        Status = $"回収結果を確認中（{Completed}件完了）";
        port.Move(destination, pending);
    }
}
