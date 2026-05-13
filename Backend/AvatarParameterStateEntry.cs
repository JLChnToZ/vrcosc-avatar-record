using System;
using OscCore;

internal readonly struct AvatarParameterStateEntry {
    public readonly Guid avatarId;
    public readonly string parameterName;
    public readonly Primitive32 value;
    public readonly bool syncEnabled;
    public readonly DateTimeOffset updatedAtUtc;

    public AvatarParameterStateEntry(Guid avatarId, string? parameterName, Primitive32 value, bool syncEnabled, DateTimeOffset updatedAtUtc) {
        this.avatarId = avatarId;
        this.parameterName = parameterName ?? string.Empty;
        this.value = value;
        this.syncEnabled = syncEnabled;
        this.updatedAtUtc = updatedAtUtc;
    }

    public AvatarParameterStateEntry(AvatarParameterKey key, Primitive32 value, bool syncEnabled, DateTimeOffset updatedAtUtc) {
        this.avatarId = key.avatarId;
        this.parameterName = key.parameterName;
        this.value = value;
        this.syncEnabled = syncEnabled;
        this.updatedAtUtc = updatedAtUtc;
    }

    public AvatarParameterStateEntry(AvatarParameterStateEntry entry, bool syncEnabled) {
        this.avatarId = entry.avatarId;
        this.parameterName = entry.parameterName;
        this.value = entry.value;
        this.syncEnabled = syncEnabled;
        this.updatedAtUtc = entry.updatedAtUtc;
    }

    public AvatarParameterStateEntry(AvatarParameterStateEntry entry, Primitive32 value, DateTimeOffset updatedAtUtc) {
        this.avatarId = entry.avatarId;
        this.parameterName = entry.parameterName;
        this.value = value;
        this.syncEnabled = entry.syncEnabled;
        this.updatedAtUtc = updatedAtUtc;
    }

    public OscMessage GetOscMessage() => new OscMessage($"/avatar/parameters/{parameterName}", value.Unwrap());

    public AvatarParameterKey GetKey() => new AvatarParameterKey(avatarId, parameterName);
}
