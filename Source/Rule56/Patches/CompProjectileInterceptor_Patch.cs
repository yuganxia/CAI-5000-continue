using HarmonyLib;
using RimWorld;
using Verse;
namespace CombatAI.Patches
{
    public static class CompProjectileInterceptor_Patch
    {
        [HarmonyPatch(typeof(CompProjectileInterceptor), nameof(CompProjectileInterceptor.CompTick))]
        private static class CompProjectileInterceptor_CompTick_Patch
        {
            public static void Postfix(CompProjectileInterceptor __instance)
            {
                if ((__instance.parent?.IsHashIntervalTick(30) ?? false) && !__instance.parent.Destroyed && __instance.parent.Spawned && __instance.Active)
                {
                    var comp = __instance.parent.Map.GetComp_Fast<MapComponent_CombatAI>();
                    if (comp != null && comp.interceptors != null)
                    {
                        comp.interceptors.TryRegister(__instance);
                    }
                }
            }
        }
    }
}
