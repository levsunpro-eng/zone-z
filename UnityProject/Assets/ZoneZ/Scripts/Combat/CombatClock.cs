using UnityEngine;

namespace ZoneZ
{
    public static class CombatClock
    {
        public const int TicksPerSecond = 60;

        // Store the same canonical float representation used by MatchTime (tick / 60f).
        // Adding seconds directly can round above that representation and extend an effect
        // by a whole tick at its supposedly exclusive end boundary.
        public static float AfterSeconds(float now, float seconds)
        {
            int startTick = Mathf.RoundToInt(now * TicksPerSecond);
            int durationTicks = Mathf.CeilToInt(Mathf.Max(0, seconds) * TicksPerSecond - .0001f);
            return (startTick + durationTicks) / (float)TicksPerSecond;
        }
    }
}
