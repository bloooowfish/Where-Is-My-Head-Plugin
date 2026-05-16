using WhereIsMyHead.Runtime;

namespace WhereIsMyHead.Hooking;

internal sealed class World3DObservation
{
    public const uint ArmFrameThreshold = 120;

    private uint observedFrames;
    private uint uniqueThreadCount;
    private uint uniqueManagerCount;
    private uint firstThreadId;
    private nuint firstManager;
    private bool reentrancyDetected;

    public DiagnosticCode DiagnosticCode { get; private set; }

    public void Record(uint threadId, nuint manager, bool reentered)
    {
        this.observedFrames++;

        if (threadId != 0)
        {
            if (this.uniqueThreadCount == 0)
            {
                this.firstThreadId = threadId;
                this.uniqueThreadCount = 1;
            }
            else if (this.firstThreadId != threadId)
            {
                this.uniqueThreadCount = 2;
            }
        }

        if (manager != 0)
        {
            if (this.uniqueManagerCount == 0)
            {
                this.firstManager = manager;
                this.uniqueManagerCount = 1;
            }
            else if (this.firstManager != manager)
            {
                this.uniqueManagerCount = 2;
            }
        }

        this.reentrancyDetected |= reentered;
    }

    public void Reset()
    {
        this.observedFrames = 0;
        this.uniqueThreadCount = 0;
        this.uniqueManagerCount = 0;
        this.firstThreadId = 0;
        this.firstManager = 0;
        this.reentrancyDetected = false;
        this.DiagnosticCode = DiagnosticCode.None;
    }

    public InstallState DecideInstallState()
    {
        if (this.reentrancyDetected)
        {
            this.DiagnosticCode = DiagnosticCode.ReentrancyDetected;
            return InstallState.Quarantined;
        }

        if (this.uniqueThreadCount != 1)
        {
            this.DiagnosticCode = DiagnosticCode.ThreadInstability;
            return InstallState.Quarantined;
        }

        if (this.uniqueManagerCount != 1)
        {
            this.DiagnosticCode = DiagnosticCode.ManagerInstability;
            return InstallState.Quarantined;
        }

        this.DiagnosticCode = DiagnosticCode.None;
        return this.observedFrames >= ArmFrameThreshold ? InstallState.Armed : InstallState.Resolved;
    }
}

