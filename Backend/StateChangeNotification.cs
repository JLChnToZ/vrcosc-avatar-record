using System;

/// <summary>
/// Represents a single state change notification.
/// </summary>
internal readonly struct StateChangeNotification {
    public readonly StateChangeType changeType;
    public readonly Guid avatarId;
    public readonly string? parameterName;
    public readonly Primitive32 newValue;
    public readonly bool syncEnabled;

    public AvatarParameterKey GetParameterKey() => new AvatarParameterKey(avatarId, parameterName ?? string.Empty);

    public AvatarParameterStateEntry ToStateEntry(DateTimeOffset updatedAtUtc) =>
        new AvatarParameterStateEntry(avatarId, parameterName, newValue, syncEnabled, updatedAtUtc);

    /// <summary>
    /// Creates a parameter change notification.
    /// </summary>
    public StateChangeNotification(AvatarParameterKey key, Primitive32 newValue, bool syncEnabled) {
        changeType = StateChangeType.ParameterChanged;
        this.avatarId = key.avatarId;
        this.parameterName = key.parameterName;
        this.newValue = newValue;
        this.syncEnabled = syncEnabled;
    }

    /// <summary>
    /// Creates a parameter removal or avatar removal notification.
    /// </summary>
    private StateChangeNotification(StateChangeType changeType, Guid avatarId, string? parameterName) {
        this.changeType = changeType;
        this.avatarId = avatarId;
        this.parameterName = parameterName;
        newValue = Primitive32.Null;
        syncEnabled = false;
    }

    /// <summary>
    /// Creates a parameter removal notification.
    /// </summary>
    public static StateChangeNotification ParameterRemoved(Guid avatarId, string parameterName) =>
        new StateChangeNotification(StateChangeType.ParameterRemoved, avatarId, parameterName);

    /// <summary>
    /// Creates an avatar removal notification.
    /// </summary>
    public static StateChangeNotification AvatarRemoved(Guid avatarId) =>
        new StateChangeNotification(StateChangeType.AvatarRemoved, avatarId, null);
}
