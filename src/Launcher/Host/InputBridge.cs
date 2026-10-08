using Microsoft.Web.WebView2.Core;

namespace Playdex.Launcher.Host;

/// <summary>
/// Posts controller events to the UI and measures input → visible focus latency from the UI's acks.
/// Messages are pre-built per (event, id) so delivery allocates nothing; ids cycle through 0..255.
/// </summary>
internal sealed class InputBridge
{
    private const int Ids = 256;
    private static readonly string[] _json = BuildMessages();

    private readonly long[] _sentAt = new long[Ids];
    private readonly LatencyStats _roundTrip = new("input.focus_latency");
    private readonly LatencyStats _presented = new("input.focus_latency_presented");
    private readonly LatencyStats _dispatch = new("input.dispatch");
    private CoreWebView2? _web;
    private int _nextId;

    public void Attach(CoreWebView2 web) => _web = web;

    /// <summary>UI thread. Returns false when there is no page to deliver to yet.</summary>
    public bool Deliver(InputCode code, long timestamp)
    {
        if (_web is null)
        {
            return false;
        }
        var id = _nextId;
        _nextId = (id + 1) & (Ids - 1);
        _sentAt[id] = timestamp;
        _dispatch.Add(Metrics.MsSince(timestamp));
        _web.PostWebMessageAsJson(_json[(int)code * Ids + id]);
        return true;
    }

    /// <summary>
    /// The UI echoes an event's id from the first animation frame after it changed focus (the budget's measure),
    /// then again from the following frame, by which point the focused frame has been presented (an upper bound).
    /// </summary>
    public void Ack(int id, bool presented)
    {
        if ((uint)id >= Ids || _sentAt[id] == 0)
        {
            return;
        }
        if (!presented)
        {
            _roundTrip.Add(Metrics.MsSince(_sentAt[id]));
            return;
        }
        _presented.Add(Metrics.MsSince(_sentAt[id]));
        _sentAt[id] = 0;
        if (_presented.Count % 200 == 0)
        {
            Log.Info(_roundTrip.Summary());
        }
    }

    public int Completed => _presented.Count;

    public void Flush()
    {
        _dispatch.Flush();
        _roundTrip.Flush();
        _presented.Flush();
    }

    private static string[] BuildMessages()
    {
        var codes = Enum.GetValues<InputCode>();
        var messages = new string[codes.Length * Ids];
        foreach (var code in codes)
        {
            for (var id = 0; id < Ids; id++)
            {
                messages[(int)code * Ids + id] = code switch
                {
                    InputCode.Up => $$"""{"type":"dir","dir":"up","id":{{id}}}""",
                    InputCode.Down => $$"""{"type":"dir","dir":"down","id":{{id}}}""",
                    InputCode.Left => $$"""{"type":"dir","dir":"left","id":{{id}}}""",
                    InputCode.Right => $$"""{"type":"dir","dir":"right","id":{{id}}}""",
                    _ => $$"""{"type":"action","action":"{{ActionName(code)}}","id":{{id}}}""",
                };
            }
        }
        return messages;
    }

    private static string ActionName(InputCode code) => code switch
    {
        InputCode.Confirm => "confirm",
        InputCode.Back => "back",
        InputCode.X => "x",
        InputCode.Y => "y",
        InputCode.Lb => "lb",
        InputCode.Rb => "rb",
        InputCode.View => "view",
        InputCode.Menu => "menu",
        InputCode.Guide => "guide",
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };
}
