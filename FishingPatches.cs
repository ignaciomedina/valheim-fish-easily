using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FishEasily
{
    /// <summary>
    /// Everything here is patched against Valheim's own assembly (assembly_valheim.dll).
    /// Private members are reached through cached Harmony accessors, resolved once at load
    /// so a game patch that renames something fails loudly instead of silently doing nothing.
    /// </summary>
    internal static class FishingPatches
    {
        // ---- FishingFloat private fields ------------------------------------------------
        private static AccessTools.FieldRef<FishingFloat, Fish> _nibbler;
        private static AccessTools.FieldRef<FishingFloat, float> _nibbleTime;
        private static AccessTools.FieldRef<FishingFloat, bool> _baitConsumed;
        private static AccessTools.FieldRef<FishingFloat, ZNetView> _nview;

        // ---- Fish private fields --------------------------------------------------------
        private static AccessTools.FieldRef<Fish, FishingFloat> _failedBait;

        // ---- private methods ------------------------------------------------------------
        private static MethodInfo _setCatch;
        private static MethodInfo _returnBait;
        private static MethodInfo _getOwner;

        private static readonly List<string> Missing = new List<string>();
        private static bool _fixedUpdateErrorLogged;

        internal static void ResolveTargets()
        {
            Missing.Clear();

            _nibbler = Try(() => AccessTools.FieldRefAccess<FishingFloat, Fish>("m_nibbler"), "FishingFloat.m_nibbler");
            _nibbleTime = Try(() => AccessTools.FieldRefAccess<FishingFloat, float>("m_nibbleTime"), "FishingFloat.m_nibbleTime");
            _baitConsumed = Try(() => AccessTools.FieldRefAccess<FishingFloat, bool>("m_baitConsumed"), "FishingFloat.m_baitConsumed");
            _nview = Try(() => AccessTools.FieldRefAccess<FishingFloat, ZNetView>("m_nview"), "FishingFloat.m_nview");
            _failedBait = Try(() => AccessTools.FieldRefAccess<Fish, FishingFloat>("m_failedBait"), "Fish.m_failedBait");

            _setCatch = TryMethod("FishingFloat.SetCatch");
            _returnBait = TryMethod("FishingFloat.ReturnBait");
            _getOwner = TryMethod("FishingFloat.GetOwner");

            if (Missing.Count > 0)
            {
                Plugin.Log.LogError("Fish-Easily: " + Missing.Count + " member(s) not found in this Valheim build: "
                                    + string.Join(", ", Missing));
                Plugin.Log.LogError("Fish-Easily will still load, but the features that rely on them are disabled.");
            }
            else
            {
                Plugin.Log.LogInfo("Fish-Easily: all patch targets resolved against this Valheim build.");
            }
        }

        private static T Try<T>(Func<T> f, string label) where T : class
        {
            try
            {
                T v = f();
                if (v == null) Missing.Add(label);
                return v;
            }
            catch (Exception)
            {
                Missing.Add(label);
                return null;
            }
        }

        private static MethodInfo TryMethod(string label)
        {
            try
            {
                string[] split = label.Split('.');
                MethodInfo m = AccessTools.Method(AccessTools.TypeByName(split[0]), split[1]);
                if (m == null) Missing.Add(label);
                return m;
            }
            catch (Exception)
            {
                Missing.Add(label);
                return null;
            }
        }

        private static bool Ready =>
            _nibbler != null && _nibbleTime != null && _baitConsumed != null &&
            _nview != null && _setCatch != null && _getOwner != null;

        // ================================================================================
        // 1 - BAIT
        // ================================================================================

        /// <summary>
        /// Fish.TestBate decides whether the fish accepts the bait on your line:
        ///     bait2.m_bait.name == ff.GetBait() && Random.value &lt; bait2.m_chance
        /// Returning true unconditionally makes every bait catch every fish.
        /// </summary>
        [HarmonyPatch(typeof(Fish), "TestBate")]
        private static class Fish_TestBate
        {
            private static bool Prefix(ref bool __result)
            {
                if (!Plugin.OneBaitFitsAll.Value) return true;
                __result = true;
                return false;
            }
        }

        // ================================================================================
        // 2 - THE BITE
        // ================================================================================

        /// <summary>
        /// Vanilla Fish.FindFloat picks a random in-range float and only returns it
        /// Random.value &lt; m_baseHookChance of the time (0.5 by default), so half of a
        /// fish's waypoints ignore your line entirely. Return the nearest float instead.
        /// </summary>
        [HarmonyPatch(typeof(Fish), "FindFloat")]
        private static class Fish_FindFloat
        {
            private static bool Prefix(Fish __instance, ref FishingFloat __result)
            {
                if (!Plugin.FishComeToYou.Value) return true;

                __result = NearestFloat(__instance);
                return false;
            }
        }

        private static FishingFloat NearestFloat(Fish fish)
        {
            FishingFloat best = null;
            float bestDist = float.MaxValue;

            FishingFloat failed = _failedBait != null ? _failedBait(fish) : null;

            foreach (FishingFloat ff in FishingFloat.GetAllInstances())
            {
                if (ff == null || !ff.IsInWater() || ff.GetCatch() != null) continue;
                if (ff == failed) continue;

                float d = Vector3.Distance(fish.transform.position, ff.transform.position);
                if (d > ff.m_range) continue;

                if (d < bestDist)
                {
                    bestDist = d;
                    best = ff;
                }
            }

            return best;
        }

        // ================================================================================
        // 3 - THE FIGHT
        // ================================================================================

        /// <summary>
        /// Fish escaping is the tug-of-war: it halves reel speed and uses m_escapeStaminaUse
        /// instead of m_staminaUse. Skipping Escape() removes it entirely.
        /// </summary>
        [HarmonyPatch(typeof(Fish), "Escape")]
        private static class Fish_Escape
        {
            private static bool Prefix()
            {
                return !Plugin.FishNeverEscape.Value;
            }
        }

        /// <summary>Fish.GetStaminaUse feeds the reeling cost. Zero it out.</summary>
        [HarmonyPatch(typeof(Fish), "GetStaminaUse")]
        private static class Fish_GetStaminaUse
        {
            private static bool Prefix(ref float __result)
            {
                if (!Plugin.NoStaminaDrain.Value) return true;
                __result = 0f;
                return false;
            }
        }

        /// <summary>
        /// The float's own stamina numbers are public serialized fields, so they can just be
        /// rewritten per instance: hooked drain, pull drain, and both max-skill variants
        /// (the originals are Mathf.Lerp'd by fishing skill, so all four must go to zero).
        /// Line break distance and reel speed live here too.
        /// </summary>
        [HarmonyPatch(typeof(FishingFloat), "Awake")]
        private static class FishingFloat_Awake
        {
            private static void Postfix(FishingFloat __instance)
            {
                if (Plugin.NoStaminaDrain.Value)
                {
                    __instance.m_pullStaminaUse = 0f;
                    __instance.m_pullStaminaUseMaxSkillMultiplier = 0f;
                    __instance.m_hookedStaminaPerSec = 0f;
                    __instance.m_hookedStaminaPerSecMaxSkill = 0f;
                }

                if (Plugin.LineNeverBreaks.Value)
                {
                    // FixedUpdate breaks the line when (rodDist - lineLength) > m_breakDistance
                    // or rodDist > m_maxDistance.
                    __instance.m_breakDistance = 100000f;
                    __instance.m_maxDistance = 100000f;
                }
            }
        }

        /// <summary>
        /// The main loop. Auto-hook the pending nibbler, and optionally land the fish
        /// immediately instead of waiting for the player to hold the reel button.
        /// </summary>
        [HarmonyPatch(typeof(FishingFloat), "FixedUpdate")]
        private static class FishingFloat_FixedUpdate
        {
            private static bool Prefix(FishingFloat __instance)
            {
                try
                {
                    if (!Ready) return true;

                    ZNetView nview = _nview(__instance);
                    if (nview == null || !nview.IsValid() || !nview.IsOwner()) return true;

                    if (Plugin.AutoHook.Value && __instance.GetCatch() == null)
                    {
                        // Vanilla TryToHook's 0.5s window. A fresh nibble is hooked on the next
                        // tick anyway; this only skips a stale one from before Auto Hook was on.
                        Fish nibbler = _nibbler(__instance);
                        if (nibbler != null && Time.time - _nibbleTime(__instance) < 0.5f)
                        {
                            Hook(__instance, nibbler);
                        }
                    }

                    if (Plugin.InstantLand.Value)
                    {
                        Fish hooked = __instance.GetCatch();
                        if (hooked != null && Land(__instance, hooked))
                        {
                            return false; // the fish is in the bag; nothing left to simulate
                        }
                    }
                }
                catch (Exception e)
                {
                    // FixedUpdate runs ~50 times a second per float; log the first failure only.
                    if (!_fixedUpdateErrorLogged)
                    {
                        _fixedUpdateErrorLogged = true;
                        Plugin.Log.LogError("Fish-Easily FixedUpdate patch failed (repeats suppressed): " + e);
                    }
                }

                return true;
            }
        }

        /// <summary>Mirrors the body of FishingFloat.TryToHook.</summary>
        private static void Hook(FishingFloat ff, Fish fish)
        {
            Character owner = (Character)_getOwner.Invoke(ff, null);
            _setCatch.Invoke(ff, new object[] { fish });
            _nibbler(ff) = null;
            Game.instance.IncrementPlayerStat(PlayerStatType.FishHooked);
            if (owner != null) Message(ff, owner, "$msg_fishing_hooked");
        }

        /// <summary>
        /// Mirrors the m_lineLength &lt;= 0.5f branch of FishingFloat.FixedUpdate.
        /// Returns false without touching anything when the owner is gone, so vanilla
        /// FixedUpdate still runs and destroys the orphaned float.
        /// </summary>
        private static bool Land(FishingFloat ff, Fish fish)
        {
            Character owner = (Character)_getOwner.Invoke(ff, null);
            if (owner == null) return false;

            string msg = FishingFloat.Catch(fish, owner);
            Message(ff, owner, msg);

            _setCatch.Invoke(ff, new object[] { null });
            fish.OnHooked(null);

            ZNetView nview = _nview(ff);
            if (nview != null) nview.Destroy();
            return true;
        }

        /// <summary>
        /// The bait leaves the inventory at cast time and SetCatch() marks it consumed.
        /// Every landing (vanilla reel-in and Instant Land) goes through FishingFloat.Catch
        /// while the catch is still set, so FindFloat(fish) finds the float and the refund
        /// works whether Instant Land is on or off. The debug console's catch command has
        /// no float, so FindFloat returns null and nothing is refunded.
        /// </summary>
        [HarmonyPatch(typeof(FishingFloat), nameof(FishingFloat.Catch))]
        private static class FishingFloat_Catch
        {
            private static void Prefix(Fish fish)
            {
                if (!Plugin.BaitNotConsumed.Value || _baitConsumed == null || _returnBait == null) return;

                FishingFloat ff = FishingFloat.FindFloat(fish);
                if (ff == null) return;

                // ReturnBait() only pays out when m_baitConsumed is false, so clear it first.
                _baitConsumed(ff) = false;
                _returnBait.Invoke(ff, null);
            }
        }

        private static void Message(FishingFloat ff, Character owner, string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            owner.Message(MessageHud.MessageType.Center, Localization.instance.Localize(msg));
        }
    }
}
