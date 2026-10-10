using Playdex.Core;
using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace Playdex.Shell;

internal enum InputCode : byte
{
    Up, Down, Left, Right, Confirm, Back, X, Y, Lb, Rb, View, Menu, Guide,
}

/// <summary>
/// Reads XInput on a dedicated thread and turns it into navigation events. Allocation-free per event: events go
/// through a fixed ring (<see cref="InputQueue"/>) and the UI thread drains it.
/// </summary>
/// <remarks>
/// Rates: 250 Hz while the launcher is in front and a pad is connected (see <see cref="DefaultPollMs"/>); 20 Hz Guide-only while a game is in front;
/// disconnected slots are probed once a second (XInputGetState on an empty slot is slow).
/// All connected pads are merged into one state, which also drops the duplicate presses that Steam Input's
/// virtual pad mirrors from the physical one.
/// </remarks>
internal sealed unsafe class ControllerInput
{
    private const ushort DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008;
    private const ushort StartButton = 0x0010, BackButton = 0x0020, LeftShoulder = 0x0100, RightShoulder = 0x0200;
    private const ushort GuideButton = 0x0400, A = 0x1000, B = 0x2000, XButton = 0x4000, YButton = 0x8000;
    private const int StickEngage = 16000, StickRelease = 11000;
    private const int MaxPads = 4;

    /// <summary>
    /// 250 Hz. Measured host CPU with all four slots polled (no pad, the worst case): 8.3 % of one core at 1 kHz,
    /// 4.9 % at 500 Hz, 3.4 % at 250 Hz, 1.4 % at 125 Hz. 250 Hz adds at most 4 ms to a press, which keeps the
    /// measured p99 (3.1 ms) inside one frame at 120 Hz (8.3 ms); 125 Hz would not. Event-driven reads aren't an
    /// option: Windows.Gaming.Input has no reading events and neither it nor GameInput reports the Guide button.
    /// </summary>
    private const int DefaultPollMs = 4;

    private static readonly (ushort Mask, InputCode Code)[] _buttons =
    [
        (A, InputCode.Confirm), (B, InputCode.Back), (XButton, InputCode.X), (YButton, InputCode.Y),
        (LeftShoulder, InputCode.Lb), (RightShoulder, InputCode.Rb), (BackButton, InputCode.View), (StartButton, InputCode.Menu),
        (GuideButton, InputCode.Guide),
    ];

    private static readonly long _repeatDelay = Ticks(400);
    private static readonly long _repeatStart = Ticks(120);
    private static readonly long _repeatMin = Ticks(60);
    private static readonly long _probeInterval = Ticks(1000);

    private readonly delegate* unmanaged[Stdcall]<uint, Native.XInputState*, uint> _getState;
    private readonly InputQueue _queue;

    // Diagnostic: poll every slot at the foreground rate as if a pad were connected, to measure the polling cost
    // on a machine without a controller (an empty slot is the slower case, so this is an upper bound).
    private readonly bool _forcePoll = Environment.GetEnvironmentVariable("PLAYDEX_INPUT_FORCE_POLL") == "1";

    // Foreground poll interval. PLAYDEX_INPUT_HZ overrides it for measurements.
    private readonly long _pollMs = int.TryParse(Environment.GetEnvironmentVariable("PLAYDEX_INPUT_HZ"), out var hz) && hz is > 0 and <= 1000
        ? 1000 / hz
        : DefaultPollMs;
    private volatile bool _foreground = true;
    private volatile int _benchRemaining;
    private long _benchInterval;

    private int _connected;
    private long _nextProbe;
    private ushort _prevButtons;
    private InputCode? _heldDir;
    private long _nextRepeat;
    private long _repeatGap;
    private bool _stickEngaged;

    public ControllerInput(InputQueue queue)
    {
        _queue = queue;
        var lib = Native.LoadLibrary("xinput1_4.dll");
        // XInputGetStateEx (ordinal 100) is the undocumented variant that also reports the Guide button.
        var proc = lib == 0 ? 0 : Native.GetProcAddress(lib, 100);
        GuideSupported = proc != 0;
        if (proc == 0 && lib != 0)
        {
            proc = Native.GetProcAddress(lib, "XInputGetState");
        }
        _getState = (delegate* unmanaged[Stdcall]<uint, Native.XInputState*, uint>)proc;
    }

    public bool GuideSupported { get; }

    public int ConnectedPads => int.PopCount(Volatile.Read(ref _connected));

    /// <summary>False while a game is in front: only the Guide button is read, at a low rate.</summary>
    public bool Foreground
    {
        set => _foreground = value;
    }

    public void Start()
    {
        if (_getState == null)
        {
            Log.Info("XInput not available; controller input disabled");
            return;
        }
        new Thread(Run) { IsBackground = true, Name = "xinput", Priority = ThreadPriority.AboveNormal }.Start();
    }

    /// <summary>Inject alternating left/right presses through the real pipeline, to measure latency without a pad.</summary>
    public void StartBench(int count, int intervalMs)
    {
        _benchInterval = Ticks(intervalMs);
        _benchRemaining = count;
    }

    private void Run()
    {
        var timer = Native.CreateWaitableTimerEx(0, 0, Native.CreateWaitableTimerHighResolution, Native.TimerAllAccess);
        Native.XInputState state;
        long nextBench = 0;
        var benchLeft = false;

        while (true)
        {
            var now = Stopwatch.GetTimestamp();
            var probe = now >= _nextProbe;
            if (probe)
            {
                _nextProbe = now + _probeInterval;
            }

            ushort buttons = 0;
            short lx = 0, ly = 0;
            var connected = _connected;
            for (uint slot = 0; slot < MaxPads; slot++)
            {
                var bit = 1 << (int)slot;
                if ((connected & bit) == 0 && !probe && !_forcePoll)
                {
                    continue;
                }
                if (_getState(slot, &state) == Native.ErrorSuccess)
                {
                    connected |= bit;
                    buttons |= state.Gamepad.Buttons;
                    if (Math.Abs(state.Gamepad.ThumbLX) + Math.Abs(state.Gamepad.ThumbLY) > Math.Abs(lx) + Math.Abs(ly))
                    {
                        lx = state.Gamepad.ThumbLX;
                        ly = state.Gamepad.ThumbLY;
                    }
                }
                else
                {
                    connected &= ~bit;
                }
            }
            Volatile.Write(ref _connected, connected);

            if (connected != 0)
            {
                Process(buttons, lx, ly, now);
            }

            if (_benchRemaining > 0 && now >= nextBench)
            {
                nextBench = now + _benchInterval;
                _benchRemaining--;
                benchLeft = !benchLeft;
                _queue.Push(benchLeft ? InputCode.Left : InputCode.Right, now);
            }

            long sleepMs = _benchRemaining > 0 ? 1
                : connected == 0 && !_forcePoll ? 1000
                : _foreground ? _pollMs
                : 50;
            var due = -sleepMs * 10_000; // relative, 100 ns units
            if (timer != 0 && Native.SetWaitableTimer(timer, &due, 0, 0, 0, false))
            {
                _ = Native.WaitForSingleObject(timer, Native.Infinite);
            }
            else
            {
                Thread.Sleep((int)sleepMs);
            }
        }
    }

    private void Process(ushort buttons, short lx, short ly, long now)
    {
        var pressed = (ushort)(buttons & ~_prevButtons);
        _prevButtons = buttons;

        if (!_foreground)
        {
            _heldDir = null;
            if ((pressed & GuideButton) != 0)
            {
                _queue.Push(InputCode.Guide, now);
            }
            return;
        }

        var dir = DirectionOf(buttons, lx, ly);
        if (dir != _heldDir)
        {
            _heldDir = dir;
            if (dir is { } d)
            {
                _queue.Push(d, now);
                _nextRepeat = now + _repeatDelay;
                _repeatGap = _repeatStart;
            }
        }
        else if (dir is { } held && now >= _nextRepeat)
        {
            _queue.Push(held, now);
            // Held directions accelerate the longer they are held.
            _repeatGap = Math.Max(_repeatMin, _repeatGap * 88 / 100);
            _nextRepeat = now + _repeatGap;
        }

        if (pressed == 0)
        {
            return;
        }
        foreach (var (mask, code) in _buttons)
        {
            if ((pressed & mask) != 0)
            {
                _queue.Push(code, now);
            }
        }
    }

    private InputCode? DirectionOf(ushort buttons, short lx, short ly)
    {
        if ((buttons & DpadUp) != 0) return InputCode.Up;
        if ((buttons & DpadDown) != 0) return InputCode.Down;
        if ((buttons & DpadLeft) != 0) return InputCode.Left;
        if ((buttons & DpadRight) != 0) return InputCode.Right;

        int ax = Math.Abs((int)lx), ay = Math.Abs((int)ly);
        var magnitude = Math.Max(ax, ay);
        _stickEngaged = magnitude > (_stickEngaged ? StickRelease : StickEngage);
        if (!_stickEngaged) return null;
        return ax > ay ? (lx > 0 ? InputCode.Right : InputCode.Left) : (ly > 0 ? InputCode.Up : InputCode.Down);
    }

    private static long Ticks(int ms) => Stopwatch.Frequency * ms / 1000;
}

/// <summary>
/// Single-producer (input thread) / single-consumer (UI thread) ring. The producer schedules one drain on the UI
/// dispatcher with a cached handler, so pushing an event allocates nothing.
/// </summary>
internal sealed class InputQueue
{
    private const int Size = 256;
    private readonly InputCode[] _codes = new InputCode[Size];
    private readonly long[] _timestamps = new long[Size];
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueHandler _drain;
    private readonly Action<InputCode, long> _deliver;
    private int _head;
    private int _tail;
    private int _scheduled;

    public InputQueue(DispatcherQueue dispatcher, Action<InputCode, long> deliver)
    {
        _dispatcher = dispatcher;
        _deliver = deliver;
        _drain = Drain;
    }

    public void Push(InputCode code, long timestamp)
    {
        var head = _head;
        if (head - Volatile.Read(ref _tail) >= Size)
        {
            return; // UI thread is stalled; dropping input beats queueing it behind the stall.
        }
        _codes[head & (Size - 1)] = code;
        _timestamps[head & (Size - 1)] = timestamp;
        Volatile.Write(ref _head, head + 1);
        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
        {
            _dispatcher.TryEnqueue(DispatcherQueuePriority.High, _drain);
        }
    }

    private void Drain()
    {
        Volatile.Write(ref _scheduled, 0);
        var head = Volatile.Read(ref _head);
        while (_tail != head)
        {
            var i = _tail & (Size - 1);
            _deliver(_codes[i], _timestamps[i]);
            Volatile.Write(ref _tail, _tail + 1);
        }
    }
}
