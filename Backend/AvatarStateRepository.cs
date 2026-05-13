using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

internal sealed class AvatarStateRepository : IAsyncDisposable {
    private const string schemaSql = @"CREATE TABLE IF NOT EXISTS avatar_parameter_state (
    avatar_id TEXT NOT NULL,
    parameter_name TEXT NOT NULL,
    value_type TEXT NOT NULL,
    value_int_bool INTEGER NULL,
    value_float REAL NULL,
    sync_enabled INTEGER NOT NULL DEFAULT 1,
    updated_at_utc TEXT NOT NULL,
    PRIMARY KEY (avatar_id, parameter_name)
);";
    private const string addSyncEnabledColumnSql = @"ALTER TABLE avatar_parameter_state ADD COLUMN sync_enabled INTEGER NOT NULL DEFAULT 1;";
    private const string upsertSql = @"INSERT INTO avatar_parameter_state (
    avatar_id,
    parameter_name,
    value_type,
    value_int_bool,
    value_float,
    updated_at_utc
) VALUES (
    $avatar_id,
    $parameter_name,
    $value_type,
    $value_int_bool,
    $value_float,
    $updated_at_utc
)
ON CONFLICT(avatar_id, parameter_name)
DO UPDATE SET
    value_type = excluded.value_type,
    value_int_bool = excluded.value_int_bool,
    value_float = excluded.value_float,
    updated_at_utc = excluded.updated_at_utc;";
    private const string upsertSyncStateSql = @"INSERT INTO avatar_parameter_state (
    avatar_id,
    parameter_name,
    value_type,
    value_int_bool,
    value_float,
    sync_enabled,
    updated_at_utc
) VALUES (
    $avatar_id,
    $parameter_name,
    $value_type,
    $value_int_bool,
    $value_float,
    $sync_enabled,
    $updated_at_utc
)
ON CONFLICT(avatar_id, parameter_name)
DO UPDATE SET
    sync_enabled = excluded.sync_enabled;";
    private const string selectByAvatarSql = @"SELECT
    parameter_name,
    value_type,
    value_int_bool,
    value_float,
    sync_enabled,
    updated_at_utc
FROM avatar_parameter_state
WHERE avatar_id = $avatar_id
ORDER BY parameter_name;";
    private const string selectAllSql = @"SELECT
    avatar_id,
    parameter_name,
    value_type,
    value_int_bool,
    value_float,
    sync_enabled,
    updated_at_utc
FROM avatar_parameter_state
ORDER BY avatar_id, parameter_name;";
    private const string updateSyncEnabledSql = @"UPDATE avatar_parameter_state
SET sync_enabled = $sync_enabled
WHERE avatar_id = $avatar_id
  AND parameter_name = $parameter_name;";
    private const string deleteParameterSql = @"DELETE FROM avatar_parameter_state
WHERE avatar_id = $avatar_id
  AND parameter_name = $parameter_name;";
    private const string deleteAvatarSql = @"DELETE FROM avatar_parameter_state
WHERE avatar_id = $avatar_id;";

    private readonly SqliteConnection connection;
    private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

    public AvatarStateRepository(string databasePath) =>
        connection = new SqliteConnection("Data Source=" + databasePath);

    public async Task InitializeAsync(CancellationToken cancellationToken) {
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(schemaSql, cancellationToken);
        await EnsureSyncEnabledColumnAsync(cancellationToken);
    }

    public Task UpsertParameterStateAsync(
        Guid avatarId,
        string parameterName,
        Primitive32 value,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken
    ) => UpsertParameterStatesAsync(
        new[] { new AvatarParameterStateEntry(avatarId, parameterName, value, true, observedAtUtc) },
        cancellationToken
    );

    public async Task UpsertParameterStatesAsync(IReadOnlyList<AvatarParameterStateEntry>? updates, CancellationToken cancellationToken) {
        if (updates == null || updates.Count == 0) return;
        await writeLock.WaitAsync(cancellationToken);
        try {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            for (int index = 0, updateCount = updates.Count; index < updateCount; index++) {
                AvatarParameterStateEntry update = updates[index];
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = upsertSql;
                if (!TryBindStateParameters(command.Parameters, update, includeSyncEnabled: false)) continue;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        } finally {
            writeLock.Release();
        }
    }

    public async Task UpsertSynchronizationStateAsync(AvatarParameterStateEntry state, CancellationToken cancellationToken) {
        await writeLock.WaitAsync(cancellationToken);
        try {
            await using var command = connection.CreateCommand();
            command.CommandText = upsertSyncStateSql;
            if (!TryBindStateParameters(command.Parameters, state, includeSyncEnabled: true)) return;
            await command.ExecuteNonQueryAsync(cancellationToken);
        } finally {
            writeLock.Release();
        }
    }

    public async Task<Dictionary<string, Primitive32>> GetParametersByAvatarIdAsync(Guid avatarId, CancellationToken cancellationToken) {
        var states = await GetStatesByAvatarIdAsync(avatarId, cancellationToken);
        var result = new Dictionary<string, Primitive32>(states.Count, StringComparer.Ordinal);
        for (int index = 0, stateCount = states.Count; index < stateCount; index++) {
            var state = states[index];
            if (state.syncEnabled && state.value.IsValid) result[state.parameterName] = state.value;
        }
        return result;
    }

    public async Task<IReadOnlyList<AvatarParameterStateEntry>> GetStatesByAvatarIdAsync(Guid avatarId, CancellationToken cancellationToken) {
        await writeLock.WaitAsync(cancellationToken);
        try {
            await using var command = connection.CreateCommand();
            command.CommandText = selectByAvatarSql;
            command.Parameters.AddWithValue("$avatar_id", avatarId.ToString());
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            List<AvatarParameterStateEntry> result = new List<AvatarParameterStateEntry>(32);
            while (await reader.ReadAsync(cancellationToken)) {
                string parameterName = reader.GetString(0);
                if (!TryGetParameterValueFromReader(reader, 1, 2, 3, out var value)) continue;
                bool syncEnabled = !reader.IsDBNull(4) && reader.GetInt32(4) != 0;
                var updatedAtUtc = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                result.Add(new AvatarParameterStateEntry(avatarId, parameterName, value, syncEnabled, updatedAtUtc));
            }

            return result;
        } finally {
            writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<AvatarParameterStateEntry>> GetAllStatesAsync(CancellationToken cancellationToken) {
        await writeLock.WaitAsync(cancellationToken);
        try {
            await using var command = connection.CreateCommand();
            command.CommandText = selectAllSql;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            var result = new List<AvatarParameterStateEntry>(64);
            while (await reader.ReadAsync(cancellationToken)) {
                string avatarId = reader.GetString(0);
                string parameterName = reader.GetString(1);
                if (!TryGetParameterValueFromReader(reader, 2, 3, 4, out var value)) continue;
                bool syncEnabled = !reader.IsDBNull(5) && reader.GetInt32(5) != 0;
                var updatedAtUtc = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                result.Add(new AvatarParameterStateEntry(Guid.Parse(avatarId), parameterName, value, syncEnabled, updatedAtUtc));
            }
            return result;
        } finally {
            writeLock.Release();
        }
    }

    public async Task SetSynchronizationEnabledAsync(Guid avatarId, string parameterName, bool enabled, CancellationToken cancellationToken) {
        await writeLock.WaitAsync(cancellationToken);
        try {
            await using var command = connection.CreateCommand();
            command.CommandText = updateSyncEnabledSql;
            var parameters = command.Parameters;
            parameters.AddWithValue("$avatar_id", avatarId.ToString());
            parameters.AddWithValue("$parameter_name", parameterName);
            parameters.AddWithValue("$sync_enabled", enabled ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
        } finally {
            writeLock.Release();
        }
    }

    public async Task DeleteParameterStateAsync(Guid avatarId, string parameterName, CancellationToken cancellationToken) {
        await writeLock.WaitAsync(cancellationToken);
        try {
            await using var command = connection.CreateCommand();
            command.CommandText = deleteParameterSql;
            var parameters = command.Parameters;
            parameters.AddWithValue("$avatar_id", avatarId.ToString());
            parameters.AddWithValue("$parameter_name", parameterName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        } finally {
            writeLock.Release();
        }
    }

    public async Task DeleteAvatarStatesAsync(Guid avatarId, CancellationToken cancellationToken) {
        await writeLock.WaitAsync(cancellationToken);
        try {
            await using var command = connection.CreateCommand();
            command.CommandText = deleteAvatarSql;
            command.Parameters.AddWithValue("$avatar_id", avatarId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        } finally {
            writeLock.Release();
        }
    }

    private async Task ExecuteNonQueryAsync(string commandText, CancellationToken cancellationToken) {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureSyncEnabledColumnAsync(CancellationToken cancellationToken) {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(avatar_parameter_state);";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        bool hasColumn = false;
        while (await reader.ReadAsync(cancellationToken))
            if (string.Equals(reader.GetString(1), "sync_enabled", StringComparison.Ordinal)) {
                hasColumn = true;
                break;
            }
        if (!hasColumn) await ExecuteNonQueryAsync(addSyncEnabledColumnSql, cancellationToken);
    }

        private static bool TryBindStateParameters(SqliteParameterCollection parameters, AvatarParameterStateEntry state, bool includeSyncEnabled) {
            string valueType;
            object intOrBoolDbValue;
            object floatDbValue;
            switch (state.value.GetTypeCode()) {
                case TypeCode.Boolean:
                    valueType = "bool";
                    intOrBoolDbValue = state.value.Unwrap() ?? DBNull.Value;
                    floatDbValue = DBNull.Value;
                    break;
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                    valueType = "int";
                    intOrBoolDbValue = state.value.Unwrap() ?? DBNull.Value;
                    floatDbValue = DBNull.Value;
                    break;
                case TypeCode.Single:
                    valueType = "float";
                    intOrBoolDbValue = DBNull.Value;
                    floatDbValue = state.value.Unwrap() ?? DBNull.Value;
                    break;
                default:
                    return false;
            }

            parameters.AddWithValue("$avatar_id", state.avatarId);
            parameters.AddWithValue("$parameter_name", state.parameterName);
            parameters.AddWithValue("$value_type", valueType);
            parameters.AddWithValue("$value_int_bool", intOrBoolDbValue);
            parameters.AddWithValue("$value_float", floatDbValue);
            if (includeSyncEnabled) {
                parameters.AddWithValue("$sync_enabled", state.syncEnabled ? 1 : 0);
            }
            parameters.AddWithValue("$updated_at_utc", state.updatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            return true;
        }

    private static bool TryGetParameterValueFromReader(SqliteDataReader reader, int typeIndex, int intOrBoolIndex, int floatIndex, out Primitive32 value) {
        string valueType = reader.GetString(typeIndex);
        value = default;
        if (string.Equals(valueType, "bool", StringComparison.Ordinal)) {
            if (reader.IsDBNull(intOrBoolIndex)) return false;
            value = reader.GetInt64(intOrBoolIndex) != 0;
            return true;
        } else if (string.Equals(valueType, "int", StringComparison.Ordinal)) {
            if (reader.IsDBNull(intOrBoolIndex)) return false;
            value = reader.GetInt32(intOrBoolIndex);
            return true;
        } else if (string.Equals(valueType, "float", StringComparison.Ordinal)) {
            if (reader.IsDBNull(floatIndex)) return false;
            value = (float)reader.GetDouble(floatIndex);
            return true;
        }
        return false;
    }

    public async ValueTask DisposeAsync() {
        writeLock.Dispose();
        await connection.DisposeAsync();
    }
}

