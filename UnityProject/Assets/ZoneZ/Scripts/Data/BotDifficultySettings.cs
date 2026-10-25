using System;
using Newtonsoft.Json.Linq;

namespace ZoneZ
{
    public sealed class BotDifficultySettings
    {
        public static readonly BotDifficultySettings Defaults = new BotDifficultySettings(null);

        public readonly float ReactionMinSeconds, ReactionMaxSeconds;
        public readonly float TrackingMinSeconds, TrackingMaxSeconds;
        public readonly float AimYawDegrees, AimPitchDegrees, TurnDegreesPerSecond;
        public readonly int BurstMinShots, BurstMaxShots;
        public readonly float BurstPauseMinSeconds, BurstPauseMaxSeconds, DamageMultiplier;

        BotDifficultySettings(JObject data)
        {
            ReactionMinSeconds = Number(data, "reactionMinSeconds", .65f, 0, 10);
            ReactionMaxSeconds = Number(data, "reactionMaxSeconds", 1.05f, 0, 10);
            TrackingMinSeconds = Number(data, "trackingMinSeconds", .24f, .01f, 5);
            TrackingMaxSeconds = Number(data, "trackingMaxSeconds", .36f, .01f, 5);
            AimYawDegrees = Number(data, "aimYawDegrees", 2.8f, 0, 45);
            AimPitchDegrees = Number(data, "aimPitchDegrees", 1.8f, 0, 45);
            TurnDegreesPerSecond = Number(data, "turnDegreesPerSecond", 115, 1, 1080);
            BurstMinShots = Integer(data, "burstMinShots", 2, 1, 100);
            BurstMaxShots = Integer(data, "burstMaxShots", 4, 1, 100);
            BurstPauseMinSeconds = Number(data, "burstPauseMinSeconds", .55f, 0, 10);
            BurstPauseMaxSeconds = Number(data, "burstPauseMaxSeconds", 1, 0, 10);
            DamageMultiplier = Number(data, "damageMultiplier", .6f, 0, 3);
            CheckOrder("reactionMinSeconds", ReactionMinSeconds, "reactionMaxSeconds", ReactionMaxSeconds);
            CheckOrder("trackingMinSeconds", TrackingMinSeconds, "trackingMaxSeconds", TrackingMaxSeconds);
            CheckOrder("burstMinShots", BurstMinShots, "burstMaxShots", BurstMaxShots);
            CheckOrder("burstPauseMinSeconds", BurstPauseMinSeconds, "burstPauseMaxSeconds", BurstPauseMaxSeconds);
        }

        public static BotDifficultySettings FromJson(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return Defaults;
            if (!(token is JObject data))
                throw new FormatException("botDifficulty must be a JSON object.");
            return new BotDifficultySettings(data);
        }

        static float Number(JObject data, string field, float fallback, float minimum, float maximum)
        {
            JToken token = data?[field];
            if (token == null) return fallback;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw new FormatException("botDifficulty." + field + " must be a number.");
            double value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum)
                throw new FormatException("botDifficulty." + field + " must be finite and between " + minimum + " and " + maximum + ".");
            return (float)value;
        }

        static int Integer(JObject data, string field, int fallback, int minimum, int maximum)
        {
            JToken token = data?[field];
            if (token == null) return fallback;
            if (token.Type != JTokenType.Integer)
                throw new FormatException("botDifficulty." + field + " must be an integer.");
            return (int)Number(data, field, fallback, minimum, maximum);
        }

        static void CheckOrder(string minField, float minimum, string maxField, float maximum)
        {
            if (minimum > maximum)
                throw new FormatException("botDifficulty." + minField + " must not exceed " + maxField + ".");
        }
    }
}
