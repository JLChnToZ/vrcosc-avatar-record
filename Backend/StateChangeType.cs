
/// <summary>
/// Represents the type of state change notification.
/// </summary>
internal enum StateChangeType : byte {
    /// <summary>Parameter value or sync state changed.</summary>
    ParameterChanged,
    /// <summary>Parameter was removed.</summary>
    ParameterRemoved,
    /// <summary>Avatar and all its parameters were removed.</summary>
    AvatarRemoved,
}
