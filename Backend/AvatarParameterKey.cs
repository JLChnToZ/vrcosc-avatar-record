using System;

internal readonly struct AvatarParameterKey : IEquatable<AvatarParameterKey> {
    public readonly Guid avatarId;
    public readonly string parameterName;

    public AvatarParameterKey(Guid avatarId, string parameterName) {
        this.avatarId = avatarId;
        this.parameterName = parameterName;
    }

    public bool Equals(AvatarParameterKey other) =>
        avatarId.Equals(other.avatarId) &&
        string.Equals(parameterName, other.parameterName, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is AvatarParameterKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(avatarId, parameterName);
}