using System;
using System.Text;
using UnityEngine;

namespace MetaDyn.Networking.HostMigration
{
    /// <summary>
    /// Owns the small, versioned lifecycle payload used to prove UGS host migration.
    /// Player and durable world state are added in later migration increments.
    /// </summary>
    internal sealed class MetaDynUGSMigrationCoordinator
    {
        private const int CurrentSchemaVersion = 1;

        internal sealed class LocalPlayerState
        {
            public string UgsPlayerId;
            public string SupabaseUserId;
            public string DisplayName;
            public string ActiveAvatarModelId;
            public int AvatarIndex;
            public Vector3 Position;
            public Quaternion Rotation;
            public byte PermissionLevel;
            public bool IsMuted;
            public long CapturedUnixTimeMilliseconds;
        }

        [Serializable]
        private sealed class LifecycleSnapshot
        {
            public int schemaVersion;
            public long sequence;
            public long capturedUnixTimeMilliseconds;
        }

        private readonly object _syncRoot = new object();
        private long _sequence;
        private byte[] _pendingSnapshot;
        private LocalPlayerState _localPlayerState;

        public bool HasPendingSnapshot
        {
            get
            {
                lock (_syncRoot)
                {
                    return _pendingSnapshot != null && _pendingSnapshot.Length > 0;
                }
            }
        }

        public byte[] GenerateLifecycleSnapshot()
        {
            lock (_syncRoot)
            {
                var snapshot = new LifecycleSnapshot
                {
                    schemaVersion = CurrentSchemaVersion,
                    sequence = ++_sequence,
                    capturedUnixTimeMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                return Encoding.UTF8.GetBytes(JsonUtility.ToJson(snapshot));
            }
        }

        public void ApplyLifecycleSnapshot(byte[] migrationData)
        {
            if (migrationData == null || migrationData.Length == 0)
            {
                Debug.LogWarning("[UGS MIGRATION] No lifecycle snapshot was available for the replacement host.");
                return;
            }

            try
            {
                string json = Encoding.UTF8.GetString(migrationData);
                var snapshot = JsonUtility.FromJson<LifecycleSnapshot>(json);
                if (snapshot == null || snapshot.schemaVersion != CurrentSchemaVersion)
                {
                    Debug.LogWarning("[UGS MIGRATION] Ignored an unsupported lifecycle snapshot.");
                    return;
                }

                lock (_syncRoot)
                {
                    _sequence = Math.Max(_sequence, snapshot.sequence);
                    _pendingSnapshot = (byte[])migrationData.Clone();
                }

                Debug.Log($"[UGS MIGRATION] Applied lifecycle snapshot sequence {snapshot.sequence}.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UGS MIGRATION] Ignored an invalid lifecycle snapshot: {ex.Message}");
            }
        }

        public void CaptureLocalPlayerState(LocalPlayerState state)
        {
            if (state == null) return;

            lock (_syncRoot)
            {
                _localPlayerState = Clone(state);
            }
        }

        public bool TryGetLocalPlayerState(out LocalPlayerState state)
        {
            lock (_syncRoot)
            {
                state = Clone(_localPlayerState);
                return state != null;
            }
        }

        public void Reset()
        {
            lock (_syncRoot)
            {
                _sequence = 0;
                _pendingSnapshot = null;
                _localPlayerState = null;
            }
        }

        private static LocalPlayerState Clone(LocalPlayerState source)
        {
            if (source == null) return null;

            return new LocalPlayerState
            {
                UgsPlayerId = source.UgsPlayerId,
                SupabaseUserId = source.SupabaseUserId,
                DisplayName = source.DisplayName,
                ActiveAvatarModelId = source.ActiveAvatarModelId,
                AvatarIndex = source.AvatarIndex,
                Position = source.Position,
                Rotation = source.Rotation,
                PermissionLevel = source.PermissionLevel,
                IsMuted = source.IsMuted,
                CapturedUnixTimeMilliseconds = source.CapturedUnixTimeMilliseconds
            };
        }
    }
}
