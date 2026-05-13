using System;
using System.Collections.Generic;

/// <summary>
/// Event arguments for batched state changes.
/// </summary>
internal sealed class StateChangeBatchEventArgs : EventArgs {
    public readonly IReadOnlyList<StateChangeNotification> changes;

    public StateChangeBatchEventArgs(IReadOnlyList<StateChangeNotification> changes) {
        this.changes = changes;
    }
}
