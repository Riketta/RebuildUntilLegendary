using System.Collections.Generic;
using Verse;

namespace RebuildUntilLegendary
{
    /// <summary>
    /// Optional rich logging, off by default (see mod settings, developer mode only).
    /// Lifecycle events go through Log/VerboseLog; per-tick events such as denied
    /// build attempts are throttled so even verbose logging stays readable.
    /// </summary>
    internal static class DebugLog
    {
        private const string Prefix = "[RebuildUntilLegendary] ";

        private const int ThrottleTicks = 600;

        private static readonly Dictionary<string, int> NextAllowedTick = new Dictionary<string, int>();

        public static bool Enabled => RebuildUntilLegendaryMod.Settings?.debugLogging ?? false;

        public static bool Verbose => Enabled && (RebuildUntilLegendaryMod.Settings?.verboseLogging ?? false);

        public static void Log(string message)
        {
            if (Enabled)
            {
                Verse.Log.Message(Prefix + TickTag() + message);
            }
        }

        public static void Warn(string message)
        {
            if (Enabled)
            {
                Verse.Log.Warning(Prefix + TickTag() + message);
            }
        }

        public static void VerboseLog(string message)
        {
            if (Verbose)
            {
                Verse.Log.Message(Prefix + TickTag() + message);
            }
        }

        /// <summary>Logs at most one message per key per ThrottleTicks, so workgiver
        /// scans that hit a restricted blueprint many times per second do not flood
        /// the log while the chosen builder is simply busy elsewhere.</summary>
        public static void VerboseThrottled(string key, string message)
        {
            if (!Verbose)
            {
                return;
            }
            int now = SafeTicksGame();
            if (NextAllowedTick.TryGetValue(key, out int next) && now < next)
            {
                return;
            }
            NextAllowedTick[key] = now + ThrottleTicks;
            Verse.Log.Message(Prefix + TickTag() + message);
        }

        /// <summary>Find.TickManager throws while mod constructors run (Current.Game
        /// is still null during play data loading), so read the tick via the game
        /// reference and settle for no tag while the game does not exist yet.</summary>
        private static int SafeTicksGame()
        {
            Game game = Current.Game;
            return game != null && game.tickManager != null ? game.tickManager.TicksGame : 0;
        }

        private static string TickTag()
        {
            int ticks = SafeTicksGame();
            return ticks > 0 ? "(t" + ticks + ") " : "";
        }
    }
}
