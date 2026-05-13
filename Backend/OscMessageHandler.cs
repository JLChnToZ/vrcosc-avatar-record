using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OscCore;

internal static class OscMessageHandler {
    private static readonly HashSet<string> blacklistedParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "IsLocal", "PreviewMode", "Viseme", "Voice", "GestureLeft", "GestureRight", "GestureLeftWeight", "GestureRightWeight",
        "AngularY", "VelocityX", "VelocityY", "VelocityZ", "VelocityMagnitude", "Upright", "Grounded", "Seated", "AFK",
        "TrackingType", "VRMode", "MuteSelf", "InStation", "Earmuffs", "IsOnFriendsList", "AvatarVersion", "IsAnimatorEnabled",
        "ScaleModified", "ScaleFactor", "ScaleFactorInverse", "EyeHeightAsMeters", "EyeHeightAsPercent",
    };

    public static async Task HandleAsync(OscMessage message, OscRuntimeSession session, CancellationToken cancellationToken) {
        string address = message.Address;
        int argumentCount = message.Count;

        if (string.Equals(address, "/avatar/change", StringComparison.Ordinal)) {
            if (argumentCount == 0 ||
                !(message[0] is string avatarIdStr) ||
                string.IsNullOrWhiteSpace(avatarIdStr) ||
                !avatarIdStr.StartsWith("avtr_", StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParse(avatarIdStr[5..], out var avatarId)) {
                Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] Ignored /avatar/change with invalid payload");
                return;
            }

            session.SetAvatar(avatarId);
            Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] Active avatar set to {avatarId}");
            await session.SendEnabledParametersForAvatarAsync(avatarId);
            return;
        }

        if (!TryParseParameterAddress(address, out string parameterName) || blacklistedParameters.Contains(parameterName))
            return;

        if (argumentCount == 0) {
            Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] Ignored parameter {parameterName}: missing value");
            return;
        }

        if (!session.TryGetActiveAvatarId(out var activeAvatarId)) {
            session.NotifyParameterDroppedBeforeAvatar(parameterName);
            return;
        }

        object parameterRawValue = message[0];
        if (!Primitive32.TryCreate(parameterRawValue, out var value)) {
            string typeName = parameterRawValue == null ? "null" : parameterRawValue.GetType().Name;
            Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] Ignored parameter {parameterName}: unsupported value type {typeName}");
            return;
        }

        session.QueueParameterUpsert(new AvatarParameterKey(activeAvatarId, parameterName), value, DateTimeOffset.UtcNow);
    }

    private static bool TryParseParameterAddress(string address, out string parameterName) {
        const string singularPrefix = "/avatar/parameter/";
        const string pluralPrefix = "/avatar/parameters/";

        if (address.StartsWith(singularPrefix, StringComparison.Ordinal)) {
            parameterName = address[singularPrefix.Length..];
            return !string.IsNullOrWhiteSpace(parameterName);
        }

        if (address.StartsWith(pluralPrefix, StringComparison.Ordinal)) {
            parameterName = address[pluralPrefix.Length..];
            return !string.IsNullOrWhiteSpace(parameterName);
        }

        parameterName = string.Empty;
        return false;
    }
}
