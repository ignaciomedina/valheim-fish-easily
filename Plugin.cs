using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace FishEasily
{
    [BepInPlugin(Guid, "Fish-Easily", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "valheim.fisheasily";

        internal static ManualLogSource Log;

        // 1 - Bait
        internal static ConfigEntry<bool> OneBaitFitsAll;
        internal static ConfigEntry<bool> BaitNotConsumed;

        // 2 - The Bite
        internal static ConfigEntry<bool> FishComeToYou;
        internal static ConfigEntry<bool> AutoHook;
        internal static ConfigEntry<float> HookWindowSeconds;

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

            HookWindowSeconds = Config.Bind(
                "2 - The Bite", "Hook Window Seconds", 3.0f,
                "With Auto Hook on, how long a nibble stays valid (seconds). Vanilla is 0.5. " +
                "Set to 0 to hook no matter how long ago the nibble was.");

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
            // PatchAll(Assembly) is the overload that walks nested types.
            _harmony.PatchAll(typeof(Plugin).Assembly);
            VerifyPatches();

            Logger.LogInfo("Fish-Easily 1.0.0 loaded - fishing is now a foregone conclusion.");
        }

        /// <summary>
        /// Harmony fails quietly: a patch class that matches nothing produces no error at
        /// all. Ask Harmony what it actually patched and shout if a target is missing.
        /// </summary>
        private void VerifyPatches()
        {
            var patched = new List<MethodBase>(_harmony.GetPatchedMethods());
            Logger.LogInfo($"Fish-Easily: Harmony reports {patched.Count} patched method(s).");

            foreach (MethodBase m in patched)
            {
                Logger.LogInfo($"  patched: {m.DeclaringType?.Name}.{m.Name}");
            }

            string[] expected =
            {
                "Fish.TestBate", "Fish.FindFloat", "Fish.Escape", "Fish.GetStaminaUse",
                "FishingFloat.Awake", "FishingFloat.FixedUpdate"
            };

            var have = new HashSet<string>(
                patched.Select(m => $"{m.DeclaringType?.Name}.{m.Name}"));

            var missing = expected.Where(e => !have.Contains(e)).ToArray();
            if (missing.Length > 0)
            {
                Logger.LogError("Fish-Easily: these methods were NOT patched, so their features "
                                + "will do nothing: " + string.Join(", ", missing));
            }
            else
            {
                Logger.LogInfo("Fish-Easily: all 6 patch targets confirmed applied.");
            }
        }
    }
}
