using System;
using System.Collections.Generic;

namespace WhereIsMyHead.Runtime;

internal sealed class PluginLogBuffer
{
    private readonly object gate = new();
    private readonly Queue<string> lines = new();

    public void Push(string line)
    {
        lock (this.gate)
        {
            this.lines.Enqueue($"{DateTime.Now:HH:mm:ss} {line}");
            while (this.lines.Count > 1000)
                this.lines.Dequeue();
        }
    }

    public string[] Snapshot()
    {
        lock (this.gate)
            return [.. this.lines];
    }
}

