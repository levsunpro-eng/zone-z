using System;

namespace ZoneZ
{
    // Integer simulation ticks make the inclusive 20-second boundary unambiguous.
    public sealed class KillCharge
    {
        public int Count { get; private set; }
        public int Deadline { get; private set; }
        public bool Ready { get; private set; }
        public void Expire(int tick) { if (Count > 0 && tick > Deadline) Count = 0; }
        public bool Register(int tick, int required, bool eligible, bool active)
        {
            Expire(tick);
            if (!eligible || active || Ready) return false;
            if (Count == 0) Deadline = tick + 1200;
            Count++;
            if (Count < required) return false;
            Ready = true; Count = 0; return true;
        }
        public void Consume() { Ready = false; Count = 0; }
        public void Reset(bool preserveReady) { Count = 0; if (!preserveReady) Ready = false; }
    }

    public static class RewardRules
    {
        public static int Team(int result, int kills, int assists, int rank)
        {
            int baseReward = result > 0 ? 300 : result < 0 ? 150 : 200;
            return baseReward + Math.Max(0,kills)*20 + Math.Max(0,assists)*5 + RankBonus(rank,100,60,30);
        }
        public static int Deathmatch(int kills,int rank) => 180 + Math.Min(30,Math.Max(0,kills))*8 + RankBonus(rank,250,150,100);
        static int RankBonus(int rank,int a,int b,int c) => rank == 1 ? a : rank == 2 ? b : rank == 3 ? c : 0;
    }
}
