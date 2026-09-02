namespace Index.Capture;

public enum CaptureSessionState
{
    Idle,
    Capturing,
    HighResolutionTransition,
    Shutdown
}

/// <summary>
/// Serializes capture-session entry and owns the explicit transition between
/// an overlay capture and its high-resolution continuation.
/// </summary>
public sealed class CaptureSessionGate
{
    private readonly object _gate = new();
    private CaptureSessionState _state;

    public CaptureSessionState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool IsShutdown => State == CaptureSessionState.Shutdown;

    public bool IsHighResolutionTransition =>
        State == CaptureSessionState.HighResolutionTransition;

    public bool TryBeginCapture()
    {
        lock (_gate)
        {
            if (_state != CaptureSessionState.Idle)
            {
                return false;
            }

            _state = CaptureSessionState.Capturing;
            return true;
        }
    }

    public bool TryBeginHighResolutionTransition()
    {
        lock (_gate)
        {
            if (_state != CaptureSessionState.Capturing)
            {
                return false;
            }

            _state = CaptureSessionState.HighResolutionTransition;
            return true;
        }
    }

    public bool TryResumeCaptureAfterHighResolutionFailure()
    {
        lock (_gate)
        {
            if (_state != CaptureSessionState.HighResolutionTransition)
            {
                return false;
            }

            _state = CaptureSessionState.Capturing;
            return true;
        }
    }

    public void EndCapture()
    {
        lock (_gate)
        {
            if (_state != CaptureSessionState.Shutdown)
            {
                _state = CaptureSessionState.Idle;
            }
        }
    }

    public void Shutdown()
    {
        lock (_gate)
        {
            _state = CaptureSessionState.Shutdown;
        }
    }
}
