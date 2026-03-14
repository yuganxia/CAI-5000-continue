using System;
using System.Linq;
using System.Reflection;
using CombatAI.Comps;
using HarmonyLib;
using RimWorld;
using Verse;
namespace CombatAI
{
    [LoadIf("CETeam.CombatExtended")]
    public class Mod_CE
    {
        [Unsaved]
        private static readonly FlagArray turretsCE = new FlagArray(short.MaxValue);

        public static bool active;

        public static JobDef ReloadWeapon;
        public static JobDef HunkerDown;

        [LoadNamed("CombatExtended.Verb_ShootCE:_isAiming")]
        public static FieldInfo Verb_ShootCE_isAiming;
        [LoadNamed("CombatExtended.Verb_ShootCE")]
        public static Type Verb_ShootCE;

        [LoadNamed("CombatExtended.ProjectilePropertiesCE")]
        public static Type ProjectilePropertiesCE;
        [LoadNamed("CombatExtended.ProjectilePropertiesCE:armorPenetrationSharp")]
        public static FieldInfo ProjectilePropertiesCE_ArmorPenetrationSharp;
        [LoadNamed("CombatExtended.ProjectilePropertiesCE:armorPenetrationBlunt")]
        public static FieldInfo ProjectilePropertiesCE_ArmorPenetrationBlunt;

        [LoadNamed("CombatExtended.ToolCE")]
        public static Type ToolCE;
        [LoadNamed("CombatExtended.ToolCE:armorPenetrationSharp")]
        public static FieldInfo ToolCE_ArmorPenetrationSharp;
        [LoadNamed("CombatExtended.ToolCE:armorPenetrationBlunt")]
        public static FieldInfo ToolCE_ArmorPenetrationBlunt;

        [LoadNamed("CombatExtended.Building_TurretGunCE")]
        public static Type Building_TurretGunCE;
        [LoadNamed("CombatExtended.Building_TurretGunCE:Active", LoadableType.Getter)]
        public static MethodInfo Building_TurretGunCE_Active;
        [LoadNamed("CombatExtended.Building_TurretGunCE:MannedByColonist", LoadableType.Getter)]
        public static MethodInfo Building_TurretGunCE_MannedByColonist;
        [LoadNamed("CombatExtended.Building_TurretGunCE:IsMannable", LoadableType.Getter)]
        public static MethodInfo Building_TurretGunCE_IsMannable;

        public static bool IsAimingCE(Verb verb)
        {
            return Verb_ShootCE_isAiming != null && Verb_ShootCE.IsInstanceOfType(verb) && (bool)Verb_ShootCE_isAiming.GetValue(verb);
        }

        public static bool IsTurretActiveCE(Building_Turret turret)
        {
            if (turret == null) return false;
            if (!turretsCE[turret.def.index]) return false;
            bool manable = false;
            if (Building_TurretGunCE_IsMannable != null)
            {
                manable = (bool)Building_TurretGunCE_IsMannable.Invoke(turret, Array.Empty<object>());
            }
            if (manable)
            {
                if (Building_TurretGunCE_MannedByColonist != null)
                    return (bool)Building_TurretGunCE_MannedByColonist.Invoke(turret, Array.Empty<object>());
                return false;
            }
            else
            {
                if (Building_TurretGunCE_Active != null)
                    return (bool)Building_TurretGunCE_Active.Invoke(turret, Array.Empty<object>());
                return false;
            }
        }

        public static float GetProjectileArmorPenetration(ProjectileProperties props)
        {
            if (props.GetType() != ProjectilePropertiesCE)
            {
                return CombatAI.Compatibility.ProjectilePropertiesCompat.GetArmorPenetration(props);
            }
            if (props.damageDef.armorCategory == DamageArmorCategoryDefOf.Sharp)
            {
                return (float)ProjectilePropertiesCE_ArmorPenetrationSharp.GetValue(props);
            }
            return (float)ProjectilePropertiesCE_ArmorPenetrationBlunt.GetValue(props);
        }


        [RunIf(loaded: true)]
        private static void OnActive()
        {
            Finder.Settings.LeanCE_Enabled = true;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.thingClass == Building_TurretGunCE)
                {
                    turretsCE[def.index] = true;
                }
            }
            Finder.Harmony.Patch(AccessTools.Method(Building_TurretGunCE, nameof(Building_Turret.SpawnSetup)), postfix: new HarmonyMethod(AccessTools.Method(typeof(Building_TurretGunCE_Patch), nameof(Building_TurretGunCE_Patch.SpawnSetup))));
            Finder.Harmony.Patch(AccessTools.Method(Building_TurretGunCE, nameof(Building_Turret.DeSpawn)), new HarmonyMethod(AccessTools.Method(typeof(Building_TurretGunCE_Patch), nameof(Building_TurretGunCE_Patch.DeSpawn))));
            // Use GetDeclaredMethods to avoid AmbiguousMatchException:
            // Verb_ShootCE inherits two TryStartCastOn overloads from Verse.Verb, so
            // AccessTools.Method(type, name) without parameter types would be ambiguous.
            // We only want to patch an override declared directly on Verb_ShootCE itself.
            // If CE doesn't declare one, the base Verse.Verb patches (applied by the main
            // ISMA verb loop) already cover Verb_ShootCE via virtual dispatch.
            MethodInfo mCETryStartCastOn = AccessTools.GetDeclaredMethods(Verb_ShootCE)
                .FirstOrDefault(m => m.Name == "TryStartCastOn" && m.ReturnType == typeof(bool));
            if (mCETryStartCastOn != null)
            {
                Finder.Harmony.Patch(mCETryStartCastOn, postfix: new HarmonyMethod(AccessTools.Method(typeof(Verb_ShootCE_Targeting_Patch), nameof(Verb_ShootCE_Targeting_Patch.Postfix))));
                Log.Message("ISMA: Patched Verb_ShootCE:TryStartCastOn for targeting notifications.");
            }
        }

        [RunIf(loaded: false)]
        private static void OnInActive()
        {
            Finder.Settings.LeanCE_Enabled = false;
        }

        private static class Building_TurretGunCE_Patch
        {
            public static void SpawnSetup(Building_Turret __instance)
            {
                __instance.Map.GetComp_Fast<TurretTracker>().Register(__instance);
            }

            public static void DeSpawn(Building_Turret __instance)
            {
                __instance.Map.GetComp_Fast<TurretTracker>().DeRegister(__instance);
            }
        }

        /// <summary>
        /// Postfix for Verb_ShootCE.TryStartCastOn that notifies the target pawn it is
        /// being aimed at. Registered manually so the Transpiler in Verb_TryCastNextBurstShot_Patch
        /// can continue to skip all CE verbs safely. Coexists with CE AIMBOT 2 because
        /// both use pure Postfixes and touch completely separate state.
        /// </summary>
        private static class Verb_ShootCE_Targeting_Patch
        {
            public static void Postfix(Verb __instance, bool __result)
            {
                if (__result && __instance?.caster != null)
                {
                    if (__instance.CurrentTarget is { IsValid: true, Thing: Pawn targetPawn } && (__instance.caster.HostileTo(targetPawn)))
                    {
                        ThingComp_CombatAI comp = targetPawn.AI();
                        if (comp != null)
                        {
                            comp.Notify_BeingTargeted(__instance.caster, __instance);
                        }
                    }
                }
            }
        }
    }
}
