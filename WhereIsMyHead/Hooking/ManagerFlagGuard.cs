using System;

namespace WhereIsMyHead.Hooking;

internal sealed unsafe class ManagerFlagGuard : IDisposable
{
    private static readonly object Gate = new();
    private static Session activeSession;

    private readonly byte* flag;
    private readonly byte previous;
    private readonly byte armed;
    private readonly bool active;
    private readonly bool suspended;

    private readonly record struct Session(
        nint Manager,
        nint Flag,
        byte Previous,
        byte Armed,
        int ThreadId,
        bool Active);

    public ManagerFlagGuard(nint manager, bool arm)
    {
        if (manager == 0)
            return;

        this.flag = (byte*)manager + World3DConstants.ClearOnlyFlagOffset;
        this.armed = 1;
        var threadId = unchecked((int)NativeMethods.GetCurrentThreadId());

        lock (Gate)
        {
            if (arm)
            {
                this.previous = *this.flag;
                *this.flag = this.armed;
                this.active = true;
                activeSession = new(manager, (nint)this.flag, this.previous, this.armed, threadId, true);
                return;
            }

            if (activeSession.Active &&
                activeSession.Manager == manager &&
                activeSession.Flag == (nint)this.flag &&
                activeSession.ThreadId == threadId &&
                *this.flag == activeSession.Armed)
            {
                this.previous = activeSession.Previous;
                *this.flag = this.previous;
                this.suspended = true;
            }
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (this.suspended && this.flag != null)
            {
                if (activeSession.Active &&
                    activeSession.Flag == (nint)this.flag &&
                    activeSession.ThreadId == unchecked((int)NativeMethods.GetCurrentThreadId()) &&
                    *this.flag == this.previous)
                {
                    *this.flag = this.armed;
                }

                return;
            }

            if (!this.active || this.flag == null)
                return;

            if (*this.flag == this.armed)
                *this.flag = this.previous;

            if (activeSession.Active && activeSession.Flag == (nint)this.flag)
                activeSession = default;
        }
    }
}
