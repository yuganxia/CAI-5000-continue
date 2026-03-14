using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;
using CombatAI.Compatibility;
namespace CombatAI
{
    public class WallGrid : MapComponent
    {
        private readonly CellIndices cellIndices;
        private readonly float[]     grid;
        private readonly float[]     gridNoDoors;

        public WallGrid(Map map) : base(map)
        {
            cellIndices = map.cellIndices;
            grid        = new float[cellIndices.NumGridCells];
            gridNoDoors = new float[cellIndices.NumGridCells];
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // Populate wall grids for existing things on the map so CanBeSeenOver
            // and GetFillCategory return correct values immediately after load.
            try
            {
                List<Thing> all = map.listerThings.AllThings;
                for (int i = 0; i < all.Count; i++)
                {
                    Thing t = all[i];
                    if (t != null && t.Spawned)
                    {
                        RecalculateCell(t.Position, t);
                    }
                }
            }
            catch (Exception er)
            {
                Log.Error($"WallGrid: FinalizeInit failed to populate grid: {er}");
            }
        }

        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            // Doors change their open percent frequently; ensure the grid is updated for doors.
            if (Find.TickManager != null && Find.TickManager.TicksGame % 30 == 0)
            {
                try
                {
                    List<Thing> all = map.listerThings.AllThings;
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i] is Building_Door door && door.Spawned && !door.Destroyed)
                        {
                            RecalculateCell(door.Position, door);
                        }
                    }
                }
                catch (Exception) { }
            }
        }

        public float this[IntVec3 cell]
        {
            get => this[cellIndices.CellToIndex(cell)];
        }

        public float this[int index]
        {
            get => grid[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FillCategory GetFillCategory(IntVec3 cell)
        {
            return GetFillCategory(cellIndices.CellToIndex(cell));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FillCategory GetFillCategory(int index)
        {
            float f = grid[index];
            if (f == 0)
            {
                return FillCategory.None;
            }
            if (f < 1f)
            {
                return FillCategory.Partial;
            }
            return FillCategory.Full;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FillCategory GetFillCategoryNoDoors(IntVec3 cell)
        {
            return GetFillCategoryNoDoors(cellIndices.CellToIndex(cell));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FillCategory GetFillCategoryNoDoors(int index)
        {
            float f = gridNoDoors[index];
            if (f == 0)
            {
                return FillCategory.None;
            }
            if (f < 1f)
            {
                return FillCategory.Partial;
            }
            return FillCategory.Full;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanBeSeenOver(IntVec3 cell)
        {
            return CanBeSeenOver(cellIndices.CellToIndex(cell));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanBeSeenOver(int index)
        {
            return grid[index] < 0.998f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanBeSeenOverNoDoors(IntVec3 cell)
        {
            return CanBeSeenOverNoDoors(cellIndices.CellToIndex(cell));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CanBeSeenOverNoDoors(int index)
        {
            return gridNoDoors[index] < 0.998f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecalculateCell(IntVec3 cell, Thing t)
        {
            RecalculateCell(cellIndices.CellToIndex(cell), t);
        }
        public void RecalculateCell(int index, Thing t)
        {
            IntVec3 cell = cellIndices.IndexToCell(index);
            // Defensive: if the provided thing is a transient/non-structural overlay
            // (e.g., fire, filth, mote) which has no fillPercent and is not a Building
            // nor otherwise impassable, try to find a structural thing at the same
            // cell to use for fill calculation. If none found, fall back to terrain.
            if (t != null && t.def.fillPercent <= 0f && !(t is Building) && t.def.Fillage != FillCategory.Full && t.def.passability != Traversability.Impassable)
            {
                try
                {
                    List<Thing> things = map.thingGrid.ThingsListAt(cell);
                    Thing candidate = null;
                    for (int i = 0; i < things.Count; i++)
                    {
                        var tt = things[i];
                        if (tt == null || tt == t) continue;
                        if (tt is Building) { candidate = tt; break; }
                        if (tt.def.Fillage == FillCategory.Full) { candidate = tt; break; }
                        if (tt.def.fillPercent > 0f) { candidate = tt; break; }
                        if (tt.def.passability == Traversability.Impassable) { candidate = tt; break; }
                        if (tt is Building_Door) { candidate = tt; break; }
                    }
                    if (candidate != null)
                    {
                        t = candidate;
                    }
                    else
                    {
                        // No structural thing at this cell; treat as no thing so terrain is used.
                        t = null;
                    }
                }
                catch (Exception) { t = null; }
            }

            if (t != null)
            {
                if (t.def.plant != null)
                {
                    if (t.def.plant.IsTree)
                    {
                        if (t is Plant plant)
                        {
                            gridNoDoors[index] = grid[index] = plant.Growth * t.def.fillPercent / 4f;
                        }
                        else
                        {
                            gridNoDoors[index] = grid[index] = t.def.fillPercent / 4f;
                        }
                    }
                }
                else if (t is Building_Door door)
                {
                    grid[index]        = 1 - DoorCompat.GetOpenPct(door);
                    gridNoDoors[index] = 0;
                }
                else if (t is Building ed && ed.def.Fillage == FillCategory.Full)
                {
                    gridNoDoors[index] = grid[index] = 1.0f;
                }
                else
                {
                    gridNoDoors[index] = grid[index] = t.def.fillPercent;
                }
            }
            else
            {
                // Default to terrain fill if no thing is present at this cell.
                float terrainFill = 0f;
                try
                {
                    var terrain = map.terrainGrid?.TerrainAt(cellIndices.IndexToCell(index));
                    if (terrain != null)
                    {
                        if (terrain.passability == Traversability.Impassable)
                        {
                            terrainFill = 1.0f;
                        }
                        else
                        {
                            // some terrains may partially block (not common) - leave as 0 for now
                            terrainFill = 0f;
                        }
                    }
                }
                catch (Exception) { }
                gridNoDoors[index] = grid[index] = terrainFill;
            }
        }

        public void Notify_ThingSpawned(Thing t)
        {
            try
            {
                if (t != null && t.Spawned)
                {
                    RecalculateCell(t.Position, t);
                }
            }
            catch (Exception) { }
        }

        public void Notify_ThingDespawned(Thing t)
        {
            try
            {
                if (t != null)
                {
                    RecalculateCell(t.Position, null);
                }
            }
            catch (Exception) { }
        }

        public void Notify_TerrainChanged()
        {
            // Recalculate whole grid based on terrain and existing things. Terrain changes are rare.
            try
            {
                int count = cellIndices.NumGridCells;
                // first, reset terrain-based values
                for (int i = 0; i < count; i++)
                {
                    grid[i] = gridNoDoors[i] = 0f;
                }
                // apply terrain
                for (int i = 0; i < count; i++)
                {
                    IntVec3 cell = cellIndices.IndexToCell(i);
                    var terrain = map.terrainGrid?.TerrainAt(cell);
                    if (terrain != null && terrain.passability == Traversability.Impassable)
                    {
                        grid[i] = gridNoDoors[i] = 1.0f;
                    }
                }
                // apply things on top
                List<Thing> all = map.listerThings.AllThings;
                for (int j = 0; j < all.Count; j++)
                {
                    Thing tt = all[j];
                    if (tt != null && tt.Spawned)
                    {
                        RecalculateCell(tt.Position, tt);
                    }
                }
            }
            catch (Exception) { }
        }
    }
}
