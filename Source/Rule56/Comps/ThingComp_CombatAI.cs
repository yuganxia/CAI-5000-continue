using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CombatAI.Abilities;
using CombatAI.R;
using CombatAI.Squads;
using CombatAI.Utilities;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using CombatAI.Compatibility;
namespace CombatAI.Comps
{
    public class ThingComp_CombatAI : ThingComp
    {
        private readonly Dictionary<Thing, AIEnvAgentInfo> allAllies;
        private readonly Dictionary<Thing, AIEnvAgentInfo> allEnemies;
        /// <summary>
        ///     Escorting pawns.
        /// </summary>
        private readonly List<Pawn> escorts = new List<Pawn>();
        /// <summary>
        ///     Set of visible enemies. A queue for visible enemies during scans.
        /// </summary>
        private readonly List<Thing> rangedEnemiesTargetingSelf = new List<Thing>(4);
        /// <summary>
        ///     Sapper path nodes.
        /// </summary>
        private readonly List<IntVec3> sapperNodes = new List<IntVec3>();
        /// <summary>
        ///     Aggro countdown ticks.
        /// </summary>
        private int aggroTicks;
        /// <summary>
        ///     Aggro target.
        /// </summary>
        private LocalTargetInfo aggroTarget;

        private Thing _bestEnemy;
        private int   _last;

        private int _sap;
        /// <summary>
        ///     Pawn ability caster.
        /// </summary>
        public Pawn_AbilityCaster abilities;
        /// <summary>
        ///     Saves job logs. for debugging only.
        /// </summary>
        public List<JobLog> jobLogs;
        /// <summary>
        ///     Pawn squad
        /// </summary>
        public Squad squad;
        /// <summary>
        ///     Parent armor report.
        /// </summary>
        private ArmorReport armor;
        /// <summary>
        ///     Cell to stand on while sapping
        /// </summary>
        private IntVec3 cellBefore = IntVec3.Invalid;
        private IntVec3     cellAhead = IntVec3.Invalid;
        public  AIAgentData data;
        /// <summary>
        ///     Custom pawn duty tracker. Allows the execution of new duties then going back to the old one once the new one is
        ///     finished.
        /// </summary>
        public Pawn_CustomDutyTracker duties;
        /// <summary>
        ///     Number of enemies in range.
        ///     Updated by the sightgrid.
        /// </summary>
        public int enemiesInRangeNum;
        /// <summary>
        ///     Whether to find escorts.
        /// </summary>
        private bool findEscorts;
        /// <summary>
        ///     Target forced by the player.
        /// </summary>
        public LocalTargetInfo forcedTarget = LocalTargetInfo.Invalid;
        /// <summary>
        ///     Whether CAI should auto-control this player pawn in combat.
        /// </summary>
        public bool aiAutoControl = false;
        /// <summary>
        ///     Whether this AutoControl ranged pawn should actively pursue enemies outside CAI's current sight coverage (Search and Destroy).
        /// </summary>
        public bool aiSearchAndDestroy = false;
        /// <summary>
        ///     Whether this AutoControl melee pawn should actively pursue enemies outside CAI's current sight coverage (Search and Destroy).
        /// </summary>
        public bool aiSearchAndDestroyMelee = false;
        /// <summary>
        ///     Sapper timestamp
        /// </summary>
        private int sapperStartTick;
        //Whether a scan is occuring.
        private bool scanning;
        /// <summary>
        ///     Parent pawn.
        /// </summary>
        public Pawn selPawn;
        /// <summary>
        ///     Parent sight reader.
        /// </summary>
        public SightTracker.SightReader sightReader;

        public ThingComp_CombatAI()
        {
            allEnemies = new Dictionary<Thing, AIEnvAgentInfo>(32);
            allAllies  = new Dictionary<Thing, AIEnvAgentInfo>(32);
            data       = new AIAgentData();
        }

        /// <summary>
        ///     Whether the pawn is downed or dead.
        /// </summary>
        public bool IsDeadOrDowned
        {
            get => selPawn.Dead || selPawn.Downed;
        }

        /// <summary>
        ///     Whether the pawning is sapping.
        /// </summary>
        public bool IsSapping
        {
            get => cellBefore.IsValid && sapperNodes.Count > 0;
        }
        /// <summary>
        ///     Whether the pawn is available to escort other pawns or available for sapping.
        /// </summary>
        public bool CanSappOrEscort
        {
            get => !IsSapping && GenTicks.TicksGame - releasedTick > 900;
        }

        /// <summary>
        ///     Whether CAI is currently auto-controlling this pawn (only active when drafted).
        /// </summary>
        public bool IsAIAutoControlled => aiAutoControl && selPawn.Drafted;

        /// <summary>
        ///     True when the player has directly commanded this AutoControl pawn
        ///     (e.g., ordered to move or attack), so AI reactions should not interrupt it.
        ///     Detected by comparing the current job's startTick against the last tick
        ///     when our AI itself started a job.
        /// </summary>
        private bool IsPlayerOverriding
        {
            get
            {
                if (!IsAIAutoControlled) return false;
                Job curJob = selPawn.CurJob;
                if (curJob == null) return false;
                if (!curJob.playerForced && !curJob.playerInterruptedForced) return false;
                // If forcedTarget is active, the AI itself manages forced movement — not a player override.
                if (forcedTarget.IsValid) return false;
                // The job started strictly after the last tick our AI started any job.
                int lastAIJobTick = Math.Max(data.LastInterrupted, data.LastRetreated);
                return curJob.startTick > lastAIJobTick;
            }
        }

        private bool IsMechanoidDestValid(IntVec3 cell)
        {
            if (!IsAIAutoControlled || !selPawn.RaceProps.IsMechanoid) return true;
            // If the mech is already outside range, allow any dest; TryIssueMechanoidReturnToRange handles homing.
            if (!MechanitorUtility.InMechanitorCommandRange(selPawn, new LocalTargetInfo(selPawn.Position))) return true;
            // Mech is inside range: destination must also be inside range
            return MechanitorUtility.InMechanitorCommandRange(selPawn, new LocalTargetInfo(cell));
        }

        private bool TryIssueMechanoidReturnToRange()
        {
            if (!IsAIAutoControlled || !selPawn.RaceProps.IsMechanoid) return false;
            if (MechanitorUtility.InMechanitorCommandRange(selPawn, new LocalTargetInfo(selPawn.Position))) return false;
            // Find the overseer to use as a homing direction.
            Pawn overseer = selPawn.GetOverseer();
            if (overseer == null || !overseer.Spawned || overseer.MapHeld != selPawn.MapHeld) return false;
            IntVec3 mechPos     = selPawn.Position;
            IntVec3 overseerPos = overseer.Position;
            Vector3 dir         = (overseerPos - mechPos).ToVector3().normalized;
            IntVec3 dest = overseerPos;
            for (int step = 1; step <= 60; step++)
            {
                IntVec3 candidate = (mechPos.ToVector3() + dir * step).ToIntVec3();
                if (!candidate.InBounds(selPawn.Map)) break;
                if (MechanitorUtility.InMechanitorCommandRange(selPawn, new LocalTargetInfo(candidate)))
                {
                    dest = candidate;
                    break;
                }
            }
            // Already heading to a cell that is inside range — no need to reissue.
            if (selPawn.CurJobDef.Is(JobDefOf.Goto) && selPawn.pather != null
                && MechanitorUtility.InMechanitorCommandRange(selPawn, new LocalTargetInfo(selPawn.pather.Destination.Cell)))
            {
                return true;
            }
            if (!IsPerformingMeleeAnimation(selPawn))
            {
                selPawn.jobs.ClearQueuedJobs();
                Job returnJob = JobMaker.MakeJob(JobDefOf.Goto, dest);
                returnJob.locomotionUrgency     = LocomotionUrgency.Jog;
                returnJob.checkOverrideOnExpire = false;
                selPawn.jobs.StartJob(returnJob, JobCondition.InterruptForced);
                data.LastInterrupted = GenTicks.TicksGame;
            }
            return true;
        }

        private static bool IsPerformingMeleeAnimation(Pawn p)
        {
            try
            {
                if (p == null) return false;
                // stance warmup/busy often indicates an ongoing attack/animation
                if (p.stances?.curStance != null)
                {
                    string sname = p.stances.curStance.GetType().Name.ToLowerInvariant();
                    if (sname.Contains("warmup") || sname.Contains("busy")) return true;
                }
                // current job or driver may indicate duel/animation
                var cj = p.CurJob;
                if (cj != null && cj.def != null)
                {
                    string jn = cj.def.defName.ToLowerInvariant();
                    if (jn.Contains("duel") || jn.Contains("melee") || jn.Contains("animation") || jn.Contains("am_")) return true;
                }
                var drv = p.jobs?.curDriver;
                if (drv != null)
                {
                    string dn = drv.GetType().Name.ToLowerInvariant();
                    if (dn.Contains("duel") || dn.Contains("melee") || dn.Contains("animation")) return true;
                }
            }
            catch {}
            return false;
        }
        private static bool IsEnemyRetreating(Pawn enemy)
        {
            if (enemy == null || enemy.Dead || enemy.Downed) return false;
            if (enemy.CurJobDef?.Is(JobDefOf.Flee) == true) return true;
            PawnDuty duty = enemy.mindState?.duty;
            if (duty != null && (duty.Is(DutyDefOf.ExitMapRandom) || duty.Is(DutyDefOf.TravelOrLeave))) return true;
            return false;
        }

        /// <summary>
        ///     For AutoControl pawns: pick the best focus-fire target among visible enemies.
        ///     Scoring: thrown/explosive weapon (50%) + low HP% (35%) + targeted by allies (15%).
        /// </summary>
        private bool TryGetFocusFireTarget(ulong selFlags, Verb verb, out Thing target)
        {
            target = null;
            if (selPawn.Map == null) return false;
            float bestScore  = -1f;
            Thing bestTarget = null;
            MapComponent_FogGrid fogComp_ff = (Finder.Settings.FogOfWar_Enabled && selPawn.Faction.IsPlayerSafe())
                ? selPawn.Map?.GetComponent<MapComponent_FogGrid>()
                : null;
            IEnumerator<AIEnvAgentInfo> enumerator = data.Enemies();
            while (enumerator.MoveNext())
            {
                AIEnvAgentInfo info = enumerator.Current;
                if (info.thing == null || !info.thing.Spawned) continue;
                if ((sightReader.GetDynamicFriendlyFlags(info.thing.Position) & selFlags) == 0) continue;
                if (fogComp_ff != null && fogComp_ff.IsFogged(info.thing.Position)) continue;
                if (!verb.CanHitTarget(info.thing)) continue;
                Pawn  ep      = info.thing as Pawn;
                float hpScore = ep != null ? 1f - ep.health.summaryHealth.SummaryHealthPercent : 0f;
                // Bonus for enemies using thrown/explosive projectiles (grenades, mortars, etc.)
                float throwScore = 0f;
                if (ep != null)
                {
                    ThingDef proj = ep.CurrentEffectiveVerb?.verbProps?.defaultProjectile;
                    if (proj?.projectile != null && (proj.projectile.flyOverhead || proj.projectile.explosionRadius > 0f))
                    {
                        throwScore = 1f;
                    }
                }
                int   allies  = 0;
                IEnumerator<AIEnvAgentInfo> ae = data.Allies();
                while (ae.MoveNext())
                {
                    if (ae.Current.thing is Pawn a && a.mindState?.enemyTarget == info.thing) allies++;
                }
                float score = throwScore * 0.5f + hpScore * 0.35f + Mathf.Min(allies * 0.15f, 0.45f) * 0.15f;
                if (score > bestScore)
                {
                    bestScore  = score;
                    bestTarget = info.thing;
                }
            }
            if (bestTarget != null) { target = bestTarget; return true; }
            return false;
        }

        /// <summary>
        ///     For AutoControl pawns: find a flanking position to support an ally being attacked.
        ///     Returns a cast position 90 degrees to the side of the attacker relative to the ally.
        /// </summary>
        private bool TryGetFlankTarget(Verb verb, ulong selFlags, out Thing flankTarget, out IntVec3 flankPos)
        {
            flankTarget = null;
            flankPos    = IntVec3.Invalid;
            if (selPawn.Map == null) return false;
            IEnumerator<AIEnvAgentInfo> alliesEnum = data.Allies();
            while (alliesEnum.MoveNext())
            {
                if (!(alliesEnum.Current.thing is Pawn ally)) continue;
                ThingComp_CombatAI allyComp = ally.AI();
                if (allyComp == null) continue;
                List<Thing> attackers = allyComp.data.BeingTargetedBy;
                if (attackers == null || attackers.Count == 0) continue;
                for (int i = 0; i < attackers.Count; i++)
                {
                    Thing attacker = attackers[i];
                    if (attacker == null || !attacker.Spawned || !attacker.HostileTo(selPawn)) continue;
                    if (!verb.CanHitTarget(attacker)) continue;
                    // Perpendicular direction (90° around Y-axis in XZ plane)
                    Vector3 dir  = (attacker.Position - ally.Position).ToVector3().normalized;
                    Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
                    CastPositionRequest req = new CastPositionRequest();
                    req.caster              = selPawn;
                    req.target              = attacker;
                    req.verb                = verb;
                    req.maxRangeFromTarget  = verb.EffectiveRange * 0.9f;
                    req.maxRangeFromCaster  = Mathf.Max(selPawn.DistanceTo_Fast(attacker) * 1.2f, 12f);
                    req.wantCoverFromTarget = true;
                    if (CastPositionFinder.TryFindCastPosition(req, out IntVec3 cell) && cell != selPawn.Position)
                    {
                        flankTarget = attacker;
                        flankPos    = cell;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        ///     Computes a lateral preferred position so attacking pawns spread out instead of
        ///     clustering in the same approach corridor. Used as preferredCastPosition hint.
        ///     Returns null if no spreading is needed (fewer than 2 allies are crowded on one side).
        /// </summary>
        private IntVec3? GetLateralSpreadPos(Thing target)
        {
            if (target == null || selPawn.Map == null) return null;
            IntVec3 selfPos   = selPawn.Position;
            IntVec3 targetPos = target.Position;
            // Approach direction: target → self, in XZ plane
            float dx  = selfPos.x - targetPos.x;
            float dz  = selfPos.z - targetPos.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 2f) return null;
            // Perpendicular unit vector (90° CW in XZ plane)
            float perpX = -dz / len;
            float perpZ =  dx / len;
            // Count allies on each perpendicular side of our approach line
            int leftCount = 0, rightCount = 0;
            IEnumerator<AIEnvAgentInfo> allies = data.Allies();
            while (allies.MoveNext())
            {
                Thing ally = allies.Current.thing;
                if (ally == null || !ally.Spawned || ally == selPawn) continue;
                float side = (ally.Position.x - selfPos.x) * perpX + (ally.Position.z - selfPos.z) * perpZ;
                if (side >  1.5f) rightCount++;
                else if (side < -1.5f) leftCount++;
            }
            if (leftCount + rightCount < 2) return null;
            // Spread toward the less-crowded side
            float sign  = leftCount <= rightCount ? -1f : 1f;
            float dist  = Mathf.Clamp((leftCount + rightCount) * 2.5f, 5f, 14f);
            IntVec3 spread = new IntVec3(
                selfPos.x + Mathf.RoundToInt(perpX * sign * dist),
                selfPos.y,
                selfPos.z + Mathf.RoundToInt(perpZ * sign * dist));
            return spread.InBounds(selPawn.Map) ? spread : (IntVec3?)null;
        }

        /// <summary>
        ///     When AutoControl + Search and Destroy is enabled and CAI detects no enemies in its
        ///     current sight coverage, use the map's full attack-target cache to find and pursue
        ///     enemies that are outside CAI's sight range.
        /// </summary>
        /// <returns>True if a pursuit job was issued.</returns>
        private bool TrySearchAndDestroy()
        {
            bool isMelee = selPawn.CurrentEffectiveVerb?.IsMeleeAttack ?? (selPawn.equipment?.Primary == null || selPawn.equipment.Primary.def.IsMeleeWeapon);
            if (!IsAIAutoControlled || !(isMelee ? aiSearchAndDestroyMelee : aiSearchAndDestroy)) return false;
            if (IsPlayerOverriding) return false;
            Map map = selPawn.Map;
            if (map == null) return false;
            float bodySize = selPawn.RaceProps.baseBodySize;
            if (data.InterruptedRecently((int)(30 * bodySize))) return false;

            MapComponent_FogGrid fogComp = Finder.Settings.FogOfWar_Enabled
                ? map.GetComponent<MapComponent_FogGrid>()
                : null;

            var   potentialTargets = map.attackTargetsCache.GetPotentialTargetsFor(selPawn);
            Thing bestTarget       = null;
            float bestDistSqr      = float.MaxValue;
            foreach (IAttackTarget attackTarget in potentialTargets)
            {
                if (attackTarget.ThreatDisabled(selPawn)) continue;
                if (!AttackTargetFinder.IsAutoTargetable(attackTarget)) continue;
                Thing t = attackTarget.Thing;
                if (t == null || !t.Spawned) continue;
                if (t is Pawn tp && (tp.Dead || tp.Downed)) continue;

                if (fogComp != null && fogComp.IsFogged(t.Position)) continue;

                float distSqr = t.Position.DistanceToSquared(selPawn.Position);
                if (distSqr >= bestDistSqr) continue;
                if (!selPawn.CanReach(t, PathEndMode.OnCell, Danger.Deadly)) continue;
                bestTarget  = t;
                bestDistSqr = distSqr;
            }
            if (bestTarget == null || IsPerformingMeleeAnimation(selPawn)) return false;

            // For mechanoids: if the target is outside command range, walk to the nearest
            // in-range cell on the boundary facing the target instead of the target itself.
            LocalTargetInfo gotoTarget = new LocalTargetInfo(bestTarget);
            if (selPawn.RaceProps.IsMechanoid
                && !MechanitorUtility.InMechanitorCommandRange(selPawn, new LocalTargetInfo(bestTarget.Position)))
            {
                IntVec3 mechPos   = selPawn.Position;
                IntVec3 targetPos = bestTarget.Position;
                Vector3 dir       = (targetPos - mechPos).ToVector3();
                float   totalDist = dir.magnitude;
                if (totalDist > 0.01f)
                {
                    dir /= totalDist;
                    IntVec3 boundaryCell = mechPos;
                    for (int step = 1; step <= (int)totalDist + 1; step++)
                    {
                        IntVec3 candidate = (mechPos.ToVector3() + dir * step).ToIntVec3();
                        if (!candidate.InBounds(selPawn.Map)) break;
                        if (!MechanitorUtility.InMechanitorCommandRange(selPawn, new LocalTargetInfo(candidate))) break;
                        boundaryCell = candidate;
                    }
                    gotoTarget = new LocalTargetInfo(boundaryCell);
                }
            }

            Job gotoJob = JobMaker.MakeJob(JobDefOf.Goto, gotoTarget);
            gotoJob.expiryInterval        = 60;
            gotoJob.checkOverrideOnExpire = true;
            gotoJob.collideWithPawns      = true;
            gotoJob.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
            selPawn.jobs.ClearQueuedJobs();
            selPawn.jobs.StartJob(gotoJob, JobCondition.InterruptForced);
            data.LastInterrupted = GenTicks.TicksGame;
            return true;
        }

        /// <summary>
        ///     Detect nearby fire, toxic/blinding gas, and incoming overhead/explosive
        ///     projectiles, then retreat to a safe position.
        /// </summary>
        private bool TryEvadeAreaThreats(Verb verb, ref int progress)
        {

            bool isEnemy = selPawn.Faction == null || !selPawn.Faction.IsPlayerSafe();
            if (!IsAIAutoControlled && !isEnemy) return false;
            Map     map    = selPawn.Map;
            if (map == null) return false;
            IntVec3 selPos = selPawn.Position;
            bool    needsEvade   = false;
            IntVec3 threatCenter = IntVec3.Invalid;
            // How far the pawn needs to move to escape the threat.
            // Kept small so the pawn stays in the fight instead of fleeing across the map.
            float   evadeRadius  = 0f;

            if (!needsEvade)
            {
                if (selPos.GasDensity(map, GasType.ToxGas) > 30 || selPos.GasDensity(map, GasType.BlindSmoke) > 50)
                {
                    needsEvade   = true;
                    threatCenter = selPos;
                    evadeRadius  = 6f;  // just clear the gas cloud
                }
            }

            if (!needsEvade)
            {
                List<Thing> fires = map.listerThings.ThingsInGroup(ThingRequestGroup.Fire);
                for (int fi = 0; fi < fires.Count; fi++)
                {
                    if (fires[fi].Position.DistanceToSquared(selPos) <= 9f)
                    {
                        needsEvade   = true;
                        threatCenter = fires[fi].Position;
                        evadeRadius  = 5f;  // step out of the fire
                        break;
                    }
                }
            }

            if (!needsEvade)
            {
                List<Thing> projectiles = map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile);
                for (int pi = 0; pi < projectiles.Count; pi++)
                {
                    if (!(projectiles[pi] is Projectile proj)) continue;
                    // Ignore friendly projectiles
                    if (proj.Launcher?.Faction != null && !proj.Launcher.Faction.HostileTo(selPawn.Faction)) continue;
                    ThingDef pd = proj.def;
                    if (pd?.projectile == null) continue;
                    if (!pd.projectile.flyOverhead && pd.projectile.explosionRadius <= 0f) continue;
                    float dangerRadius = pd.projectile.explosionRadius + 4f;
                    // Use intendedTarget then usedTarget to find the landing zone
                    LocalTargetInfo lti = proj.intendedTarget.IsValid ? proj.intendedTarget : proj.usedTarget;
                    if (!lti.IsValid) continue;
                    IntVec3 lz = lti.HasThing ? lti.Thing.Position : lti.Cell;
                    if (lz.DistanceTo(selPos) < dangerRadius)
                    {
                        needsEvade   = true;
                        threatCenter = lz;
                        evadeRadius  = dangerRadius + 2f;  // clear the blast radius with a small margin
                        break;
                    }
                }
            }

            if (!needsEvade) return false;
            progress = 95;
            CoverPositionRequest request = new CoverPositionRequest();
            request.caster             = selPawn;
            request.verb               = verb;
            request.target             = threatCenter.IsValid ? (LocalTargetInfo)threatCenter : LocalTargetInfo.Invalid;
            request.maxRangeFromCaster = Mathf.Clamp(evadeRadius + 2f, 5f, 10f);
            request.checkBlockChance   = false;
            if (rangedEnemiesTargetingSelf.Count > 0)
            {
                request.majorThreats = new List<Thing>(rangedEnemiesTargetingSelf);
            }
            if (CoverPositionFinder.TryFindRetreatPosition(request, out IntVec3 safeCell) && safeCell != selPos
                && IsMechanoidDestValid(safeCell))
            {
                _last = 90;
                Job job_goto = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Retreat, safeCell);
                job_goto.expiryInterval        = -1;
                job_goto.checkOverrideOnExpire = false;
                job_goto.playerForced          = forcedTarget.IsValid;
                job_goto.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
                if (!IsPerformingMeleeAnimation(selPawn))
                {
                    selPawn.jobs.ClearQueuedJobs();
                    selPawn.jobs.StartJob(job_goto, JobCondition.InterruptForced);
                    data.LastRetreated = GenTicks.TicksGame;
                }
                return true;
            }
            return false;
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            selPawn = parent as Pawn;
            if (selPawn == null)
            {
                throw new Exception($"ThingComp_CombatAI initialized for a non pawn {parent}/def:{parent.def}");
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            base.PostDeSpawn(map, mode);
            allAllies.Clear();
            allEnemies.Clear();
            escorts.Clear();
            rangedEnemiesTargetingSelf.Clear();
            sapperNodes.Clear();
            aggroTarget = LocalTargetInfo.Invalid;
            data?.PostDeSpawn();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            armor          =   selPawn.GetArmorReport();
            duties         ??= new Pawn_CustomDutyTracker(selPawn);
            duties.pawn    =   selPawn;
            abilities      ??= new Pawn_AbilityCaster(selPawn);
            abilities.pawn =   selPawn;
            data.LastInterrupted = GenTicks.TicksGame;
        }

#if DEBUG_REACTION
        private static List<Thing> _buffer = new List<Thing>(16);
        public override void CompTick()
        {
            base.CompTick();
            if (selPawn.IsHashIntervalTick(15) && Find.Selector.SelectedPawns.Contains(selPawn))
            {
                List<Thing> buffer = _buffer;
                buffer.Clear();
                sightReader.GetEnemies(selPawn.Position, buffer);
                foreach (var thing in buffer)
                {
                    selPawn.Map.debugDrawer.FlashCell(thing.Position, 0.01f, "H", 15);
                }
                buffer.Clear();
                sightReader.GetFriendlies(selPawn.Position, buffer);
                foreach (var thing in buffer)
                {
                    selPawn.Map.debugDrawer.FlashCell(thing.Position, 0.99f, "F", 15);
                }
                buffer.Clear();
            }
        }
#endif

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!selPawn.Spawned)
            {
                return;
            }
            if (IsDeadOrDowned)
            {
                if (IsSapping || escorts.Count > 0)
                {
                    ReleaseEscorts(false);
                    sapperNodes.Clear();
                    cellBefore      = IntVec3.Invalid;
                    sapperStartTick = -1;
                }
                return;
            }
            if (selPawn.IsBurning_Fast())
            {
                return;
            }
            // Clear Search and Destroy flags when the pawn is no longer drafted.
            if (!selPawn.Drafted)
            {
                aiSearchAndDestroy       = false;
                aiSearchAndDestroyMelee  = false;
            }
            if (aggroTicks > 0)
            {
                aggroTicks -= GenTicks.TickRareInterval;
                if (aggroTicks <= 0)
                {
                    if (aggroTarget.IsValid)
                    {
                        TryAggro(aggroTarget, 0.8f, Rand.Int);
                    }
                    aggroTarget = LocalTargetInfo.Invalid;
                }

            }
            if (duties != null)
            {
                duties.TickRare();
            }
            // Periodically return out-of-range CAI-controlled friendly mechanoids to their
            // mechanitor's command zone (handles the no-enemies-in-sight case).
            TryIssueMechanoidReturnToRange();
            if (abilities != null)
            {
                // abilities.TickRare(visibleEnemies);
            }
            if (selPawn.IsApproachingMeleeTarget(out Thing target))
            {
                ThingComp_CombatAI comp = target.GetComp_Fast<ThingComp_CombatAI>();
                ;
                if (comp != null)
                {
                    comp.Notify_BeingTargeted(selPawn, selPawn.CurrentEffectiveVerb);
                }
            }
            if (IsSapping)
            {
                // end if this pawn is in
                if (sapperNodes[0].GetEdifice(parent.Map) == null)
                {
                    cellBefore = sapperNodes[0];
                    sapperNodes.RemoveAt(0);
                    if (sapperNodes.Count > 0)
                    {
                        _sap++;
                        TryStartSapperJob();
                    }
                    else
                    {
                        ReleaseEscorts(success: true);
                        sapperNodes.Clear();
                        cellBefore      = IntVec3.Invalid;
                        sapperStartTick = -1;
                    }
                }
                else
                {
                    TryStartSapperJob();
                }
            }
            if (forcedTarget.IsValid)
            {
                if (Mod_CE.active && (selPawn.CurJobDef.Is(Mod_CE.ReloadWeapon) || selPawn.CurJobDef.Is(Mod_CE.HunkerDown)))
                {
                    return;
                }
                // remove the forced target on when not drafted and near the target
                if (!selPawn.Drafted || selPawn.Position.DistanceToSquared(forcedTarget.Cell) < 25)
                {
                    forcedTarget = LocalTargetInfo.Invalid;
                }
                else if (enemiesInRangeNum == 0 && (selPawn.jobs.curJob?.def.Is(JobDefOf.Goto) == false || selPawn.pather?.Destination != forcedTarget.Cell))
                {
                    Job gotoJob = JobMaker.MakeJob(JobDefOf.Goto, forcedTarget);
                    gotoJob.canUseRangedWeapon    = true;
                    gotoJob.checkOverrideOnExpire = false;
                    gotoJob.locomotionUrgency     = LocomotionUrgency.Jog;
                    gotoJob.playerForced          = true;
                    if (!IsPerformingMeleeAnimation(selPawn))
                    {
                        selPawn.jobs.ClearQueuedJobs();
                        selPawn.jobs.StartJob(gotoJob);
                    }
                }
            }
        }

        public override void CompTickLong()
        {
            base.CompTickLong();
            // update the current armor report.
            armor = selPawn.GetArmorReport();
        }

        /// <summary>
        ///     Returns whether the parent has took damage in the last number of ticks.
        /// </summary>
        public bool TookDamageRecently(int ticks)
        {
            return data.TookDamageRecently(ticks);
        }
        /// <summary>
        ///     Returns whether the parent has reacted in the last number of ticks.
        /// </summary>
        public bool ReactedRecently(int ticks)
        {
            return data.InterruptedRecently(ticks);
        }

        /// <summary>
        ///     Called when a scan for enemies starts. Will clear the visible enemy queue. If not called, calling OnScanFinished or
        ///     Notify_VisibleEnemy(s) will result in an error.
        ///     Should only be called from the main thread.
        /// </summary>
        public void OnScanStarted()
        {
            if (allEnemies.Count != 0)
            {
                if (scanning)
                {
                    Log.Warning($"ISMA: OnScanStarted called while scanning. ({allEnemies.Count}, {Thread.CurrentThread.ManagedThreadId})");
                    return;
                }
                allEnemies.Clear();
            }
            if (allAllies.Count != 0)
            {
                allAllies.Clear();
            }
            scanning         = true;
            data.LastScanned = lastScanned = GenTicks.TicksGame;
        }

        /// <summary>
        ///     Called a scan is finished. This will process enemies queued in visibleEnemies. Responsible for parent reacting.
        ///     If OnScanStarted is not called before then this will result in an error.
        ///     Should only be called from the main thread.
        /// </summary>
        public void OnScanFinished(ref int progress)
        {
            if (scanning == false)
            {
                Log.Warning($"ISMA: OnScanFinished called while not scanning. ({allEnemies.Count}, {Thread.CurrentThread.ManagedThreadId})");
                return;
            }
            scanning = false;
            // set enemies.
            data.ReSetEnemies(allEnemies);
            // set allies.
            data.ReSetAllies(allAllies);
            // update when this pawn last saw enemies
            data.LastSawEnemies = data.NumEnemies > 0 ? GenTicks.TicksGame : -1;
            //
            var settings = parent is Pawn ? Finder.Settings.GetDefKindSettings(parent as Pawn) :  Finder.Settings.GetDefKindSettings(parent.def, null);
            // For debugging and logging.
            progress = 1;
            // skip for animals.
            if (selPawn.mindState == null || selPawn.RaceProps.Animal || IsDeadOrDowned)
            {
                return;
            }
            // skip for player pawns with no forced target.
            if (selPawn.Faction.IsPlayerSafe() && !forcedTarget.IsValid && !IsAIAutoControlled)
            {
                return;
            }
            // if the pawn is burning don't react.
            if (selPawn.IsBurning_Fast())
            {
                return;
            }
            if (TryIssueMechanoidReturnToRange()) return;
            ReactDebug_Internel(out progress);
            progress = 3;
            List<Thing> targetedBy = data.BeingTargetedBy;
            data.LastSawEnemies = data.NumEnemies > 0 ? GenTicks.TicksGame : data.LastSawEnemies;
            // if no enemies are visible nor anyone targeting self skip.
            if (data.NumEnemies == 0 && targetedBy.Count == 0)
            {
                // Search and Destroy: pursue enemies outside CAI's current sight coverage.
                if (IsAIAutoControlled && (aiSearchAndDestroy || aiSearchAndDestroyMelee))
                {
                    if (!TrySearchAndDestroy())
                    {
                        selPawn.jobs?.ClearQueuedJobs();
                    }
                }
                return;
            }
            // For debugging and logging.
            progress = 4;
            // check if the TPS is good enough.
            // reduce cooldown if the pawn hasn't seen enemies for a few ticks
            if (!Finder.Performance.TpsCriticallyLow)
            {
                if (GenTicks.TicksGame - lastSawEnemies > 90)
                {
                    lastInterupted = -1;
                    if (Finder.Settings.Debug && Finder.Settings.Debug_ValidateSight)
                    {
                        parent.Map.debugDrawer.FlashCell(parent.Position, 1.0f, "X", 60);
                    }
                }
                lastSawEnemies = GenTicks.TicksGame;
            }
            // For debugging and logging.
            progress = 5;
            // get body size and use it in cooldown math.
            float bodySize = selPawn.RaceProps.baseBodySize;
            // pawn reaction cooldown changes with their body size.
            if (data.InterruptedRecently((int)(45 * bodySize)) || data.RetreatedRecently((int)(120 * bodySize)))
            {
                return;
            }
            // if the pawn is kidnapping a pawn skip.
            if (selPawn.CurJobDef.Is(JobDefOf.Kidnap) || selPawn.CurJobDef.Is(JobDefOf.Flee))
            {
                return;
            }
            // if the pawn is sapping, stop sapping.
            if (selPawn.CurJobDef.Is(JobDefOf.Mine) && sightReader.GetVisibilityToEnemies(selPawn.Position) > 0)
            {
                selPawn.jobs.StopAll();
            }
            // For debugging and logging.
            progress = 6;
            // Skip if some vanilla duties are active.
            PawnDuty duty = selPawn.mindState.duty;
            if (duty.Is(DutyDefOf.Build) || duty.Is(DutyDefOf.SleepForever) || duty.Is(DutyDefOf.TravelOrLeave))
            {
                data.LastInterrupted = GenTicks.TicksGame + Rand.Int % 240;
                return;
            }
            PersonalityTacker.PersonalityResult personality      = parent.GetCombatPersonality();
            IntVec3                             selPos           = selPawn.Position;
            // used to update nearest enemy THing
            // For debugging and logging.
            progress = 7;
            // check if the chance of survivability is high enough
            // defensive actions
            Verb verb = selPawn.CurrentEffectiveVerb;
            if (verb is { WarmupStance.ticksLeft: < 40 })
            {
                return;
            }
            // For debugging and logging.
            progress = 8;
            rangedEnemiesTargetingSelf.Clear();
            // Calc the current threat 
            ThreatUtility.CalculateThreat(selPawn, targetedBy, armor, rangedEnemiesTargetingSelf, null, out float possibleDmg, out float possibleDmgDistance, out float possibleDmgWarmup, out Thing nearestEnemy, out float nearestEnemyDist, out Pawn nearestMeleeEnemy, out float nearestMeleeEnemyDist, ref progress);
            // Respect player commands: if the player directly commanded this pawn (move/attack order),
            // treat it as highest priority and skip all AI-initiated reactions.
            if (IsPlayerOverriding)
            {
                return;
            }
            // Try retreat
            if ((settings?.Retreat_Enabled ?? true) && (bodySize < 2 || selPawn.RaceProps.Humanlike))
            {
                // For debugging and logging.
                progress = 9;
                if (TryStartRetreat(possibleDmg, possibleDmgWarmup, possibleDmgDistance, personality, nearestMeleeEnemy, nearestMeleeEnemyDist, ref progress))
                {
                    return;
                }
                // Dodge fire, gas, and incoming thrown/explosive projectiles
                // (AutoControl player pawns and all enemy/neutral AI pawns).
                if (TryEvadeAreaThreats(verb, ref progress))
                {
                    return;
                }
            }
            // For debugging and logging.
            progress = 100;
            if (duty.Is(DutyDefOf.ExitMapRandom))
            {
                return;
            }
            // For debugging and logging.
            progress = 200;
            // offensive actions
            if (verb != null)
            {
                bool isUnarmedAutoControl = IsAIAutoControlled && selPawn.equipment?.Primary == null;
                if (isUnarmedAutoControl) return;

                // if the pawn is retreating and the pawn is still in danger or recently took damage, skip any offensive reaction.
                if (verb.IsMeleeAttack)
                {
                    TryMeleeReaction(possibleDmg, ref progress);
                }
                // ranged
                else
                {
                    TryRangedReaction(verb, selPos, personality, bodySize, duty, ref progress);
                }
            }
        }
        private void TryRangedReaction(Verb verb, IntVec3 selPos, PersonalityTacker.PersonalityResult personality, float bodySize, PawnDuty duty, ref int progress)
        {
                Thing nearestEnemy          = null;
            float nearestEnemyDist      = 1e6f;
            Pawn  nearestMeleeEnemy     = null;
            float nearestMeleeEnemyDist = 1e6f;
            // For debugging and logging.
            progress = 208;
                if (selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Cover) && GenTicks.TicksGame - selPawn.CurJob.startTick < 60)
                {
                    return;
                }
            // if CE is active skip reaction if the pawn is hunkering down.
            if (Mod_CE.active && selPawn.CurJobDef.Is(Mod_CE.HunkerDown))
            {
                return;
            }
            if (Mod_CE.active && selPawn.CurJobDef.Is(Mod_CE.ReloadWeapon))
            {
                if (rangedEnemiesTargetingSelf.Count > 0 && GenTicks.TicksGame - lastCEReloadCoverTick > 500)
                {
                    CoverPositionRequest request = new CoverPositionRequest();
                    request.caster             = selPawn;
                    request.target             = rangedEnemiesTargetingSelf[0];
                    request.verb               = verb;
                    request.majorThreats       = new List<Thing>(rangedEnemiesTargetingSelf);
                    request.checkBlockChance   = true;
                    request.maxRangeFromCaster = Mathf.Clamp(verb.EffectiveRange * 0.3f, 6f, 14f);
                    IntVec3 cell  = default;
                    bool    found = false;
                    try { found = CoverPositionFinder.TryFindCoverPosition(request, out cell); }
                    catch (System.ArgumentOutOfRangeException) { cell = default; found = false; }
                    if (found && cell != selPos && IsMechanoidDestValid(cell) && !IsPerformingMeleeAnimation(selPawn))
                    {
                        _bestEnemy            = rangedEnemiesTargetingSelf[0];
                        lastCEReloadCoverTick = GenTicks.TicksGame;
                        Job job_goto = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Cover, cell);
                        job_goto.expiryInterval        = -1;
                        job_goto.checkOverrideOnExpire = false;
                        job_goto.playerForced          = forcedTarget.IsValid;
                        job_goto.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
                        // Queue Wait_Combat after arrival so CE's reload ThinkNode fires when
                        // the pawn tries to shoot and finds the weapon still needs ammo.
                        Job job_wait = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 150 + 200);
                        job_wait.playerForced                   = forcedTarget.IsValid;
                        job_wait.endIfCantShootTargetFromCurPos = true;
                        job_wait.checkOverrideOnExpire          = true;
                        selPawn.jobs.ClearQueuedJobs();
                        selPawn.jobs.StartJob(job_goto, JobCondition.InterruptForced);
                        selPawn.jobs.jobQueue.EnqueueFirst(job_wait);
                    }
                }
                return;
            }
            // For debugging and logging.
            progress = 209;
            // check if the verb is available.
            // If Combat Extended is aiming, still allow player-controlled pawns to proceed
            if (!verb.Available() || (Mod_CE.active && Mod_CE.IsAimingCE(verb) && !selPawn.Faction.IsPlayerSafe()))
            {
                return;
            }
            bool  bestEnemyVisibleNow  = false;
            bool  bestEnemyVisibleSoon = false;
            ulong selFlags             = selPawn.GetThingFlags();
            // For player-side pawns under fog of war, resolve the fog grid so we can gate
            // attacks on enemies whose positions haven't been revealed yet.
            MapComponent_FogGrid fogComp_ranged = (Finder.Settings.FogOfWar_Enabled && selPawn.Faction.IsPlayerSafe())
                ? selPawn.Map?.GetComponent<MapComponent_FogGrid>()
                : null;
            // For debugging and logging.
            progress = 210;
            // A not fast check will check for retreat and for reactions to enemies that are visible or soon to be visible.
            // A fast check will check only for retreat.
            IEnumerator<AIEnvAgentInfo> enumerator = data.Enemies();
            while (enumerator.MoveNext())
            {
                progress = 301;
                AIEnvAgentInfo info = enumerator.Current;
#if DEBUG_REACTION
                if (info.thing == null)
                {
                    Log.Error("Found null thing (3)");
                    continue;
                }
#endif
                // For debugging and logging.
                progress = 302;
                if (info.thing.Spawned)
                {
                    Pawn enemyPawn = info.thing as Pawn;
                    if ((sightReader.GetDynamicFriendlyFlags(info.thing.Position) & selFlags) != 0 && verb.CanHitTarget(info.thing)
                        && (fogComp_ranged == null || !fogComp_ranged.IsFogged(info.thing.Position)))
                    {
                        // For debugging and logging.
                        progress = 311;
                        if (!bestEnemyVisibleNow)
                        {
                            nearestEnemy        = null;
                            nearestEnemyDist    = 1e4f;
                            bestEnemyVisibleNow = true;
                        }
                        float dist = selPawn.DistanceTo_Fast(info.thing);
                        if (dist < nearestEnemyDist)
                        {
                            nearestEnemyDist = dist;
                            nearestEnemy     = info.thing;
                        }
                    }
                    else if (enemyPawn != null && !bestEnemyVisibleNow)
                    {
                        // For debugging and logging.
                        progress = 312;
                        IntVec3 temp = PawnPathUtility.GetMovingShiftedPosition(enemyPawn, 120);
                        if ((sightReader.GetDynamicFriendlyFlags(temp) & selFlags) != 0 && verb.CanHitTarget(temp)
                            && (fogComp_ranged == null || !fogComp_ranged.IsFogged(temp)))
                        {
                            if (!bestEnemyVisibleSoon)
                            {
                                nearestEnemy         = null;
                                nearestEnemyDist     = 1e4f;
                                bestEnemyVisibleSoon = true;
                            }
                            float dist = selPawn.DistanceTo_Fast(info.thing);
                            if (dist < nearestEnemyDist)
                            {
                                nearestEnemyDist = dist;
                                nearestEnemy     = info.thing;
                            }
                        }
                        else if (!bestEnemyVisibleSoon)
                        {
                            float dist = selPawn.DistanceTo_Fast(info.thing);
                            if (dist < nearestEnemyDist)
                            {
                                nearestEnemyDist = dist;
                                nearestEnemy     = info.thing;
                            }
                        }
                    }
                    // For debugging and logging.
                    progress = 303;
                    if (enemyPawn != null && (enemyPawn.CurrentEffectiveVerb?.IsMeleeAttack ?? false))
                    {
                        float dist = selPos.DistanceTo_Fast(PawnPathUtility.GetMovingShiftedPosition(enemyPawn, 120f));
                        if (dist < nearestMeleeEnemyDist)
                        {
                            nearestMeleeEnemyDist = dist;
                            nearestMeleeEnemy     = enemyPawn;
                        }
                    }
                }
            }
            progress = 400;
            void StartOrQueueCoverJob(IntVec3 cell, int codeOffset)
            {
                // Don't move CAI-controlled friendly mechanoids outside their mechanitor's command range
                if (!IsMechanoidDestValid(cell)) return;
                Job curJob = selPawn.CurJob;
                if (curJob != null && curJob.Is(JobDefOf.AttackMelee))
                {
                    Job job_goto_delayed = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Cover, cell);
                    job_goto_delayed.playerForced = forcedTarget.IsValid;
                    job_goto_delayed.expiryInterval = -1;
                    job_goto_delayed.checkOverrideOnExpire = false;
                    selPawn.jobs.jobQueue.EnqueueFirst(job_goto_delayed);
                    return;
                }
                if (curJob != null && (curJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Cover) || curJob.Is(JobDefOf.Goto)) && cell == curJob.targetA.Cell)
                {
                    if (selPawn.jobs.jobQueue.Count == 0 || !selPawn.jobs.jobQueue[0].job.Is(JobDefOf.Wait_Combat))
                    {
                        Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 150 + 200);
                        job_waitCombat.targetA                        = nearestEnemy;
                        job_waitCombat.playerForced                   = forcedTarget.IsValid;
                        job_waitCombat.endIfCantShootTargetFromCurPos = true;
                        job_waitCombat.checkOverrideOnExpire          = true;
                        selPawn.jobs.ClearQueuedJobs();
                        selPawn.jobs.jobQueue.EnqueueFirst(job_waitCombat);
                    }
                }
                else if (cell == selPawn.Position)
                {
                    if (!selPawn.CurJob.Is(JobDefOf.Wait_Combat))
                    {
                        Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 150 + 200);
                        job_waitCombat.targetA                        = nearestEnemy;
                        job_waitCombat.playerForced                   = forcedTarget.IsValid;
                        job_waitCombat.endIfCantShootTargetFromCurPos = true;
                        job_waitCombat.checkOverrideOnExpire          = true;
                        if (!IsPerformingMeleeAnimation(selPawn))
                        {
                            selPawn.jobs.StartJob(job_waitCombat, JobCondition.InterruptForced);
                        }
                    }
                }
                else if (selPawn.CurJob.Is(JobDefOf.Wait_Combat))
                {
                    _last = 50 + codeOffset;
                    Job job_goto = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Cover, cell);
                    job_goto.playerForced          = forcedTarget.IsValid;
                    job_goto.expiryInterval        = -1;
                    job_goto.checkOverrideOnExpire = false;
                    job_goto.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
                    Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 150 + 200);
                    job_waitCombat.targetA                        = nearestEnemy;
                    job_waitCombat.playerForced                   = forcedTarget.IsValid;
                    job_waitCombat.endIfCantShootTargetFromCurPos = true;
                    job_waitCombat.checkOverrideOnExpire          = true;
                    selPawn.jobs.ClearQueuedJobs();
                    selPawn.jobs.jobQueue.EnqueueFirst(job_waitCombat);
                    selPawn.jobs.jobQueue.EnqueueFirst(job_goto);
                }
                else
                {
                    _last                         = 51 + codeOffset;
                    selPawn.mindState.enemyTarget = nearestEnemy;
                    Job job_goto = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Cover, cell);
                    job_goto.expiryInterval        = -1;
                    job_goto.checkOverrideOnExpire = false;
                    job_goto.playerForced          = forcedTarget.IsValid;
                    job_goto.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
                    Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 150 + 200);
                    job_waitCombat.playerForced                   = forcedTarget.IsValid;
                    job_waitCombat.endIfCantShootTargetFromCurPos = true;
                    job_waitCombat.checkOverrideOnExpire          = true;
                    selPawn.jobs.ClearQueuedJobs();
                    if (!IsPerformingMeleeAnimation(selPawn))
                    {
                        selPawn.jobs.StartJob(job_goto, JobCondition.InterruptForced);
                    }
                    selPawn.jobs.jobQueue.EnqueueFirst(job_waitCombat);
                }
                data.LastInterrupted = GenTicks.TicksGame;
            }

            void StartWaitCombatJob(int lastCode)
            {
                _last = lastCode;
                Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 100 + 100);
                job_waitCombat.playerForced                   = forcedTarget.IsValid;
                job_waitCombat.endIfCantShootTargetFromCurPos = true;
                if (!IsPerformingMeleeAnimation(selPawn))
                {
                    selPawn.jobs.ClearQueuedJobs();
                    selPawn.jobs.StartJob(job_waitCombat, JobCondition.InterruptForced);
                    data.LastInterrupted = GenTicks.TicksGame;
                }
            }

            void TryFlankOrChase()
            {
                if (!Finder.Settings.Flank_Enabled)
                    return;
                if (TryGetFlankTarget(verb, selFlags, out Thing flankTarget, out IntVec3 flankCell))
                {
                    _bestEnemy = flankTarget;
                    _last      = 81;
                    StartOrQueueCoverJob(flankCell, 40);
                }
                else if (nearestEnemy != null)
                {
                    _bestEnemy = nearestEnemy;
                    bool enemyRetreating = IsEnemyRetreating(nearestEnemy as Pawn);
                    // Chase if: no one is shooting at us, OR the target is already fleeing
                    if ((rangedEnemiesTargetingSelf.Count == 0 || enemyRetreating) &&
                        (sightReader.GetDynamicFriendlyFlags(nearestEnemy.Position) & selFlags) != 0)
                    {
                        float maxRange = enemyRetreating
                            ? Mathf.Min(nearestEnemyDist * 0.8f, verb.EffectiveRange * personality.cover)
                            : Mathf.Min(nearestEnemyDist, verb.EffectiveRange * personality.cover);
                        CastPositionRequest request = new CastPositionRequest();
                        request.caster              = selPawn;
                        request.target              = nearestEnemy;
                        request.maxRangeFromTarget  = verb.EffectiveRange * 0.9f;
                        request.verb                = verb;
                        request.maxRangeFromCaster  = maxRange;
                        request.wantCoverFromTarget = true;
                        if (CastPositionFinder.TryFindCastPosition(request, out IntVec3 cell) && cell != selPos)
                        {
                            _last = 80;
                            StartOrQueueCoverJob(cell, 30);
                        }
                    }
                }
            }

            if (nearestEnemy != null && rangedEnemiesTargetingSelf.Contains(nearestEnemy))
            {
                rangedEnemiesTargetingSelf.Remove(nearestEnemy);
            }
            // For AutoControl pawns: override nearestEnemy with the best focus-fire target.
            if (IsAIAutoControlled && TryGetFocusFireTarget(selFlags, verb, out Thing focusTarget))
            {
                nearestEnemy        = focusTarget;
                nearestEnemyDist    = selPawn.DistanceTo_Fast(focusTarget);
                bestEnemyVisibleNow = true;
            }
            progress = 500;
            // For AI auto-controlled pawns, don't retreat from enemies that are already fleeing/retreating.
            bool meleeThreatRetreating = IsAIAutoControlled && IsEnemyRetreating(nearestMeleeEnemy);
            bool rangedThreatRetreating = IsAIAutoControlled && IsEnemyRetreating(nearestEnemy as Pawn);
            bool retreatMeleeThreat = !meleeThreatRetreating && nearestMeleeEnemy != null && verb.EffectiveRange * personality.retreat > 16 && nearestMeleeEnemyDist < Maths.Max(verb.EffectiveRange * personality.retreat / 3f, 9) && 0.25f * data.NumAllies < data.NumEnemies;
            bool retreatThreat      = !rangedThreatRetreating && !retreatMeleeThreat && nearestEnemy != null && nearestEnemyDist < Maths.Max(verb.EffectiveRange * personality.retreat / 4f, 5);
            _bestEnemy = retreatMeleeThreat ? nearestMeleeEnemy : nearestEnemy;
            // retreat because of a close melee threat
            if (bodySize < 2.0f && (retreatThreat || retreatMeleeThreat))
            {
                progress   = 501;
                _bestEnemy = retreatThreat ? nearestEnemy : nearestMeleeEnemy;
                _last      = 40;
                CoverPositionRequest request = new CoverPositionRequest();
                request.caster             = selPawn;
                request.target             = nearestMeleeEnemy;
                request.verb               = verb;
                request.majorThreats       = new List<Thing>(rangedEnemiesTargetingSelf);
                request.checkBlockChance   = true;
                request.maxRangeFromCaster = Mathf.Clamp(verb.EffectiveRange * 0.3f, 8f, 12f);
                IntVec3 cell = default(IntVec3);
                bool found = false;
                try
                {
                    found = CoverPositionFinder.TryFindRetreatPosition(request, out cell);
                }
                catch (System.ArgumentOutOfRangeException ex)
                {
                    int rangedCount = -1;
                    try { rangedCount = rangedEnemiesTargetingSelf?.Count ?? -1; } catch { }
                    Log.Error($"CoverPositionFinder.TryFindRetreatPosition threw IndexOutOfRange for {selPawn} progress:{progress} rangedEnemiesTargetingSelf.Count={rangedCount} \n{ex}");
                    found = false;
                }
                if (found)
                {
                    if (cell != selPos && IsMechanoidDestValid(cell))
                    {
                        _last = 41;
                        Job job_goto = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Retreat, cell);
                        job_goto.expiryInterval        = -1;
                        job_goto.checkOverrideOnExpire = false;
                        job_goto.playerForced          = forcedTarget.IsValid;
                        job_goto.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
                        if (!IsPerformingMeleeAnimation(selPawn))
                        {
                            selPawn.jobs.ClearQueuedJobs();
                            selPawn.jobs.StartJob(job_goto, JobCondition.InterruptForced);
                        }
                        data.LastRetreated = GenTicks.TicksGame;
                    }
                }
            }
            // best enemy is insight
            else if (nearestEnemy != null)
            {
                progress   = 502;
                _bestEnemy = nearestEnemy;
                if (!selPawn.RaceProps.Humanlike || bodySize > 2.0f)
                {
                    progress = 511;
                    if (bodySize > 2.0f)
                    {
                        // Large mechanicals: hold position and shoot only, no repositioning.
                        if (bestEnemyVisibleNow)
                        {
                            if (selPawn.mindState.enemyTarget == null)
                            {
                                selPawn.mindState.enemyTarget = nearestEnemy;
                            }
                            if (ShouldShootNow())
                            {
                                StartWaitCombatJob(52);
                            }
                        }
                    }
                    else
                    {
                        if (bestEnemyVisibleNow)
                        {
                            if (selPawn.mindState.enemyTarget == null)
                            {
                                selPawn.mindState.enemyTarget = nearestEnemy;
                            }
                            // Try to find an optimal cast/cover position before shooting.
                            if (nearestEnemyDist > 6 * personality.cover)
                            {
                                CastPositionRequest request = new CastPositionRequest();
                                request.caster                = selPawn;
                                request.target                = nearestEnemy;
                                request.maxRangeFromTarget    = 9999;
                                request.verb                  = verb;
                                request.maxRangeFromCaster    = (Maths.Max(Maths.Min(verb.EffectiveRange, nearestEnemyDist) / 2f, 10f) * personality.cover) * Finder.P50;
                                request.wantCoverFromTarget   = true;
                                request.preferredCastPosition = !IsAIAutoControlled && Finder.Settings.LateralSpread_Enabled ? GetLateralSpreadPos(nearestEnemy) : null;
                                if (CastPositionFinder.TryFindCastPosition(request, out IntVec3 castCell) && castCell != selPos && ShouldMoveTo(castCell))
                                {
                                    StartOrQueueCoverJob(castCell, 0);
                                }
                                else if (ShouldShootNow())
                                {
                                    StartWaitCombatJob(52);
                                }
                            }
                            // Fallback: already close enough, just shoot.
                            else if (ShouldShootNow())
                            {
                                StartWaitCombatJob(52);
                            }
                        }
                        // Find cover while the enemy approaches but isn't yet in sight.
                        else if (bestEnemyVisibleSoon)
                        {
                            progress = 512;
                            CoverPositionRequest request = new CoverPositionRequest();
                            request.caster             = selPawn;
                            request.verb               = verb;
                            request.target             = nearestEnemy;
                            request.maxRangeFromCaster = Maths.Min(verb.EffectiveRange, 10f) * personality.cover;
                            request.checkBlockChance   = true;
                            try
                            {
                                if (CoverPositionFinder.TryFindCoverPosition(request, out IntVec3 cell) && ShouldMoveTo(cell))
                                {
                                    StartOrQueueCoverJob(cell, 10);
                                }
                            }
                            catch (System.ArgumentOutOfRangeException ex)
                            {
                                Log.Error($"CoverPositionFinder.TryFindCoverPosition threw IndexOutOfRange for {selPawn} progress:{progress} \n{ex}");
                            }
                        }
                        // Flank or chase when no enemy is visible or approaching.
                        else
                        {
                            progress = 513;
                            TryFlankOrChase();
                        }
                    }
                }
                else
                {
                    progress = 521;
                    int                         shootingNum      = 0;
                    int                         rangedNum        = 0;
                    IEnumerator<AIEnvAgentInfo> enumeratorAllies = data.Allies();
                    while (enumeratorAllies.MoveNext())
                    {
                        AIEnvAgentInfo info = enumeratorAllies.Current;
                        if (info.thing is Pawn ally && DamageUtility.GetDamageReport(ally).primaryIsRanged)
                        {
                            rangedNum++;
                            if (ally.stances?.curStance is Stance_Warmup)
                            {
                                shootingNum++;
                            }
                        }
                    }
                    progress = 522;
                    float distOffset = Mathf.Clamp(2.0f * shootingNum - rangedEnemiesTargetingSelf.Count, 0, 25);
                    float moveBias   = Mathf.Clamp01(2f * shootingNum / (rangedNum + 1f) * personality.group);
                    if (Finder.Settings.Debug_LogJobs && distOffset > 0)
                    {
                        selPawn.Map.debugDrawer.FlashCell(selPos, distOffset / 20f, $"{distOffset}");
                    }
                    if (moveBias <= 0.5f)
                    {
                        moveBias = 0f;
                    }
                    if (duty.Is(CombatAI_DutyDefOf.CombatAI_AssaultPoint) && Rand.Chance(1 - moveBias))
                    {
                        return;
                    }
                    if (bestEnemyVisibleNow)
                    {
                        progress = 523;
                        if (nearestEnemyDist > 6 * personality.cover)
                        {
                            CastPositionRequest request = new CastPositionRequest();
                            request.caster                = selPawn;
                            request.target                = nearestEnemy;
                            request.maxRangeFromTarget    = 9999;
                            request.verb                  = verb;
                            request.maxRangeFromCaster    = (Maths.Max(Maths.Min(verb.EffectiveRange, nearestEnemyDist) / 2f, 10f) * personality.cover + distOffset) * Finder.P50;
                            request.wantCoverFromTarget   = true;
                            request.preferredCastPosition = !IsAIAutoControlled && Finder.Settings.LateralSpread_Enabled ? GetLateralSpreadPos(nearestEnemy) : null;
                            if (CastPositionFinder.TryFindCastPosition(request, out IntVec3 cell) && cell != selPos && (ShouldMoveTo(cell) || Rand.Chance(moveBias)))
                            {
                                StartOrQueueCoverJob(cell, 0);
                            }
                            else if (ShouldShootNow())
                            {
                                StartWaitCombatJob(52);
                            }
                        }
                        else if (ShouldShootNow())
                        {
                            StartWaitCombatJob(53);
                        }
                    }
                    // best enemy is approaching but not yet in view
                    else if (bestEnemyVisibleSoon)
                    {
                        progress = 524;
                        _last    = 60;
                        CoverPositionRequest request = new CoverPositionRequest();
                        request.caster = selPawn;
                        request.verb   = verb;
                        request.target = nearestEnemy;
                        if (!Finder.Performance.TpsCriticallyLow)
                        {
                            request.majorThreats = new List<Thing>();
                            int threatCount = rangedEnemiesTargetingSelf.Count;
                            if (threatCount > 0)
                            {
                                Thing[] snapshot = rangedEnemiesTargetingSelf.ToArray();
                                for (int i = 0; i < threatCount; i++)
                                {
                                    request.majorThreats.Add(snapshot[Math.Abs(Rand.Int) % threatCount]);
                                }
                            }
                            request.maxRangeFromCaster = Maths.Min(verb.EffectiveRange, 10f) + distOffset;
                        }
                        else
                        {
                            request.maxRangeFromCaster = Maths.Max(verb.EffectiveRange, 10f);
                        }
                        request.maxRangeFromCaster *= personality.cover;
                        request.checkBlockChance   =  true;
                        try
                        {
                            if (CoverPositionFinder.TryFindCoverPosition(request, out IntVec3 cell))
                            {
                                if (ShouldMoveTo(cell) || Rand.Chance(moveBias))
                                {
                                    StartOrQueueCoverJob(cell, 10);
                                }
                                else if (nearestEnemy is Pawn enemyPawn)
                                {
                                    _last = 71;
                                    // fallback
                                    request.target             = PawnPathUtility.GetMovingShiftedPosition(enemyPawn, 90);
                                    request.maxRangeFromCaster = Mathf.Min(request.maxRangeFromCaster, 5) + distOffset;
                                    if (CombatAI.Compatibility.VerbCompat.CanHitFromCellIgnoringRange(verb, selPos, request.target, out _))
                                    {
                                        bool found2 = false;
                                        try
                                        {
                                            found2 = CoverPositionFinder.TryFindCoverPosition(request, out cell);
                                        }
                                        catch (System.ArgumentOutOfRangeException ex)
                                        {
                                            int rangedCount = -1;
                                            try { rangedCount = rangedEnemiesTargetingSelf?.Count ?? -1; } catch { }
                                            Log.Error($"CoverPositionFinder.TryFindCoverPosition threw IndexOutOfRange for {selPawn} progress:{progress} rangedEnemiesTargetingSelf.Count={rangedCount} \n{ex}");
                                            found2 = false;
                                        }
                                        if (found2 && (ShouldMoveTo(cell) || Rand.Chance(moveBias)))
                                        {
                                            StartOrQueueCoverJob(cell, 20);
                                        }
                                    }
                                }
                            }
                        }
                        catch (System.ArgumentOutOfRangeException ex)
                        {
                            int rangedCount = -1;
                            try { rangedCount = rangedEnemiesTargetingSelf?.Count ?? -1; } catch { }
                            Log.Error($"CoverPositionFinder.TryFindCoverPosition threw IndexOutOfRange for {selPawn} progress:{progress} rangedEnemiesTargetingSelf.Count={rangedCount} \n{ex}");
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error($"CoverPositionFinder.TryFindCoverPosition threw exception for {selPawn} progress:{progress} \n{ex}");
                        }
                    } else
                    {
                        progress = 525;
                        TryFlankOrChase();
                    }
                }
                
            }
        }
        
        private void TryMeleeReaction(float possibleDmg, ref int progress)
        {
            float nearestEnemyDist = 1e6f;
            Thing nearestEnemy = null;
            // For debugging and logging.
            progress = 201;
            if ((selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Retreat) || selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Cover)) && (rangedEnemiesTargetingSelf.Count == 0 || possibleDmg < 2.5f))
            {
                _last = 30;
                selPawn.jobs.StopAll();
            }
            bool    bestEnemyIsRanged             = false;
            bool    bestEnemyIsMeleeAttackingAlly = false;
            // TODO create melee reactions.
            IEnumerator<AIEnvAgentInfo> enumeratorEnemies = data.EnemiesWhere(AIEnvAgentState.nearby);
            // For debugging and logging.
            progress = 202;
            while (enumeratorEnemies.MoveNext())
            {
                AIEnvAgentInfo info = enumeratorEnemies.Current;
#if DEBUG_REACTION
                if (info.thing == null)
                {
                    Log.Error("Found null thing (2)");
                    continue;
                }
#endif
                // For debugging and logging.
                progress = 203;
                if (info.thing.Spawned && selPawn.CanReach(info.thing, PathEndMode.Touch, Danger.Deadly))
                {
                    Verb enemyVerb = info.thing.TryGetAttackVerb();
                    if (enemyVerb?.IsMeleeAttack == true && info.thing is Pawn enemyPawn && enemyPawn.CurJob.Is(JobDefOf.AttackMelee) && enemyPawn.CurJob.targetA.Thing?.TryGetAttackVerb()?.IsMeleeAttack == false)
                    {
                        if (!bestEnemyIsMeleeAttackingAlly)
                        {
                            bestEnemyIsMeleeAttackingAlly = true;
                            nearestEnemyDist              = 1e5f;
                            nearestEnemy                  = null;
                        }
                        float dist = selPawn.DistanceTo_Fast(info.thing);
                        if (dist < nearestEnemyDist)
                        {
                            nearestEnemyDist = dist;
                            nearestEnemy     = info.thing;
                        }
                    }
                    else if (!bestEnemyIsMeleeAttackingAlly)
                    {
                        if (enemyVerb?.IsMeleeAttack == false)
                        {
                            if (!bestEnemyIsRanged)
                            {
                                bestEnemyIsRanged = true;
                                nearestEnemyDist  = 1e5f;
                                nearestEnemy      = null;
                            }
                            float dist = selPawn.DistanceTo_Fast(info.thing);
                            if (dist < nearestEnemyDist)
                            {
                                nearestEnemyDist = dist;
                                nearestEnemy     = info.thing;
                            }
                        }
                        else if (!bestEnemyIsRanged)
                        {
                            float dist = selPawn.DistanceTo_Fast(info.thing);
                            if (dist < nearestEnemyDist)
                            {
                                nearestEnemyDist = dist;
                                nearestEnemy     = info.thing;
                            }
                        }
                    }
                }
                // For debugging and logging.
                progress = 204;
            }
            // For debugging and logging.
            progress = 205;
            if (nearestEnemy == null)
            {
                nearestEnemy = selPawn.mindState.enemyTarget;
            }
            if (nearestEnemy == null || selPawn.CurJob.Is(JobDefOf.AttackMelee) && selPawn.CurJob.targetA.Thing == nearestEnemy)
            {
                return;
            }
            // For debugging and logging.
            progress   = 206;
            _bestEnemy = nearestEnemy;
            float distToEnemy = selPawn.DistanceTo_Fast(nearestEnemy);

            // ── Helper: issue standard melee attack job ──────────────────────────
            void IssueMeleeJob()
            {
                if (!selPawn.mindState.MeleeThreatStillThreat || selPawn.stances?.stagger?.Staggered == false)
                {
                    _last = 31;
                    Job job_melee = JobMaker.MakeJob(JobDefOf.AttackMelee, nearestEnemy);
                    job_melee.playerForced      = forcedTarget.IsValid;
                    job_melee.locomotionUrgency = LocomotionUrgency.Jog;
                    if (!IsPerformingMeleeAnimation(selPawn))
                    {
                        selPawn.jobs.ClearQueuedJobs();
                        selPawn.jobs.StartJob(job_melee, JobCondition.InterruptForced);
                        data.LastInterrupted = GenTicks.TicksGame;
                    }
                }
            }

            // ── Helper: issue a cover-advance movement job ───────────────────────
            bool IssueMoveJob(IntVec3 cell, int lastCode)
            {
                if (cell == selPawn.Position || !IsMechanoidDestValid(cell)) return false;
                _last = lastCode;
                selPawn.mindState.enemyTarget = nearestEnemy;
                Job job_goto = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Cover, cell);
                job_goto.expiryInterval        = -1;
                job_goto.checkOverrideOnExpire = false;
                job_goto.playerForced          = forcedTarget.IsValid;
                job_goto.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
                if (!IsPerformingMeleeAnimation(selPawn))
                {
                    selPawn.jobs.ClearQueuedJobs();
                    selPawn.jobs.StartJob(job_goto, JobCondition.InterruptForced);
                    data.LastInterrupted = GenTicks.TicksGame;
                }
                return true;
            }

            // Priority 1 — INTERCEPT: enemy is melee-attacking a ranged ally → charge immediately to protect
            if (bestEnemyIsMeleeAttackingAlly)
            {
                IssueMeleeJob();
                progress = 207;
                return;
            }

            IntVec3 meleePos    = selPawn.Position;
            IntVec3 meleeTarget = nearestEnemy.Position;

            // Priority 2 — COUNT nearby enemies to detect crowd density
            int nearbyEnemyCount = 0;
            IEnumerator<AIEnvAgentInfo> densityEnum = data.EnemiesWhere(AIEnvAgentState.nearby);
            while (densityEnum.MoveNext())
            {
                AIEnvAgentInfo di = densityEnum.Current;
                if (di.thing?.Spawned == true && selPawn.DistanceTo_Fast(di.thing) < 5f)
                    nearbyEnemyCount++;
            }
            bool crowded = nearbyEnemyCount >= 3;

            // Priority 3 — FLANK / SIDE APPROACH: avoid charging into a dense crowd or heavy crossfire
            progress = 211;
            if (crowded || rangedEnemiesTargetingSelf.Count >= 2)
            {
                float dx  = meleeTarget.x - meleePos.x;
                float dz  = meleeTarget.z - meleePos.z;
                float len = Mathf.Sqrt(dx * dx + dz * dz);
                if (len > 2f)
                {
                    float perpX      = -dz / len;
                    float perpZ      =  dx / len;
                    // Aim for a position beside the enemy, not in front of them
                    float sideOffset = Mathf.Clamp(distToEnemy * 0.4f, 3f, 8f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        IntVec3 flankCell = new IntVec3(
                            meleeTarget.x + Mathf.RoundToInt(perpX * side * sideOffset),
                            meleeTarget.y,
                            meleeTarget.z + Mathf.RoundToInt(perpZ * side * sideOffset));
                        if (flankCell.InBounds(selPawn.Map)
                            && flankCell != meleePos
                            && selPawn.CanReach(flankCell, PathEndMode.OnCell, Danger.Deadly))
                        {
                            if (IssueMoveJob(flankCell, 33))
                            {
                                progress = 207;
                                return;
                            }
                        }
                    }
                }
            }

            // Priority 4 — COVER ADVANCE: when being shot while approaching, route through lower-threat cells
            progress = 210;
            if (distToEnemy > 12f && rangedEnemiesTargetingSelf.Count > 0
                && !selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Cover))
            {
                IntVec3 advanceCell = new IntVec3(
                    Mathf.RoundToInt(meleePos.x + (meleeTarget.x - meleePos.x) * 0.5f),
                    meleePos.y,
                    Mathf.RoundToInt(meleePos.z + (meleeTarget.z - meleePos.z) * 0.5f));
                if (advanceCell.InBounds(selPawn.Map) && IssueMoveJob(advanceCell, 32))
                {
                    progress = 207;
                    return;
                }
            }

            // Priority 5 — FALLBACK: direct melee charge
            IssueMeleeJob();
            // For debugging and logging.
            progress = 207;
        }

        private void ReactDebug_Internel(out int progress)
        {
            // For debugging and logging.
            progress = 2;
#if DEBUG_REACTION
            if (Finder.Settings.Debug && Finder.Settings.Debug_ValidateSight)
            {
                _visibleEnemies.Clear();
                IEnumerator<AIEnvAgentInfo> enumerator = data.Enemies();
                while (enumerator.MoveNext())
                {
                    AIEnvAgentInfo info = enumerator.Current;
                    if (info.thing == null)
                    {
                        Log.Warning("Found null thing (1)");
                        continue;
                    }
                    if (info.thing.Spawned && info.thing is Pawn pawn)
                    {
                        _visibleEnemies.Add(pawn);
                    }
                }
                // For debugging and logging.
                progress = 21;
                if (_path.Count == 0 || _path.Last() != parent.Position)
                {
                    _path.Add(parent.Position);
                    if (GenTicks.TicksGame - lastInterupted < 150)
                    {
                        _colors.Add(Color.red);
                    }
                    else if (GenTicks.TicksGame - lastInterupted < 240)
                    {
                        _colors.Add(Color.yellow);
                    }
                    else
                    {
                        _colors.Add(Color.black);
                    }
                    if (_path.Count >= 30)
                    {
                        _path.RemoveAt(0);
                        _colors.RemoveAt(0);
                    }
                }
                // For debugging and logging.
                progress = 22;
            }
#endif
        }
        
        private bool TryStartRetreat(float possibleDmg, float possibleDmgWarmup, float possibleDmgDistance, PersonalityTacker.PersonalityResult personality, Pawn nearestMeleeEnemy, float nearestMeleeEnemyDist, ref int progress)
        {
            IntVec3 selPos = selPawn.Position;
            // For AI auto-controlled pawns, strip retreating/fleeing enemies from ranged threat list
            // so we don't run away from enemies that are already defeated and leaving.
            if (IsAIAutoControlled && rangedEnemiesTargetingSelf.Count > 0)
            {
                rangedEnemiesTargetingSelf.RemoveAll(e => IsEnemyRetreating(e as Pawn));
            }
            // For AutoControl pawns: boost effective damage when critically wounded to trigger retreat sooner.
            if (IsAIAutoControlled && selPawn.health?.summaryHealth?.SummaryHealthPercent < 0.35f)
            {
                possibleDmg *= 1.6f;
            }
            if (rangedEnemiesTargetingSelf.Count > 0 && nearestMeleeEnemyDist > 2)
            {
                float retreatRoll = 15 + Rand.Range(0, 15 * rangedEnemiesTargetingSelf.Count) + data.NumAllies * 15;
                if (Finder.Settings.Debug_LogJobs)
                {
                    MoteMaker.ThrowText(selPawn.DrawPos, selPawn.Map, $"r:{Math.Round(retreatRoll)},d:{possibleDmg}", Color.white);
                }
                // For debugging and logging.
                progress = 91;
                // major retreat attempt if the pawn is doomed
                if (possibleDmg * personality.retreat - retreatRoll > 0.001f && possibleDmg * personality.retreat >= 50)
                {
                    _last      = 10;
                    _bestEnemy = nearestMeleeEnemy;
                    CoverPositionRequest request = new CoverPositionRequest();
                    request.caster             = selPawn;
                    request.target             = nearestMeleeEnemy;
                    request.majorThreats       = new List<Thing>(rangedEnemiesTargetingSelf);
                    request.maxRangeFromCaster = 12;
                    request.checkBlockChance   = true;
                    IntVec3 cell = default(IntVec3);
                    bool found = false;
                    try
                    {
                        found = CoverPositionFinder.TryFindRetreatPosition(request, out cell);
                    }
                    catch (System.ArgumentOutOfRangeException ex)
                    {
                        int rangedCount = -1;
                        try { rangedCount = rangedEnemiesTargetingSelf?.Count ?? -1; } catch { }
                        Log.Error($"CoverPositionFinder.TryFindRetreatPosition threw IndexOutOfRange for {selPawn} progress:{progress} rangedEnemiesTargetingSelf.Count={rangedCount} \n{ex}");
                        found = false;
                    }
                    if (found)
                    {
                        // For debugging and logging.
                        progress = 911;
                        if (ShouldMoveTo(cell) && IsMechanoidDestValid(cell))
                        {
                            if (cell != selPos)
                            {
                                _last = 11;
                                Job job_goto = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Retreat, cell);
                                job_goto.playerForced          = forcedTarget.IsValid;
                                job_goto.checkOverrideOnExpire = false;
                                job_goto.expiryInterval        = -1;
                                job_goto.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
                                if (!IsPerformingMeleeAnimation(selPawn))
                                {
                                    selPawn.jobs.ClearQueuedJobs();
                                    selPawn.jobs.StartJob(job_goto, JobCondition.InterruptForced);
                                }
                                data.LastRetreated = GenTicks.TicksGame;
                                if (Rand.Chance(0.5f) && !Finder.Settings.Debug_LogJobs)
                                {
                                    MoteMaker.ThrowText(selPawn.DrawPos, selPawn.Map, "Cover me!", Color.white);
                                }
                            }
                            return true;
                        }
                        if (Finder.Settings.Debug_LogJobs)
                        {
                            MoteMaker.ThrowText(selPawn.DrawPos, selPawn.Map, "retreat skipped", Color.white);
                        }
                    }
                }
                // For debugging and logging.
                progress = 92;
                // try minor retreat (duck for cover fast)
                if (possibleDmg * personality.duck - retreatRoll * 0.5f > 0.001f && possibleDmg * personality.duck >= 30)
                {
                    // selPawn.Map.debugDrawer.FlashCell(selPos, 1.0f, $"{possibleDmg}, {targetedBy.Count}, {rangedEnemiesTargetingSelf.Count}");
                    CoverPositionRequest request = new CoverPositionRequest();
                    request.caster             = selPawn;
                    request.majorThreats       = new List<Thing>(rangedEnemiesTargetingSelf);
                    request.checkBlockChance   = true;
                    request.maxRangeFromCaster = Mathf.Clamp(possibleDmgWarmup * 5f - rangedEnemiesTargetingSelf.Count, 4f, 8f);
                    IntVec3 cell = default(IntVec3);
                    bool foundDuck = false;
                    try
                    {
                        foundDuck = CoverPositionFinder.TryFindDuckPosition(request, out cell);
                    }
                    catch (System.ArgumentOutOfRangeException ex)
                    {
                        int rangedCount = -1;
                        try { rangedCount = rangedEnemiesTargetingSelf?.Count ?? -1; } catch { }
                        Log.Error($"CoverPositionFinder.TryFindDuckPosition threw IndexOutOfRange for {selPawn} progress:{progress} rangedEnemiesTargetingSelf.Count={rangedCount} \n{ex}");
                        foundDuck = false;
                    }
                    if (foundDuck)
                    {
                        bool diff = cell != selPos && IsMechanoidDestValid(cell);
                        // run to cover
                        if (diff)
                        {
                            _last = 12;
                            Job job_goto = JobMaker.MakeJob(CombatAI_JobDefOf.CombatAI_Goto_Duck, cell);
                            job_goto.playerForced          = forcedTarget.IsValid;
                            job_goto.checkOverrideOnExpire = false;
                            job_goto.expiryInterval        = -1;
                            job_goto.locomotionUrgency     = Finder.Settings.Enable_Sprinting ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog;
                            if (!IsPerformingMeleeAnimation(selPawn))
                            {
                                selPawn.jobs.ClearQueuedJobs();
                                selPawn.jobs.StartJob(job_goto, JobCondition.InterruptForced);
                                data.LastRetreated = lastRetreated = GenTicks.TicksGame;
                            }
                        }
                        if (data.TookDamageRecently(45) || !diff)
                        {
                            _last = 13;
                            Job job_waitCombat = JobMaker.MakeJob(JobDefOf.Wait_Combat, Rand.Int % 50 + 50);
                            job_waitCombat.playerForced          = forcedTarget.IsValid;
                            job_waitCombat.checkOverrideOnExpire = true;
                            selPawn.jobs.jobQueue.EnqueueFirst(job_waitCombat);
                            data.LastRetreated = lastRetreated = GenTicks.TicksGame;
                        }
                        if (Rand.Chance(0.5f) && !Finder.Settings.Debug_LogJobs)
                        {
                            MoteMaker.ThrowText(selPawn.DrawPos, selPawn.Map, "Finding cover!", Color.white);
                        }
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        ///     Returns whether parent pawn should move to a new position.
        /// </summary>
        /// <param name="newPos">New position</param>
        /// <returns>Whether to move or not</returns>
        private bool ShouldMoveTo(IntVec3 newPos)
        {
            IntVec3 pos = selPawn.Position;
            if (pos == newPos)
            {
                return true;
            }
            float curVisibility = sightReader.GetVisibilityToEnemies(pos);
            float curThreat     = sightReader.GetVisibilityToEnemies(pos);
            Job   job           = selPawn.CurJob;
            if (curThreat == 0 && curVisibility == 0 && !(job.Is(JobDefOf.Wait_Combat) || job.Is(CombatAI_JobDefOf.CombatAI_Goto_Cover) || job.Is(CombatAI_JobDefOf.CombatAI_Goto_Duck) || job.Is(CombatAI_JobDefOf.CombatAI_Goto_Retreat)))
            {
                return sightReader.GetVisibilityToEnemies(newPos) <= 2f && sightReader.GetThreat(newPos) < 1f;
            }
            float visDiff    = curVisibility - sightReader.GetVisibilityToEnemies(newPos);
            float magDiff    = Maths.Sqrt_Fast(sightReader.GetEnemyDirection(pos).sqrMagnitude, 4) - Maths.Sqrt_Fast(sightReader.GetEnemyDirection(newPos).sqrMagnitude, 4);
            float threatDiff = curThreat - sightReader.GetThreat(newPos);
            return Rand.Chance(visDiff) && Rand.Chance(threatDiff) && Rand.Chance(magDiff);
        }

        /// <summary>
        ///     Whether the pawn should start shooting now.
        /// </summary>
        /// <returns></returns>
        private bool ShouldShootNow()
        {
            return !selPawn.CurJob.Is(JobDefOf.Wait_Combat) && (!selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Cover) && !selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Duck) || !ShouldMoveTo(selPawn.CurJob.targetA.Cell));
        }

        /// <summary>
        ///     Called When the parent takes damage.
        /// </summary>
        /// <param name="dInfo">Damage info</param>
        public void Notify_TookDamage(DamageInfo dInfo)
        {
            // notify the custom duty manager that this pawn took damage.
            if (duties != null)
            {
                duties.Notify_TookDamage();
            }
            data.LastTookDamage = lastTookDamage = GenTicks.TicksGame;
            if (dInfo.Instigator != null && data.NumAllies != 0 && dInfo.Instigator.HostileTo(selPawn))
            {
                StartAggroCountdown(dInfo.Instigator);
            }
        }

        /// <summary>
        ///     Called when a bullet impacts nearby.
        /// </summary>
        /// <param name="instigator">Attacker</param>
        /// <param name="cell">Impact position</param>
        public void Notify_BulletImpact(Thing instigator, IntVec3 cell)
        {
            if (instigator == null)
            {
                StartAggroCountdown(new LocalTargetInfo(cell));
            }
            else
            {
                StartAggroCountdown(new LocalTargetInfo(instigator));
            }
        }
        /// <summary>
        ///     Start aggro countdown.
        /// </summary>
        /// <param name="enemy">Enemy.</param>
        public void StartAggroCountdown(LocalTargetInfo enemy)
        {
            aggroTarget = enemy;
            aggroTicks  = Rand.Range(30, 90);
        }

        /// <summary>
        ///     Switch the pawn to an aggro mode and their allies around them.
        /// </summary>
        /// <param name="enemy">Attacker</param>
        /// <param name="aggroAllyChance">Chance to aggro nearbyAllies</param>
        /// <param name="sig">Aggro sig</param>
        private void TryAggro(LocalTargetInfo enemy, float aggroAllyChance, int sig)
        {
            if (selPawn.mindState.duty.Is(DutyDefOf.Defend) && data.AgroSig != sig)
            {
                Pawn_CustomDutyTracker.CustomPawnDuty custom = CustomDutyUtility.HuntDownEnemies(enemy.Cell, Rand.Int % 1200 + 2400);
                if (selPawn.TryStartCustomDuty(custom))
                {
                    data.AgroSig = sig;
                    // aggro nearby Allies
                    IEnumerator<AIEnvAgentInfo> allies = data.AlliesNearBy();
                    while (allies.MoveNext())
                    {
                        AIEnvAgentInfo ally = allies.Current;
                        // make allies not targeting anyone target the attacking enemy
                        if (Rand.Chance(aggroAllyChance) && ally.thing is Pawn { Destroyed: false, Spawned: true, Downed: false } other && other.mindState.duty.Is(DutyDefOf.Defend))
                        {
                            ThingComp_CombatAI comp = other.AI();
                            if (comp != null && comp.data.AgroSig != sig)
                            {
                                if (enemy.HasThing)
                                {
                                    other.mindState.enemyTarget ??= enemy.Thing;
                                }
                                comp.TryAggro(enemy, aggroAllyChance / 2f, sig);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Start a sapping task.
        /// </summary>
        /// <param name="blocked">Blocked cells</param>
        /// <param name="cellBefore">Cell before blocked cells</param>
        /// <param name="findEscorts">Whether to look for escorts</param>
        public void StartSapper(List<IntVec3> blocked, IntVec3 cellBefore, IntVec3 cellAhead, bool findEscorts)
        {
            if (cellBefore.IsValid && sapperNodes.Count > 0 && GenTicks.TicksGame - sapperStartTick < 4800)
            {
                ReleaseEscorts(false);
            }
            this.cellBefore  = cellBefore;
            this.cellAhead   = cellAhead;
            this.findEscorts = findEscorts;
            sapperStartTick  = GenTicks.TicksGame;
            sapperNodes.Clear();
            sapperNodes.AddRange(blocked);
            _sap = 0;
//			TryStartSapperJob();
        }

        /// <summary>
        ///     Returns debug gizmos.
        /// </summary>
        /// <returns></returns>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Finder.Settings.Debug && Finder.Settings.Debug_LogJobs)
            {
                Command_Action jobs = new Command_Action();
                jobs.defaultLabel = "DEV: View job logs";
                jobs.action = delegate
                {
                    if (WindowStackCompat.Windows(Find.WindowStack).Any(w => w is Window_JobLogs logs && logs.comp == this))
                    {
                        return;
                    }
                    jobLogs ??= new List<JobLog>();
                    Window_JobLogs window = new Window_JobLogs(this);
                    Find.WindowStack.Add(window);
                };
                yield return jobs;
            }
            if (Prefs.DevMode && DebugSettings.godMode)
            {
                if ((selPawn.mindState.duty.Is(DutyDefOf.Escort) || selPawn.mindState.duty.Is(CombatAI_DutyDefOf.CombatAI_Escort)) && selPawn.mindState.duty.focus.IsValid)
                {
                    Command_Action escort = new Command_Action();
                    escort.defaultLabel = "DEV: Flash escort area";
                    escort.action = delegate
                    {
                        Pawn  focus  = selPawn.mindState.duty.focus.Thing as Pawn;
                        Map   map    = focus.Map;
                        float radius = selPawn.mindState.duty.radius;
                        map.debugDrawer.FlashCell(focus.Position, 1, "XXXXXXX");
                        foreach (IntVec3 cell in GenRadial.RadialCellsAround(focus.Position, 0, 20))
                        {
                            if (JobGiver_CAIFollowEscortee.NearFollowee(selPawn, focus, cell, radius, out _))
                            {
                                map.debugDrawer.FlashCell(cell, 0.9f, $"{cell.HeuristicDistanceTo(focus.Position, map)}");
                            }
                            else
                            {
                                map.debugDrawer.FlashCell(cell, 0.01f, $"{cell.HeuristicDistanceTo(focus.Position, map)}");
                            }
                        }
                    };
                    yield return escort;
                }
                Verb           verb           = selPawn.TryGetAttackVerb();
                float          retreatDistSqr = Maths.Max(verb.EffectiveRange * verb.EffectiveRange / 9, 36);
                Map            map            = selPawn.Map;
                Command_Action retreat        = new Command_Action();
                retreat.defaultLabel = "DEV: Retreat position search";
                retreat.action = delegate
                {
                    CoverPositionRequest request = new CoverPositionRequest();
                    if (_bestEnemy != null)
                    {
                        request.target = new LocalTargetInfo(_bestEnemy.Position);
                    }
                    request.caster             = selPawn;
                    request.verb               = verb;
                    request.maxRangeFromCaster = Maths.Min(Mathf.Max(retreatDistSqr * 2 / (selPawn.BodySize + 0.01f), 5), 15);
                    request.checkBlockChance   = true;
                    CoverPositionFinder.TryFindRetreatPosition(request, out IntVec3 cell, (cell, val) => map.debugDrawer.FlashCell(cell, Mathf.Clamp((val + 15f) / 30f, 0.01f, 0.99f), $"{Math.Round(val, 3)}"));
                    if (cell.IsValid)
                    {
                        map.debugDrawer.FlashCell(cell, 1, "XXXXXXX", 150);
                    }
                };
                Command_Action duck = new Command_Action();
                duck.defaultLabel = "DEV: Duck position search";
                duck.action = delegate
                {
                    CoverPositionRequest request = new CoverPositionRequest();
                    request.majorThreats       = data.BeingTargetedBy;
                    request.caster             = selPawn;
                    request.verb               = verb;
                    request.maxRangeFromCaster = 5;
                    request.checkBlockChance   = true;
                    CoverPositionFinder.TryFindDuckPosition(request, out IntVec3 cell, (cell, val) => map.debugDrawer.FlashCell(cell, Mathf.Clamp((val + 15f) / 30f, 0.01f, 0.99f), $"{Math.Round(val, 3)}"));
                    if (cell.IsValid)
                    {
                        map.debugDrawer.FlashCell(cell, 1, "XXXXXXX", 150);
                    }
                };
                Command_Action cover = new Command_Action();
                cover.defaultLabel = "DEV: Cover position search";
                cover.action = delegate
                {
                    CoverPositionRequest request = new CoverPositionRequest();
                    if (_bestEnemy != null)
                    {
                        request.target = new LocalTargetInfo(_bestEnemy.Position);
                    }
                    request.caster             = selPawn;
                    request.verb               = verb;
                    request.maxRangeFromCaster = Mathf.Clamp(selPawn.GetStatValue_Fast(StatDefOf.MoveSpeed, 60) * 3 / (selPawn.BodySize + 0.01f), 4, 15);
                    request.checkBlockChance   = true;
                    CoverPositionFinder.TryFindCoverPosition(request, out IntVec3 cell, (cell, val) => map.debugDrawer.FlashCell(cell, Mathf.Clamp((val + 15f) / 30f, 0.01f, 0.99f), $"{Math.Round(val, 3)}"));
                    if (cell.IsValid)
                    {
                        map.debugDrawer.FlashCell(cell, 1, "XXXXXXX", 150);
                    }
                };
                Command_Action cast = new Command_Action();
                cast.defaultLabel = "DEV: Cast position search";
                cast.action = delegate
                {
                    if (_bestEnemy == null)
                    {
                        return;
                    }
                    CastPositionRequest request = new CastPositionRequest();
                    request.caster              = selPawn;
                    request.target              = _bestEnemy;
                    request.verb                = verb;
                    request.maxRangeFromTarget  = 9999;
                    request.maxRangeFromCaster  = Mathf.Clamp(selPawn.GetStatValue_Fast(StatDefOf.MoveSpeed, 60) * 3 / (selPawn.BodySize + 0.01f), 4, 15);
                    request.wantCoverFromTarget = true;
                    try
                    {
                        DebugViewSettings.drawCastPositionSearch = true;
                        CastPositionFinder.TryFindCastPosition(request, out IntVec3 cell);
                        if (cell.IsValid)
                        {
                            map.debugDrawer.FlashCell(cell, 1, "XXXXXXX", 150);
                        }
                    }
                    catch (Exception er)
                    {
                        Log.Error(er.ToString());
                    }
                    finally
                    {
                        DebugViewSettings.drawCastPositionSearch = false;
                    }
                };
                yield return retreat;
                yield return duck;
                yield return cover;
                yield return cast;
            }
            if ((selPawn.IsColonist || (selPawn.RaceProps.IsMechanoid && selPawn.Faction != null && selPawn.Faction.IsPlayerSafe())) && selPawn.Drafted)
            {
                // Only yield this gizmo from the first selected drafted colonist or friendly mechanoid.
                // GizmoGridDrawer calls toggleAction for every gizmo in the same group,
                // so yielding from all pawns causes even numbers of pawns to cancel each other out.
                bool isGroupLeader = true;
                List<Pawn> selectedPawns = Find.Selector.SelectedPawns;
                for (int si = 0; si < selectedPawns.Count; si++)
                {
                    Pawn p = selectedPawns[si];
                    if (p == selPawn) break;
                    if ((p.IsColonist || (p.RaceProps.IsMechanoid && p.Faction != null && p.Faction.IsPlayerSafe())) && p.Drafted && p.AI() != null)
                    {
                        isGroupLeader = false;
                        break;
                    }
                }
                if (isGroupLeader)
                {
                    Command_Toggle autoControlToggle = new Command_Toggle();
                    autoControlToggle.defaultLabel = "CombatAI.Gizmos.AutoControl".Translate();
                    autoControlToggle.defaultDesc  = "CombatAI.Gizmos.AutoControl.Desc".Translate();
                    autoControlToggle.icon         = Tex.Isma_Gizmos_move_attack;
                    autoControlToggle.groupable    = false;
                    autoControlToggle.isActive = () =>
                    {
                        foreach (Pawn pawn in Find.Selector.SelectedPawns)
                        {
                            if ((pawn.IsColonist || (pawn.RaceProps.IsMechanoid && pawn.Faction != null && pawn.Faction.IsPlayerSafe())) && pawn.Drafted)
                            {
                                ThingComp_CombatAI comp = pawn.AI();
                                if (comp != null && comp.aiAutoControl)
                                {
                                    return true;
                                }
                            }
                        }
                        return false;
                    };
                    autoControlToggle.toggleAction = () =>
                    {
                        bool anyEnabled = false;
                        foreach (Pawn pawn in Find.Selector.SelectedPawns)
                        {
                            if ((pawn.IsColonist || (pawn.RaceProps.IsMechanoid && pawn.Faction != null && pawn.Faction.IsPlayerSafe())) && pawn.Drafted)
                            {
                                ThingComp_CombatAI comp = pawn.AI();
                                if (comp != null && comp.aiAutoControl)
                                {
                                    anyEnabled = true;
                                    break;
                                }
                            }
                        }
                        bool newVal = !anyEnabled;
                        foreach (Pawn pawn in Find.Selector.SelectedPawns)
                        {
                            if (pawn.IsColonist || (pawn.RaceProps.IsMechanoid && pawn.Faction != null && pawn.Faction.IsPlayerSafe()))
                            {
                                ThingComp_CombatAI comp = pawn.AI();
                                if (comp != null)
                                {
                                    comp.aiAutoControl = newVal;
                            // Also clear S&D mode when AutoControl is turned off.
                            if (!newVal)
                            {
                                comp.aiSearchAndDestroy      = false;
                                comp.aiSearchAndDestroyMelee = false;
                            }
                        }
                    }
                }
                };
                    yield return autoControlToggle;
                    // S&D gizmos: only visible while AutoControl is active.
                    // Only the first AC pawn in the selection emits the SD gizmos to avoid
                    // duplicates when multiple AutoControl pawns are selected at once.
                    if (IsAIAutoControlled)
                    {
                        bool isFirstACPawnInSelection =
                            Find.Selector.SelectedPawns.FirstOrDefault(p =>
                                (p.IsColonist || (p.RaceProps.IsMechanoid && p.Faction != null && p.Faction.IsPlayerSafe()))
                                && p.Drafted && (p.AI()?.IsAIAutoControlled ?? false)) == selPawn;

                        if (isFirstACPawnInSelection)
                        {
                            // Determine which weapon-type categories are present among selected AC pawns.
                            bool anyRangedAC = false;
                            bool anyMeleeAC  = false;
                            foreach (Pawn p in Find.Selector.SelectedPawns)
                            {
                                if (!(p.IsColonist || (p.RaceProps.IsMechanoid && p.Faction != null && p.Faction.IsPlayerSafe()))
                                    || !p.Drafted) continue;
                                ThingComp_CombatAI c = p.AI();
                                if (c == null || !c.IsAIAutoControlled) continue;
                                bool pMelee = p.equipment?.Primary == null || p.equipment.Primary.def.IsMeleeWeapon;
                                if (pMelee) anyMeleeAC  = true;
                                else        anyRangedAC = true;
                            }

                            // ---- Ranged S&D toggle ----
                            if (anyRangedAC)
                            {
                                Command_Toggle sdRanged = new Command_Toggle();
                                sdRanged.defaultLabel = "CombatAI.Gizmos.SearchDestroy.Ranged".Translate();
                                sdRanged.defaultDesc  = "CombatAI.Gizmos.SearchDestroy.Ranged.Desc".Translate();
                                sdRanged.icon         = TexCommand.Attack;
                                sdRanged.groupable    = false;
                                sdRanged.isActive = () =>
                                {
                                    foreach (Pawn pawn in Find.Selector.SelectedPawns)
                                    {
                                        if (!(pawn.IsColonist || (pawn.RaceProps.IsMechanoid && pawn.Faction != null && pawn.Faction.IsPlayerSafe())) || !pawn.Drafted) continue;
                                        if (pawn.equipment?.Primary == null || pawn.equipment.Primary.def.IsMeleeWeapon) continue;
                                        ThingComp_CombatAI comp = pawn.AI();
                                        if (comp != null && comp.aiSearchAndDestroy) return true;
                                    }
                                    return false;
                                };
                                sdRanged.toggleAction = () =>
                                {
                                    bool anyOn = false;
                                    foreach (Pawn pawn in Find.Selector.SelectedPawns)
                                    {
                                        if (!(pawn.IsColonist || (pawn.RaceProps.IsMechanoid && pawn.Faction != null && pawn.Faction.IsPlayerSafe())) || !pawn.Drafted) continue;
                                        if (pawn.equipment?.Primary == null || pawn.equipment.Primary.def.IsMeleeWeapon) continue;
                                        ThingComp_CombatAI comp = pawn.AI();
                                        if (comp != null && comp.aiSearchAndDestroy) { anyOn = true; break; }
                                    }
                                    bool newVal = !anyOn;
                                    foreach (Pawn pawn in Find.Selector.SelectedPawns)
                                    {
                                        if (!(pawn.IsColonist || (pawn.RaceProps.IsMechanoid && pawn.Faction != null && pawn.Faction.IsPlayerSafe()))) continue;
                                        if (pawn.equipment?.Primary == null || pawn.equipment.Primary.def.IsMeleeWeapon) continue;
                                        ThingComp_CombatAI comp = pawn.AI();
                                        if (comp != null && comp.IsAIAutoControlled) comp.aiSearchAndDestroy = newVal;
                                    }
                                };
                                yield return sdRanged;
                            }

                            // ---- Melee S&D toggle (fist icon) ----
                            if (anyMeleeAC)
                            {
                                Command_Toggle sdMelee = new Command_Toggle();
                                sdMelee.defaultLabel = "CombatAI.Gizmos.SearchDestroy.Melee".Translate();
                                sdMelee.defaultDesc  = "CombatAI.Gizmos.SearchDestroy.Melee.Desc".Translate();
                                sdMelee.icon         = TexCommand.AttackMelee;
                                sdMelee.groupable    = false;
                                sdMelee.isActive = () =>
                                {
                                    foreach (Pawn pawn in Find.Selector.SelectedPawns)
                                    {
                                        if (!(pawn.IsColonist || (pawn.RaceProps.IsMechanoid && pawn.Faction != null && pawn.Faction.IsPlayerSafe())) || !pawn.Drafted) continue;
                                        if (!(pawn.equipment?.Primary == null || pawn.equipment.Primary.def.IsMeleeWeapon)) continue;
                                        ThingComp_CombatAI comp = pawn.AI();
                                        if (comp != null && comp.aiSearchAndDestroyMelee) return true;
                                    }
                                    return false;
                                };
                                sdMelee.toggleAction = () =>
                                {
                                    bool anyOn = false;
                                    foreach (Pawn pawn in Find.Selector.SelectedPawns)
                                    {
                                        if (!(pawn.IsColonist || (pawn.RaceProps.IsMechanoid && pawn.Faction != null && pawn.Faction.IsPlayerSafe())) || !pawn.Drafted) continue;
                                        if (!(pawn.equipment?.Primary == null || pawn.equipment.Primary.def.IsMeleeWeapon)) continue;
                                        ThingComp_CombatAI comp = pawn.AI();
                                        if (comp != null && comp.aiSearchAndDestroyMelee) { anyOn = true; break; }
                                    }
                                    bool newVal = !anyOn;
                                    foreach (Pawn pawn in Find.Selector.SelectedPawns)
                                    {
                                        if (!(pawn.IsColonist || (pawn.RaceProps.IsMechanoid && pawn.Faction != null && pawn.Faction.IsPlayerSafe()))) continue;
                                        if (!(pawn.equipment?.Primary == null || pawn.equipment.Primary.def.IsMeleeWeapon)) continue;
                                        ThingComp_CombatAI comp = pawn.AI();
                                        if (comp != null && comp.IsAIAutoControlled) comp.aiSearchAndDestroyMelee = newVal;
                                    }
                                };
                                yield return sdMelee;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Release escorts pawns.
        /// </summary>
        public void ReleaseEscorts(bool success)
        {
            for (int i = 0; i < escorts.Count; i++)
            {
                Pawn escort = escorts[i];
                if (escort == null || escort.Destroyed || escort.Dead || escort.Downed || escort.mindState.duty == null)
                {
                    continue;
                }
                if (escort.mindState.duty.focus == parent)
                {
                    if (success)
                    {
                        escort.AI().releasedTick = GenTicks.TicksGame;
                    }
                    escort.AI().duties.FinishAllDuties(CombatAI_DutyDefOf.CombatAI_Escort, parent);
                }
            }
            if (success)
            {
                Predicate<Thing> validator = t =>
                {
                    if (!t.HostileTo(selPawn))
                    {
                        ThingComp_CombatAI comp = t.GetComp_Fast<ThingComp_CombatAI>();
                        if (comp != null && comp.IsSapping && comp.sapperNodes.Count > 3)
                        {
                            ReleaseEscorts(false);
                            comp.cellBefore      = IntVec3.Invalid;
                            comp.sapperStartTick = GenTicks.TicksGame + 800;
                            comp.sapperNodes.Clear();
                        }
                    }
                    return false;
                };
                    CombatAI.Compatibility.CompatHelpers.RegionwiseBFSWorker_NoOut(selPawn.Position, selPawn.Map, ThingRequest.ForGroup(ThingRequestGroup.Pawn), PathEndMode.InteractionCell, TraverseParms.For(selPawn), validator, null, 1, 4, 15);
            }
            escorts.Clear();
        }


        /// <summary>
        ///     Add enemy targeting self to Env data.
        /// </summary>
        /// <param name="enemy"></param>
        /// <param name="ticksToBurst"></param>
        public void Notify_BeingTargeted(Thing enemy, Verb verb)
        {
            if (enemy != null && !enemy.Destroyed)
            {
                data.BeingTargeted(enemy);
                if (Rand.Chance(0.15f) && (selPawn.mindState.duty.Is(DutyDefOf.Defend) || selPawn.mindState.duty.Is(DutyDefOf.Escort)))
                {
                    StartAggroCountdown(enemy);
                }
            }
            else
            {
                Log.Error($"{selPawn} received a null thing in Notify_BeingTargeted");
            }
        }

        /// <summary>
        ///     Enqueue enemy for reaction processing.
        /// </summary>
        /// <param name="things">Spotted enemy</param>
        public void Notify_Enemy(AIEnvAgentInfo info)
        {
            if (!scanning)
            {
                Log.Warning($"ISMA: Notify_EnemiesVisible called while not scanning. ({allEnemies.Count}, {Thread.CurrentThread.ManagedThreadId})");
                return;
            }
            if (info.thing is Pawn enemy)
            {
                // skip if the enemy is downed
                if (enemy.Downed)
                {
                    return;
                }
                // skip for children
                DevelopmentalStage stage = enemy.DevelopmentalStage;
                if (stage <= DevelopmentalStage.Child && stage != DevelopmentalStage.None)
                {
                    return;
                }
            }
            if (allEnemies.TryGetValue(info.thing, out AIEnvAgentInfo store))
            {
                info = store.Combine(info);
            }
            allEnemies[info.thing] = info;
        }

        /// <summary>
        ///     Enqueue ally for reaction processing.
        /// </summary>
        /// <param name="things">Spotted enemy</param>
        public void Notify_Ally(AIEnvAgentInfo info)
        {
            if (!scanning)
            {
                Log.Warning($"ISMA: Notify_EnemiesVisible called while not scanning. ({allEnemies.Count}, {Thread.CurrentThread.ManagedThreadId})");
                return;
            }
            if (info.thing is Pawn ally)
            {
                // skip if the ally is downed
                if (ally.Downed)
                {
                    return;
                }
                // skip for children
                DevelopmentalStage stage = ally.DevelopmentalStage;
                if (stage <= DevelopmentalStage.Child && stage != DevelopmentalStage.None)
                {
                    return;
                }
            }
            if (allAllies.TryGetValue(info.thing, out AIEnvAgentInfo store))
            {
                info = store.Combine(info);
            }
            allAllies[info.thing] = info;
        }

        /// <summary>
        ///     Called to notify a wait job started by reaction has ended. Will reduce the reaction cooldown.
        /// </summary>
        public void Notify_WaitJobEnded()
        {
            lastInterupted -= 30;
        }

        /// <summary>
        ///     Called when the parent sightreader group has changed.
        ///     Should only be called from SighTracker/SightGrid.
        /// </summary>
        /// <param name="reader">The new sightReader</param>
        public void Notify_SightReaderChanged(SightTracker.SightReader reader)
        {
            sightReader = reader;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            if (Finder.Settings.Debug)
            {
                PersonalityTacker.PersonalityResult personality = parent.GetCombatPersonality();
                Scribe_Deep.Look(ref personality, "personality");
            }
            Scribe_Deep.Look(ref data, "AIAgentData.0");
            data ??= new AIAgentData();
            Scribe_Deep.Look(ref duties, "duties2");
            Scribe_Deep.Look(ref abilities, "abilities2");
            Scribe_TargetInfo.Look(ref forcedTarget, "forcedTarget");
            Scribe_Values.Look(ref aiAutoControl, "aiAutoControl", false);
            Scribe_Values.Look(ref aiSearchAndDestroy, "aiSearchAndDestroy", false);
            Scribe_Values.Look(ref aiSearchAndDestroyMelee, "aiSearchAndDestroyMelee", false);
            if (duties == null)
            {
                duties = new Pawn_CustomDutyTracker(selPawn);
            }
            if (abilities == null)
            {
                abilities = new Pawn_AbilityCaster(selPawn);
            }
            duties.pawn    = selPawn;
            abilities.pawn = selPawn;
        }

        private void TryStartSapperJob()
        {
            bool failed = sapperNodes.Count == 0 || (sightReader.GetVisibilityToFriendlies(cellAhead) > 0 && GenTicks.TicksGame - sapperStartTick > 1000);
            if (failed)
            {
                ReleaseEscorts(false);
                cellBefore      = IntVec3.Invalid;
                sapperStartTick = -1;
                sapperNodes.Clear();
                return;
            }
            if (IsDeadOrDowned || !(selPawn.mindState.duty.Is(DutyDefOf.AssaultColony) || selPawn.mindState.duty.Is(CombatAI_DutyDefOf.CombatAI_AssaultPoint) || selPawn.mindState.duty.Is(DutyDefOf.AssaultThing)) || selPawn.CurJob.Is(JobDefOf.Wait_Combat) || selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Cover) || selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Retreat)  || selPawn.CurJob.Is(CombatAI_JobDefOf.CombatAI_Goto_Duck))
            {
                ReleaseEscorts(false);
                cellBefore      = IntVec3.Invalid;
                sapperStartTick = -1;
                sapperNodes.Clear();
                return;
            }
            Map   map     = selPawn.Map;
            Thing blocker = sapperNodes[0].GetEdifice(map);
            if (blocker != null)
            {
                Job                                 job         = null;
                float                               miningSkill = selPawn.GetSkillLevelSafe(SkillDefOf.Mining, 0);
                PersonalityTacker.PersonalityResult personality = parent.GetCombatPersonality();
                if (findEscorts && Rand.Chance(1 - Maths.Min(escorts.Count / (Maths.Max(miningSkill, 7) * personality.sapping), 0.85f)))
                {
                    int     count       = escorts.Count;
                    int     countTarget = 7 + Mathf.FloorToInt(Maths.Max(miningSkill, 7) * personality.sapping) + Maths.Min(sapperNodes.Count, 10);
                    Faction faction     = selPawn.Faction;
                    Predicate<Thing> validator = t =>
                    {
                        if (count < countTarget && t.Faction == faction && t is Pawn ally && !ally.Destroyed && !ally.CurJobDef.Is(JobDefOf.Mine) && !ally.IsColonist && ally.def != CombatAI_ThingDefOf.Mech_Tunneler && ally.mindState?.duty?.def != CombatAI_DutyDefOf.CombatAI_Escort 
                            && (sightReader == null || sightReader.GetAbsVisibilityToEnemies(ally.Position) == 0) 
                            && ally.GetSkillLevelSafe(SkillDefOf.Mining, 0) < miningSkill)
                        {
                            ThingComp_CombatAI comp = ally.AI();
                            if (comp?.duties != null && comp.duties?.Any(CombatAI_DutyDefOf.CombatAI_Escort) == false && !comp.IsSapping && GenTicks.TicksGame - comp.releasedTick > 600)
                            {
                                Pawn_CustomDutyTracker.CustomPawnDuty custom = CustomDutyUtility.Escort(selPawn, 20, 100, 600 + Mathf.CeilToInt(12 * selPawn.Position.DistanceTo(cellBefore)) + 540 * sapperNodes.Count + Rand.Int % 600);
                                if (ally.TryStartCustomDuty(custom))
                                {
                                    escorts.Add(ally);
                                }
                                if (comp.duties.curCustomDuty?.duty != duties.curCustomDuty?.duty)
                                {
                                    count += 4;
                                }
                                else
                                {
                                    count++;
                                }
                            }
                            return count == countTarget;
                        }
                        return false;
                    };
                    CombatAI.Compatibility.CompatHelpers.RegionwiseBFSWorker_NoOut(selPawn.Position, map, ThingRequest.ForGroup(ThingRequestGroup.Pawn), PathEndMode.InteractionCell, TraverseParms.For(selPawn), validator, null, 1, 10, 40);
                }
                float visibility = sightReader.GetAbsVisibilityToEnemies(cellBefore);
                if (!Mod_CE.active &&  (visibility > 0 || miningSkill < 9) && (escorts.Count >= 2 || miningSkill == 0))
                {
                    job = TryStartRemoteSapper(selPawn, blocker, sapperNodes[0], cellBefore);
                }
                if (job == null)
                {
                    // if no remote sapping job has been started, start a melee sapping job. If that's not possible release held escorts
                    if (visibility <= 1e-2f && miningSkill > 0)
                    {
                        job                    = DigUtility.PassBlockerJob(selPawn, blocker, cellBefore, true, true);
                        job.playerForced       = true;
                        job.expiryInterval     = 3600;
                        job.maxNumMeleeAttacks = 300;
                        if (!IsPerformingMeleeAnimation(selPawn))
                        {
                            selPawn.jobs.StartJob(job, JobCondition.InterruptForced);
                        }
                    }
                    else
                    {
                        ReleaseEscorts(false);
                        cellBefore      = IntVec3.Invalid;
                        sapperStartTick = -1;
                        sapperNodes.Clear();
                    }
                }
                if (!Mod_CE.active && job != null && job.def == JobDefOf.UseVerbOnThing)
                {
                    TryStartSapperJobForEscort(blocker);
                }
            }
        }

        /// <summary>
        /// Tries to start ranged sapping jobs for escorting pawns
        /// </summary>
        /// <param name="blocker">The path blocker</param>
        private void TryStartSapperJobForEscort(Thing blocker)
        {
            foreach (Pawn ally in escorts)
            {
                if (!ally.Destroyed && ally.Spawned && !ally.Downed && !ally.Dead && !ally.IsUsingVerb() && sightReader.GetAbsVisibilityToEnemies(ally.Position) == 0)
                {
                    TryStartRemoteSapper(ally, blocker, sapperNodes[0], cellBefore);
                }
            }
        }
        
        /// <summary>
        /// Starts remote sapping for a pawn given a blocker 
        /// </summary>
        /// <param name="blocker"></param>
        /// <returns></returns>
        private static Job TryStartRemoteSapper(Pawn pawn, Thing blocker, IntVec3 cellBlocker, IntVec3 cellBefore)
        {
            Verb verb = pawn.TryGetAttackVerb();
            if (verb.IsRangedSappingCompatible() && (verb.verbProps.burstShotCount > 1 || !pawn.RaceProps.IsMechanoid))
            {
                CastPositionRequest request = new CastPositionRequest();
                request.verb               = verb;
                request.caster             = pawn;
                request.target             = blocker;
                request.maxRangeFromTarget = 10;
                Vector3 dir = (cellBefore - cellBlocker).ToVector3();
                request.validator = cell =>
                {
                    return Mathf.Abs(Vector3.Angle((cell - cellBlocker).ToVector3(), dir)) <= 45f && cellBefore.DistanceToSquared(cell) >= 9;
                };
                try
                {
                    if (CastPositionFinder.TryFindCastPosition(request, out IntVec3 loc))
                    {
                        if (IsPerformingMeleeAnimation(pawn))
                        {
                            return null;
                        }
                        Job job = JobMaker.MakeJob(JobDefOf.UseVerbOnThing);
                        job.targetA             = blocker;
                        job.targetB             = loc;
                        job.verbToUse           = verb;
                        job.preventFriendlyFire = true;
                        job.expiryInterval      = JobGiver_AIFightEnemy.ExpiryInterval_ShooterSucceeded.RandomInRange;
                        pawn.jobs.StartJob(job, JobCondition.InterruptForced);
                        for (int i = 0; i < 4; i++)
                        {
                            job                     = JobMaker.MakeJob(JobDefOf.UseVerbOnThing);
                            job.targetA             = blocker;
                            job.targetB             = loc;
                            job.verbToUse           = verb;
                            job.preventFriendlyFire = true;
                            job.expiryInterval      = JobGiver_AIFightEnemy.ExpiryInterval_ShooterSucceeded.RandomInRange;
                            pawn.jobs.jobQueue.EnqueueFirst(job);
                        }
                        return job;
                    }
                }
                catch (Exception er)
                {
                    Log.Error($"1. {er}");
                }
            }
            return null;
        }

        #region TimeStamps

        /// <summary>
        ///     When the last injury occured/damage.
        /// </summary>
        private int lastTookDamage;
        /// <summary>
        ///     When the last scan occured. SightGrid is responisble for these scan cycles.
        /// </summary>
        private int lastScanned;
        /// <summary>
        ///     When did this comp last interupt the parent pawn. IE: reacted, retreated, etc.
        /// </summary>
        private int lastInterupted;
        /// <summary>
        ///     When the pawn was last order to retreat by CAI.
        /// </summary>
        private int lastRetreated;
        /// <summary>
        ///     Last tick CAI interrupted a CE reload to seek cover.
        /// </summary>
        private int lastCEReloadCoverTick;
        /// <summary>
        ///     Last tick any enemies where reported in a scan.
        /// </summary>
        private int lastSawEnemies;
        /// <summary>
        ///     The general direction of enemies last time the pawn reacted.
        /// </summary>
        private Vector2 prevEnemyDir = Vector2.zero;
        /// <summary>
        ///     Tick when this pawn was released as an escort.
        /// </summary>
        private int releasedTick;

        #endregion

#if DEBUG_REACTION
        /*
         * Debug only vars.
         */

        public override void DrawGUIOverlay()
        {
            if (Finder.Settings.Debug && Finder.Settings.Debug_ValidateSight && parent is Pawn pawn)
            {
                base.DrawGUIOverlay();
                Verb  verb = pawn.CurrentEffectiveVerb;
                float sightRange = Maths.Min(SightUtility.GetSightRadius(pawn).scan, !verb.IsMeleeAttack ? verb.EffectiveRange : 15);
                float sightRangeSqr = sightRange * sightRange;
                if (sightRange != 0 && verb != null)
                {
                    Vector3 drawPos = pawn.DrawPos;
                    IntVec3 shiftedPos = PawnPathUtility.GetMovingShiftedPosition(pawn, 30);
                    List<Pawn> nearbyVisiblePawns = pawn.Position.ThingsInRange(pawn.Map, TrackedThingsRequestCategory.Pawns, sightRange)
                        .Select(t => t as Pawn)
                        .Where(p => !p.Dead && !p.Downed && PawnPathUtility.GetMovingShiftedPosition(p, 60).DistanceToSquared(shiftedPos) < sightRangeSqr && verb.CanHitTargetFrom(shiftedPos, PawnPathUtility.GetMovingShiftedPosition(p, 60)) && p.HostileTo(pawn))
                        .ToList();
                    Gui.GUIUtility.ExecuteSafeGUIAction(() =>
                    {
                        Vector2 drawPosUI = drawPos.MapToUIPosition();
                        Text.Font = GameFont.Tiny;
                        string state = GenTicks.TicksGame - lastInterupted > 120 ? "<color=blue>O</color>" : "<color=yellow>X</color>";
                        Widgets.Label(new Rect(drawPosUI.x - 25, drawPosUI.y - 15, 50, 30), $"{state}/{_visibleEnemies.Count}:{_last}:{data.AllEnemies.Count}:{data.NumAllies}:{data.BeingTargetedBy.Count}");
                    });
                    bool    bugged = nearbyVisiblePawns.Count != _visibleEnemies.Count;
                    Vector2 a = drawPos.MapToUIPosition();
                    if (bugged)
                    {
                        Rect    rect;
                        Vector2 b;
                        Vector2 mid;
                        foreach (Pawn other in nearbyVisiblePawns.Where(p => !_visibleEnemies.Contains(p)))
                        {
                            b = other.DrawPos.MapToUIPosition();
                            Widgets.DrawLine(a, b, Color.red, 1);

                            mid = (a + b) / 2;
                            rect = new Rect(mid.x - 25, mid.y - 15, 50, 30);
                            Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                            Widgets.DrawBox(rect);
                            Widgets.Label(rect, $"<color=red>Errored</color>.  {Math.Round(other.Position.DistanceTo(pawn.Position), 1)}");
                        }
                    }
                    bool selected = Find.Selector.SelectedPawns.Contains(pawn);
                    if (bugged || selected)
                    {
                        GenDraw.DrawRadiusRing(pawn.Position, sightRange);
                    }
                    if (selected)
                    {
                        for (int i = 1; i < _path.Count; i++)
                        {
                            Widgets.DrawBoxSolid(new Rect((_path[i - 1].ToVector3().Yto0() + new Vector3(0.5f, 0, 0.5f)).MapToUIPosition() - new Vector2(5, 5), new Vector2(10, 10)), _colors[i]);
                            Widgets.DrawLine((_path[i - 1].ToVector3().Yto0() + new Vector3(0.5f, 0, 0.5f)).MapToUIPosition(), (_path[i].ToVector3().Yto0() + new Vector3(0.5f, 0, 0.5f)).MapToUIPosition(), Color.white, 1);
                        }
                        if (_path.Count > 0)
                        {
                            Vector2 v = pawn.DrawPos.Yto0().MapToUIPosition();
                            Widgets.DrawLine((_path.Last().ToVector3().Yto0() + new Vector3(0.5f, 0, 0.5f)).MapToUIPosition(), v, _colors.Last(), 1);
                            Widgets.DrawBoxSolid(new Rect(v - new Vector2(5, 5), new Vector2(10, 10)), _colors.Last());
                        }
//						int     index = 0;
                        foreach (AIEnvAgentInfo ally in data.AllAllies)
                        {
                            if (ally.thing != null)
                            {
                                Vector2 b = ally.thing.DrawPos.MapToUIPosition();
                                Widgets.DrawLine(a, b, Color.green, 1);

                                Vector2 mid = (a + b) / 2;
                                Rect    rect = new Rect(mid.x - 25, mid.y - 15, 50, 30);
                                Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                                Widgets.DrawBox(rect);
                                DamageReport report = DamageUtility.GetDamageReport(ally.thing);
                                if (report.IsValid)
                                {
                                    Widgets.Label(rect, $"{Math.Round(report.SimulatedDamage(armor), 2)}");
                                }
                            }
                        }
                        AIEnvThings enemies = data.AllEnemies;
                        foreach (AIEnvAgentInfo enemy in enemies)
                        {
                            if (enemy.thing != null)
                            {
                                Vector2 b = enemy.thing.DrawPos.MapToUIPosition();
                                Widgets.DrawLine(a, b, Color.yellow, 1);

                                Vector2 mid = (a + b) / 2;
                                Rect    rect = new Rect(mid.x - 25, mid.y - 15, 50, 30);
                                Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                                Widgets.DrawBox(rect);
                                DamageReport report = DamageUtility.GetDamageReport(enemy.thing);
                                if (report.IsValid)
                                {
                                    Widgets.Label(rect, $"{Math.Round(report.SimulatedDamage(armor), 2)}");
                                }
                            }
                        }
                        foreach (Thing enemy in data.BeingTargetedBy)
                        {
                            if (enemy != null && enemy.TryGetAttackVerb() is Verb enemyVerb && ThreatUtility.GetEnemyAttackTargetId(enemy) == selPawn.thingIDNumber)
                            {
                                Vector2 b = enemy.DrawPos.MapToUIPosition();
                                Ray2D   ray = new Ray2D(a, b - a);
                                float   dist = Vector2.Distance(a, b);
                                if (dist > 0)
                                {
                                    for (int i = 1; i < dist; i++)
                                    {
                                        Widgets.DrawLine(ray.GetPoint(i - 1), ray.GetPoint(i), i % 2 == 1 ? Color.black : Color.magenta, 2);
                                    }
                                    Vector2 mid = (a + b) / 2;
                                    Rect    rect = new Rect(mid.x - 25, mid.y - 15, 50, 30);
                                    Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.2f, 0.2f, 0.8f));
                                    Widgets.DrawBox(rect);
                                    DamageReport report = DamageUtility.GetDamageReport(enemy);
                                    if (report.IsValid)
                                    {
                                        Widgets.Label(rect, $"{Math.Round(report.SimulatedDamage(armor), 2)}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private readonly HashSet<Pawn> _visibleEnemies = new HashSet<Pawn>();
        private readonly List<IntVec3> _path = new List<IntVec3>();
        private readonly List<Color>   _colors = new List<Color>();
#endif
    }
}
