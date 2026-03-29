using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
namespace CombatAI.Patches
{
    public static class Building_TurretGun_Patch
    {
        [HarmonyPatch(typeof(Building_TurretGun), nameof(Building_TurretGun.SpawnSetup))]
        private static class Building_TurretGun_SpawnSetup_Patch
        {
            public static void Postfix(Building_TurretGun __instance)
            {
                __instance.Map.GetComp_Fast<TurretTracker>().Register(__instance);
            }
        }

        [HarmonyPatch(typeof(Building_TurretGun), nameof(Building_TurretGun.DeSpawn))]
        private static class Building_TurretGun_DeSpawn_Patch
        {
            public static void Prefix(Building_TurretGun __instance)
            {
                __instance.Map.GetComp_Fast<TurretTracker>().DeRegister(__instance);
            }
        }

        /// <summary>

        ///     Prevents enemy overhead-fire turrets (mortars) from targeting the player

        /// </summary>
        [HarmonyPatch]
        private static class Building_TurretGun_TryFindNewTarget_Patch
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                // Always patch the vanilla base class
                yield return AccessTools.Method(typeof(Building_TurretGun), nameof(Building_TurretGun.TryFindNewTarget));

                // If CE is loaded, also patch Building_TurretGunCE.TryFindNewTarget if CE
                // declares its own override (DeclaredOnly = only if CE explicitly overrides it).
                System.Type ceTurretType = GenTypes.GetTypeInAnyAssembly("CombatExtended.Building_TurretGunCE");
                if (ceTurretType != null)
                {
                    MethodInfo ceOverride = ceTurretType.GetMethod(
                        nameof(Building_TurretGun.TryFindNewTarget),
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (ceOverride != null)
                        yield return ceOverride;
                }
            }

            public static bool Prefix(Building_TurretGun __instance, ref LocalTargetInfo __result)
            {
                // Only intercept overhead-projectile turrets (mortars etc.)
                if (!__instance.AttackVerb.ProjectileFliesOverhead())
                    return true;

                // Only apply to turrets hostile to the player
                Faction playerFaction = Faction.OfPlayerSilentFail;
                if (playerFaction == null || !__instance.HostileTo(playerFaction))
                    return true;

                Map map = __instance.Map;
                if (map == null)
                    return true;

                // Require the CAI sight system to be present; if not, defer to vanilla logic.
                SightTracker sightTracker = map.GetComp_Fast<SightTracker>();
                if (sightTracker == null)
                    return true;

                // raidersAndHostiles.grid stores per-cell signal strength cast by all
                // hostile/raider pawns.  A value > 0 at a player pawn's position means
                // at least one enemy is currently watching that cell — i.e. they have
                // "seen" the player.
                ITSignalGrid        enemySightGrid = sightTracker.raidersAndHostiles.grid;
                IReadOnlyList<Pawn> allPawns       = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < allPawns.Count; i++)
                {
                    Pawn pawn = allPawns[i];
                    if (pawn.Spawned && !pawn.Dead
                        && pawn.Faction != null
                        && pawn.Faction.HostileTo(__instance.Faction)
                        && enemySightGrid.GetSignalStrengthAt(pawn.Position) > 0f)
                    {
                        return true; // at least one player pawn is visible to the enemy — allow
                    }
                }

                // No player pawn detected by enemy sight yet — suppress targeting.
                __result = LocalTargetInfo.Invalid;
                return false;
            }
        }
    }
}
