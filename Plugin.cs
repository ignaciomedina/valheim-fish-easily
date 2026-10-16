using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace FishEasily
{
    [BepInPlugin(Guid, "Fish-Easily", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "valheim.fisheasily";
        public const string Version = "1.0.1";

        internal static ManualLogSource Log;

        // 1 - Bait
        internal static ConfigEntry<bool> OneBaitFitsAll;
        internal static ConfigEntry<bool> BaitNotConsumed;

        // 2 - The Bite
        internal static ConfigEntry<bool> FishComeToYou;
        internal static ConfigEntry<bool> AutoHook;

        // 3 - The Fight
        internal static ConfigEntry<bool> NoStaminaDrain;
        internal static ConfigEntry<bool> FishNeverEscape;
        internal static ConfigEntry<bool> InstantLand;

        // 4 - The Line
        internal static ConfigEntry<bool> LineNeverBreaks;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            OneBaitFitsAll = Config.Bind(
                "1 - Bait", "One Bait Fits All", true,
                "Any bait catches any fish. Vanilla only bites when the fish's own bait table " +
                "matches what is on your line, which is why you end up carrying five bait types.");

            BaitNotConsumed = Config.Bind(
                "1 - Bait", "Bait Not Consumed", true,
                "Put the bait back in your inventory after a catch, so one stack lasts forever.");

            FishComeToYou = Config.Bind(
                "2 - The Bite", "Fish Come To You", true,
                "Fish always swim towards your float instead of only sometimes noticing it. " +
                "Nothing is spawned; it only removes the coin-flip in Fish.FindFloat.");

            AutoHook = Config.Bind(
                "2 - The Bite", "Auto Hook", true,
                "Hook the fish the moment it nibbles. No reaction timing.");

            NoStaminaDrain = Config.Bind(
                "3 - The Fight", "No Stamina Drain", true,
                "Reeling and holding a hooked fish cost no stamina, so you never lose a fish " +
                "to an empty bar mid-fight.");

            FishNeverEscape = Config.Bind(
                "3 - The Fight", "Fish Never Escape", true,
                "Fish never do their escape struggle (the thing that halves reel speed and " +
                "doubles stamina drain).");

            InstantLand = Config.Bind(
                "3 - The Fight", "Instant Land", true,
                "The fish is landed the moment it is hooked. No tug-of-war, no button hold. " +
                "Turn this off to keep the fight but keep every other convenience.");

            LineNeverBreaks = Config.Bind(
                "4 - The Line", "Line Never Breaks", true,
                "The line cannot snap from distance or from a fighting fish.");

            FishingPatches.ResolveTargets();

            _harmony = new Harmony(Guid);
            // NOTE: Harmony.PatchAll(Type) only builds a processor for that one type and
            // never descends into nested classes, so it silently applied nothing here.
            // PatchAll(Assembly) is the overload that walks nested types. It also throws on
            // any target it cannot find, so a missing method aborts loading with Harmony's
            // own error naming it.
            _harmony.PatchAll(typeof(Plugin).Assembly);
            VerifyPatches();

            Logger.LogInfo($"Fish-Easily {Version} loaded - fishing is now a foregone conclusion.");
        }

        /// <summary>
        /// Log what Harmony actually patched, so a patch call that walks no classes at all
        /// (the PatchAll(Type) mistake above) shows up as "0 patched" instead of silence.
        /// </summary>
        private void VerifyPatches()
        {
            var patched = new List<MethodBase>(_harmony.GetPatchedMethods());
            Logger.LogInfo($"Fish-Easily: Harmony reports {patched.Count} patched method(s).");

            foreach (MethodBase m in patched)
            {
                Logger.LogInfo($"  patched: {m.DeclaringType?.Name}.{m.Name}");
            }
        }
    }
}
