using System;
using Unity.Collections;
using Unity.Netcode;

namespace MetaDyn.UserList
{
    public struct MetaDynUGSUserData : INetworkSerializable, IEquatable<MetaDynUGSUserData>
    {
        public ulong ClientId;
        public FixedString32Bytes PlayerName;
        public FixedString64Bytes UserId;
        public bool IsMuted;
        public byte PermissionLevel;

        public bool IsAdmin => PermissionLevel >= 2;
        public bool IsModerator => PermissionLevel >= 1;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref PlayerName);
            serializer.SerializeValue(ref UserId);
            serializer.SerializeValue(ref IsMuted);
            serializer.SerializeValue(ref PermissionLevel);
        }

        public bool Equals(MetaDynUGSUserData other)
        {
            return ClientId == other.ClientId
                && PlayerName.Equals(other.PlayerName)
                && UserId.Equals(other.UserId)
                && IsMuted == other.IsMuted
                && PermissionLevel == other.PermissionLevel;
        }

        public override bool Equals(object obj)
        {
            return obj is MetaDynUGSUserData other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ClientId.GetHashCode();
        }
    }
}
