using System;

namespace ZoneZ
{
    [Serializable]
    public sealed class NetworkCommand
    {
        public int sequence;
        public float moveX, moveY, yaw, pitch;
        public bool jump, crouch, walk, fire, firePressed, fireReleased, alt, reload, use, ultimate;
        public int slot = -1;
    }

    [Serializable]
    public sealed class NetworkMessage
    {
        public string type, nickname, code, content, reason, primary, pistol, ability;
        public int protocol, actorId;
        public NetworkCommand input;
        public NetworkSnapshot snapshot;
    }

    [Serializable]
    public sealed class NetworkSnapshot
    {
        public string matchId, mode, winner, roundReason;
        public int phase, tick, round;
        public float time, remaining, preparation;
        public int[] wins;
        public string[] feed;
        public float[] feedTimes;
        public NetworkActorState[] actors;
        public NetworkObjectState[] loot, projectiles;
    }

    [Serializable]
    public sealed class NetworkActorState
    {
        public int id, team, kills, deaths, assists, score;
        public string name;
        public bool alive, human, crouch;
        public float x, y, z, yaw, pitch, vx, vy, vz, hp, maxHp, armor, protection, respawn;
        public WeaponNetworkState weapon;
        public AbilityNetworkState ability;
    }

    [Serializable]
    public sealed class NetworkObjectState
    {
        public int key;
        public string kind;
        public float x, y, z, rx, ry, rz, expires;
    }
}
