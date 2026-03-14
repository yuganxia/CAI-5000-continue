using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.Assertions;
using System.Reflection;
using CombatAI.Compatibility;
using Verse;
using RimWorld;
using RimWorld.Planet;
namespace CombatAI
{
	[StaticConstructorOnStartup]
	public class MapComponent_FogGrid : MapComponent
	{
		private const int SECTION_SIZE = 16;

		private static          float     zoom;
		private static readonly Texture2D fogTex;
		private static readonly Mesh      mesh;
		private static readonly Shader    fogShader;

		private readonly object        _lockerSpots = new object();
		private readonly AsyncActions  asyncActions;
		private readonly ISection[][]  grid2d;
		private readonly Rect          mapRect;
		private readonly List<Vector3> spotBuffer;

		private readonly HashSet<ITempSpot> spotsQueued;
		private          bool               alive;
		public           CellIndices        cellIndices;
		public           GlowGrid           glow;

		public  bool[] grid;
		private HashSet<int> vanillaFogIndices;
		private bool   initialized;
		private bool   previousFogEnabled;
		private bool   previousUseVanillaUnexplored;
		private bool   vanillaRebuildPending;

		private          Rect      mapScreenRect;
		private          bool      ready;
		public           SightGrid sight;
		private volatile int       ticksGame;
		private volatile float     tickRateMultiplier;
		private          int       updateNum;
		private volatile int       screenMinU;
		private volatile int       screenMinV;
		private volatile int       screenMaxU;
		private volatile int       screenMaxV;
		private int                backgroundNextU;
		private int                backgroundNextV;
		public           WallGrid  walls;


		static MapComponent_FogGrid()
		{
			fogTex = new Texture2D(SECTION_SIZE, SECTION_SIZE, TextureFormat.RGBAFloat, true);
			fogTex.Apply();
			mesh      = CombatAI_MeshMaker.NewPlaneMesh(Vector2.zero, Vector2.one * SECTION_SIZE);
			fogShader = AssetBundleDatabase.Get<Shader>("assets/fogshader.shader");
			Assert.IsNotNull(fogShader);
		}

		public MapComponent_FogGrid(Map map) : base(map)
		{
			alive        = true;
			spotsQueued  = new HashSet<ITempSpot>(16);
			spotBuffer   = new List<Vector3>(256);
			asyncActions = new AsyncActions();
			cellIndices  = map.cellIndices;
			mapRect      = new Rect(0, 0, CellIndicesCompat.GetMapSizeX(cellIndices), CellIndicesCompat.GetMapSizeZ(cellIndices));
			grid         = new bool[map.cellIndices.NumGridCells];
			grid2d       = new ISection[Mathf.CeilToInt(CellIndicesCompat.GetMapSizeX(cellIndices) / (float)SECTION_SIZE)][];
		}

		internal static bool IsGravshipLandingSelectionActive
		{
			get
			{
				if (!ModsConfig.OdysseyActive) return false;
				try
				{
					if (Current.ProgramState != ProgramState.Playing) return false;
					var ctrl = Find.GravshipController;
					return ctrl != null
						&& ctrl.LandingAreaConfirmationInProgress
						&& !WorldComponent_GravshipController.CutsceneInProgress
						&& Current.Game.Gravship != null;
				}
				catch { return false; }
			}
		}

		internal bool EffectiveUseVanillaUnexplored =>
			Finder.Settings.FogOfWar_UseVanillaUnexplored && !IsGravshipLandingSelectionActive;

		public float SkyGlow
		{
			get;
			private set;
		}

		public override void FinalizeInit()
		{
			base.FinalizeInit();
			asyncActions.Start();
		}

		public override void ExposeData()
		{
			base.ExposeData();
			try
			{
				if (Scribe.mode == LoadSaveMode.Saving)
				{
					RestoreAllAppliedWarFog();
				}
				if (Scribe.mode == LoadSaveMode.PostLoadInit)
				{
					try
					{
						initialized = false;
						ready = false;
						RecomputeOnceMainThread();
						if (Finder.Settings.FogOfWar_UseVanillaUnexplored)
						{
							ApplyVanillaUnexploredOverlay();
						}
					}
					catch (Exception e)
					{
						Log.Error($"MapComponent_FogGrid: error during PostLoadInit recompute: {e}");
					}
				}
			}
			catch (Exception e)
			{
				Log.Error($"MapComponent_FogGrid: ExposeData error: {e}");
			}
		}

		public bool IsFogged(IntVec3 cell)
		{
			return IsFogged(cellIndices.CellToIndex(cell));
		}
		public bool IsFogged(int index)
		{
			if (!Finder.Settings.FogOfWar_Enabled)
			{
				return false;
			}
			if (Finder.Settings.FogOfWar_DisableOnPlayerMap && map.IsPlayerHome)
			{
				return map.fogGrid?.IsFogged(index) ?? false;
			}
			if (index >= 0 && index < cellIndices.NumGridCells)
			{
				return grid[index];
			}
			return false;
		}



		public void RestoreAllAppliedWarFog()
		{
			// Clear CAI internal grid state so the next recompute will reapply
			// DeepWarFog (visual overlay) without modifying vanilla FogGrid.
			try
			{
				int cleared = 0;
				for (int i = 0; i < grid.Length; i++)
				{
					if (grid[i]) cleared++;
					grid[i] = false;
				}
				// mark sections dirty and zero their float cells so visuals update
				for (int u = 0; u < grid2d.Length; u++)
				{
					// inner arrays may not yet be initialized
					if (grid2d[u] == null) continue;
					for (int v = 0; v < grid2d[u].Length; v++)
					{
						var sec = grid2d[u][v];
						if (sec != null)
						{
							sec.dirty = true;
							for (int ci = 0; ci < sec.cells.Length; ci++) sec.cells[ci] = 0f;
						}
					}
				}
				// Force a full redraw so any DeepWarFog visual overlay is cleared.
				if (map != null && map.mapDrawer != null)
				{
					foreach (var cell in map.AllCells)
					{
						map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.FogOfWar | MapMeshFlagDefOf.Things);
					}
				}
				// If we modified the vanilla FogGrid earlier, try to restore those bits.
				try
				{
					if (vanillaFogIndices != null && vanillaFogIndices.Count > 0 && map?.fogGrid != null)
					{
						var fogGridField = typeof(FogGrid).GetField("fogGrid", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
						if (fogGridField != null)
						{
							object nativeFog = fogGridField.GetValue(map.fogGrid);
							if (nativeFog != null)
							{
								var t = nativeFog.GetType();
								MethodInfo setBool = t.GetMethod("Set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(int), typeof(bool) }, null)
									?? t.GetMethod("Set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(int) }, null);
								PropertyInfo indexer = t.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								foreach (int idx in vanillaFogIndices)
								{
									try
									{
										if (setBool != null)
										{
											var p = setBool.GetParameters();
											if (p.Length == 2) setBool.Invoke(nativeFog, new object[] { idx, false });
											else setBool.Invoke(nativeFog, new object[] { idx });
										}
										else if (indexer != null && indexer.CanWrite)
										{
											indexer.SetValue(nativeFog, false, new object[] { idx });
										}
									}
									catch { }
								}
							}
						}
					}
				}
				catch (Exception e)
				{
					Log.Error($"MapComponent_FogGrid: error while restoring vanilla FogGrid bits: {e}");
				}
				vanillaFogIndices?.Clear();
				if (Finder.Settings.Debug)
				{
					Log.Message($"MapComponent_FogGrid: cleared CAI grid state for map {map}, previously true count: {cleared}");
				}
			}
			catch (Exception e)
			{
				Log.Error($"MapComponent_FogGrid: error while clearing CAI grid after restore for map {map}: {e}");
			}
		}

		public void RecomputeOnceMainThread()
		{
			if (!ready)
			{
				sight = map.GetComp_Fast<SightTracker>()?.colonistsAndFriendlies;
				walls = map.GetComp_Fast<WallGrid>();
				glow  = map.glowGrid;
				ready = sight != null;
			}
			if (!ready) return;
			if (grid2d == null) return;
			for (int u = 0; u < grid2d.Length; u++)
			{
				var inner = grid2d[u];
				if (inner == null) continue;
				for (int v = 0; v < inner.Length; v++)
				{
					var sec = inner[v];
					if (sec == null) continue;
					sec.Update(true);
					sec.ApplyFogged();
				}
			}
			// ensure any queued main-thread actions run
			asyncActions.ExecuteMainThreadActions();
		}

		public void ApplyVanillaUnexploredOverlay()
		{
			try
			{
				// If enabled, write CAI's internal fog bits into the vanilla FogGrid's
				// internal bit array so vanilla mesh layers that read it will reflect
				// DeepWarFog as "unexplored".
				try
				{
					if (map?.fogGrid != null)
					{
						var fogGridField = typeof(FogGrid).GetField("fogGrid", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
						if (fogGridField != null)
						{
							object nativeFog = fogGridField.GetValue(map.fogGrid);
							if (nativeFog != null)
							{
								if (vanillaFogIndices == null) vanillaFogIndices = new HashSet<int>();
								var t = nativeFog.GetType();
								MethodInfo setBool = t.GetMethod("Set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(int), typeof(bool) }, null)
									?? t.GetMethod("Set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(int) }, null);
								PropertyInfo indexer = t.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								for (int i = 0; i < grid.Length; i++)
								{
									if (!grid[i]) continue;
									try
									{
										// if it's already set in vanilla fog, skip
										bool already = false;
										MethodInfo isSet = t.GetMethod("IsSet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(int) }, null);
										if (isSet != null)
										try { already = (bool)isSet.Invoke(nativeFog, new object[] { i }); } catch { already = false; }
									if (already) continue;
									if (setBool != null)
									{
										var p = setBool.GetParameters();
										if (p.Length == 2) setBool.Invoke(nativeFog, new object[] { i, true });
										else setBool.Invoke(nativeFog, new object[] { i });
									}
									else if (indexer != null && indexer.CanWrite)
									{
										indexer.SetValue(nativeFog, true, new object[] { i });
									}
									vanillaFogIndices.Add(i);
								}
								catch { }
							}
							}
						}
					}
				}
				catch (Exception e)
				{
					Log.Error($"MapComponent_FogGrid: error while applying vanilla unexplored overlay: {e}");
				}
				foreach (var c in map.AllCells)
				{
					map.mapDrawer.MapMeshDirty(c, MapMeshFlagDefOf.FogOfWar | MapMeshFlagDefOf.Things);
				}
				if (Finder.Settings.Debug) Log.Message($"MapComponent_FogGrid: Applied DeepWarFog visual overlay for map {map}");
			}
			catch (Exception e)
			{
				Log.Error($"MapComponent_FogGrid: error while applying DeepWarFog visual overlay for map {map}: {e}");
			}
		}

		public void ScheduleVanillaUnexploredRebuild()
		{
			if (!Finder.Settings.FogOfWar_Enabled) return;
			if (!EffectiveUseVanillaUnexplored) return;
			if (Finder.Settings.FogOfWar_DisableOnPlayerMap && map.IsPlayerHome) return;
			if (vanillaRebuildPending) return;
			vanillaRebuildPending = true;
			asyncActions.EnqueueMainThreadAction(() =>
			{
				vanillaRebuildPending = false;
				ApplyVanillaUnexploredOverlay();
			});
		}

		public void ApplyWarFogChangeMainThread(int index)
		{
			if (Finder.Settings.FogOfWar_DisableOnPlayerMap && map.IsPlayerHome)
			{
				return;
			}
			int mapSizeX = map.Size.x;
			int x = index % mapSizeX;
			int z = index / mapSizeX;
			IntVec3 cell = new IntVec3(x, 0, z);
			map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.FogOfWar | MapMeshFlagDefOf.Things);
			// If configured to use vanilla unexplored overlay, update vanilla FogGrid bit for this cell.
			if (EffectiveUseVanillaUnexplored && map?.fogGrid != null)
			{
				try
				{
					SetVanillaFogBit(index, this.grid[index]);
				}
				catch { }
			}
		}

		private void SetVanillaFogBit(int index, bool value)
		{
			var fogGridField = typeof(FogGrid).GetField("fogGrid", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (fogGridField == null) return;
			object nativeFog = fogGridField.GetValue(map.fogGrid);
			if (nativeFog == null) return;
			var t = nativeFog.GetType();
			MethodInfo setBool = t.GetMethod("Set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(int), typeof(bool) }, null)
				?? t.GetMethod("Set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(int) }, null);
			PropertyInfo indexer = t.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			try
			{
				if (setBool != null)
				{
					var p = setBool.GetParameters();
					if (p.Length == 2) setBool.Invoke(nativeFog, new object[] { index, value });
					else if ((bool) (setBool.Invoke(nativeFog, new object[] { index }) ?? false) != value)
					{
						// no-op fallback
					}
				}
				else if (indexer != null && indexer.CanWrite)
				{
					indexer.SetValue(nativeFog, value, new object[] { index });
				}
				if (value)
				{
					if (vanillaFogIndices == null) vanillaFogIndices = new HashSet<int>();
					vanillaFogIndices.Add(index);
				}
				else
				{
					vanillaFogIndices?.Remove(index);
				}
			}
			catch { }
		}

		public override void MapComponentUpdate()
		{
			if (Finder.Settings.FogOfWar_Enabled && !previousFogEnabled)
			{
				previousFogEnabled = true;
			}
			// if fog was enabled previously but now disabled, restore modified cells
			if (previousFogEnabled && !Finder.Settings.FogOfWar_Enabled)
			{
				asyncActions.EnqueueMainThreadAction(RestoreAllAppliedWarFog);
				previousFogEnabled = false;
				return;
			}

			// detect runtime changes to FogOfWar_UseVanillaUnexplored (or landing selection phase) and apply/restore as needed
			if (EffectiveUseVanillaUnexplored != previousUseVanillaUnexplored)
			{
				if (EffectiveUseVanillaUnexplored)
				{
					asyncActions.EnqueueMainThreadAction(ApplyVanillaUnexploredOverlay);
				}
				else
				{
					asyncActions.EnqueueMainThreadAction(RestoreAllAppliedWarFog);
				}
				previousUseVanillaUnexplored = EffectiveUseVanillaUnexplored;
			}
			if (!initialized && Finder.Settings.FogOfWar_Enabled)
			{
				initialized = true;
				for (int i = 0; i < grid2d.Length; i++)
				{
					grid2d[i] = new ISection[Mathf.CeilToInt(CellIndicesCompat.GetMapSizeZ(cellIndices) / (float)SECTION_SIZE)];
					for (int j = 0; j < grid2d[i].Length; j++)
					{
						grid2d[i][j] = new ISection(this, new Rect(new Vector2(i * SECTION_SIZE, j * SECTION_SIZE), Vector2.one * SECTION_SIZE), mapRect, mesh, fogTex, fogShader);
					}
				}
				asyncActions.EnqueueOffThreadAction(() =>
				{
					OffThreadLoop(0, 0, grid2d.Length, grid2d[0].Length);
				});
			}
			if (!alive || !Finder.Settings.FogOfWar_Enabled)
			{
				return;
			}
			if (Find.CurrentMap != map)
			{
				ready = false;
				return;
			}
			if (WorldRendererUtility.WorldRendered)
			{
				return;
			}
			ticksGame = GenTicks.TicksGame;
			try
			{
				tickRateMultiplier = Find.TickManager?.TickRateMultiplier ?? 1f;
			}
			catch
			{
				tickRateMultiplier = 1f;
			}
			if (!ready)
			{
				sight = map.GetComp_Fast<SightTracker>().colonistsAndFriendlies;
				walls = map.GetComp_Fast<WallGrid>();
				glow  = map.glowGrid;
				ready = sight != null;
			}
			base.MapComponentUpdate();
			if (ready)
			{
				SkyGlow = map.skyManager.CurSkyGlow;
				Rect     rect     = new Rect();
				CellRect cellRect = Find.CameraDriver.CurrentViewRect;
				rect.xMin     = Mathf.Clamp(cellRect.minX - SECTION_SIZE, 0, CellIndicesCompat.GetMapSizeX(cellIndices));
				rect.xMax     = Mathf.Clamp(cellRect.maxX + SECTION_SIZE, 0, CellIndicesCompat.GetMapSizeX(cellIndices));
				rect.yMin     = Mathf.Clamp(cellRect.minZ - SECTION_SIZE, 0, CellIndicesCompat.GetMapSizeZ(cellIndices));
				rect.yMax     = Mathf.Clamp(cellRect.maxZ + SECTION_SIZE, 0, CellIndicesCompat.GetMapSizeZ(cellIndices));
				mapScreenRect = rect;
				// publish visible section bounds for the off-thread loop to prioritize
				screenMinU = Mathf.FloorToInt(mapScreenRect.xMin / SECTION_SIZE);
				screenMinV = Mathf.FloorToInt(mapScreenRect.yMin / SECTION_SIZE);
				screenMaxU = Mathf.FloorToInt(mapScreenRect.xMax / SECTION_SIZE);
				screenMaxV = Mathf.FloorToInt(mapScreenRect.yMax / SECTION_SIZE);
				//mapScreenRect.ExpandedBy(32, 32);
				asyncActions.ExecuteMainThreadActions();
				zoom = Mathf.CeilToInt(Mathf.Clamp(CombatAI.Compatibility.CameraDriverCompat.GetRootPosY(Find.CameraDriver), 15, 30f));
				DrawFog(Mathf.FloorToInt(mapScreenRect.xMin / SECTION_SIZE), Mathf.FloorToInt(mapScreenRect.yMin / SECTION_SIZE), Mathf.FloorToInt(mapScreenRect.xMax / SECTION_SIZE), Mathf.FloorToInt(mapScreenRect.yMax / SECTION_SIZE));
			}
		}

		public void RevealSpot(IntVec3 cell, float radius, int duration)
		{
			ITempSpot spot = new ITempSpot();
			spot.center   = cell;
			spot.radius   = Maths.Max(radius, 1f) * Finder.Settings.FogOfWar_RangeMultiplier;
			spot.duration = duration;
			lock (_lockerSpots)
			{
				spotsQueued.Add(spot);
			}
		}

		public override void MapRemoved()
		{
			// restore any modified vanilla fog before removing
			RestoreAllAppliedWarFog();
			alive = false;
			asyncActions.Kill();
			base.MapRemoved();
		}

		private void DrawFog(int minU, int minV, int maxU, int maxV)
		{
				if (Finder.Settings.FogOfWar_DisableOnPlayerMap && map.IsPlayerHome && Find.CurrentMap == map)
				{
					return;
				}
			maxU = Mathf.Clamp(Maths.Max(maxU, minU + 1), 0, grid2d.Length - 1);
			minU = Mathf.Clamp(minU - 1, 0, grid2d.Length - 1);
			maxV = Mathf.Clamp(Maths.Max(maxV, minV + 1), 0, grid2d[0].Length - 1);
			minV = Mathf.Clamp(minV - 1, 0, grid2d[0].Length - 1);
			bool  update       = updateNum % 2 == 0;
			bool  updateForced = updateNum % 4 == 0;
			float color        = Finder.Settings.FogOfWar_FogColor;
			for (int u = minU; u <= maxU; u++)
			{
				for (int v = minV; v <= maxV; v++)
				{
					ISection section = grid2d[u][v];
					if (section.s_color != color)
					{
						section.ApplyColor();
					}
					if (updateForced || update && section.dirty)
					{
						section.ApplyFogged();
					}
					section.Draw(mapScreenRect);
				}
			}
			updateNum++;
		}

		private void OffThreadLoop(int minU, int minV, int maxU, int maxV)
		{
			Stopwatch       stopwatch = new Stopwatch();
			List<ITempSpot> spots     = new List<ITempSpot>();
			while (alive)
			{
				stopwatch.Restart();
				if (ready && Finder.Settings.FogOfWar_Enabled)
				{
					lock (_lockerSpots)
					{
						if (spotsQueued.Count > 0)
						{
							spots.AddRange(spotsQueued);
							spotsQueued.Clear();
						}
					}
					if (spots.Count > 0)
					{
						int ticks = ticksGame;
						while (spots.Count > 0)
						{
							ITempSpot spot = spots.Pop();
							Action<IntVec3, int, int, float> setAction = (cell, carry, dist, coverRating) =>
							{
								if (cell.InBounds(map))
								{
									int      u       = cell.x / SECTION_SIZE;
									int      v       = cell.z / SECTION_SIZE;
									ISection section = grid2d[u][v];
									if (section != null)
									{
										ITempCell tCell = new ITempCell();
										tCell.u         = (byte)(cell.x % SECTION_SIZE);
										tCell.v         = (byte)(cell.z % SECTION_SIZE);
										tCell.val       = Mathf.Clamp01(1f - cell.DistanceTo_Fast(spot.center) / spot.radius);
										tCell.timestamp = GenTicks.TicksGame;
										tCell.duration  = (short)spot.duration;
										section.extraCells.Add(tCell);
									}
								}
							};
							setAction(spot.center, 0, 0, 0);
							ShadowCastingUtility.CastWeighted(map, spot.center, setAction, Mathf.CeilToInt(spot.radius), 16, spotBuffer);
						}
					}
					// Prioritize updating visible sections, then a limited number of background sections per loop
					int visMinU = Mathf.Clamp(screenMinU - 1, 0, grid2d.Length - 1);
					int visMinV = Mathf.Clamp(screenMinV - 1, 0, grid2d[0].Length - 1);
					int visMaxU = Mathf.Clamp(screenMaxU + 1, 0, grid2d.Length - 1);
					int visMaxV = Mathf.Clamp(screenMaxV + 1, 0, grid2d[0].Length - 1);
					for (int u = visMinU; u <= visMaxU; u++)
					{
						for (int v = visMinV; v <= visMaxV; v++)
						{
							grid2d[u][v].Update();
						}
					}
					// Background update budget scales with tick rate multiplier
					int bgBudget = Mathf.Clamp(Mathf.CeilToInt(16f * tickRateMultiplier), 4, grid2d.Length * grid2d[0].Length);
					int totalU = grid2d.Length;
					int totalV = grid2d[0].Length;
					while (bgBudget > 0)
					{
						// wrap indices
						if (backgroundNextU >= totalU) { backgroundNextU = 0; backgroundNextV = 0; }
						grid2d[backgroundNextU][backgroundNextV].Update();
						bgBudget--;
						backgroundNextV++;
						if (backgroundNextV >= totalV) { backgroundNextV = 0; backgroundNextU++; }
					}
					stopwatch.Stop();
					float elapsed = (float)stopwatch.ElapsedTicks / Stopwatch.Frequency;
					float multiplier = tickRateMultiplier;
					if (multiplier < 1f) multiplier = 1f;
					float frameTarget = 0.016f / multiplier;
					float remaining = frameTarget - elapsed;
					if (remaining > 0f)
					{
						// If remaining is sizeable, sleep most of it then spin-wait for precision
						if (remaining > 0.005f)
						{
							int sleepMs = Mathf.FloorToInt((remaining - 0.003f) * 1000f);
							if (sleepMs > 0) Thread.Sleep(sleepMs);
						}
						// busy-wait until target reached (short duration)
						while (((float)stopwatch.ElapsedTicks / Stopwatch.Frequency) < frameTarget)
						{
							Thread.SpinWait(10);
						}
					}
				}
				else
				{
					Thread.Sleep(100);
				}
			}
		}

		private class ISection
		{
			public readonly  float[]              cells;
			private readonly MapComponent_FogGrid comp;
			public readonly  List<ITempCell>      extraCells;
			private readonly Material             mat;
			private readonly Mesh                 mesh;
			private readonly Vector3              pos;
			public           bool                 dirty = true;
			public           Rect                 rect;
			public           float                s_color;

			public ISection(MapComponent_FogGrid comp, Rect rect, Rect mapRect, Mesh mesh, Texture2D tex, Shader shader)
			{
				extraCells = new List<ITempCell>();
				this.comp  = comp;
				this.rect  = rect;
				this.mesh  = mesh;
				pos        = new Vector3(rect.position.x, AltitudeLayer.MapDataOverlay.AltitudeFor() , rect.position.y);
				mat        = new Material(shader);
				mat.SetVector("_Color", new Vector4(0.1f, 0.1f, 0.1f, 0.8f));
				mat.SetTexture("_Tex", tex);
				cells = new float[256];
				cells.Initialize();
				mat.SetFloatArray("_Fog", cells);
			}

			public void ApplyColor()
			{
				mat.SetVector("_Color", new Vector4(0.1f, 0.1f, 0.1f, s_color = Finder.Settings.FogOfWar_FogColor));
			}

			public void ApplyFogged()
			{
				mat.SetFloatArray("_Fog", cells);
				dirty = false;
			}

			public void Update(bool runOnMainThread = false)
			{
				var indicesNullable = comp.map?.cellIndices;
				if (!indicesNullable.HasValue)
				{
					return;
				}
				CellIndices indices = indicesNullable.Value;
				int         numGridCells = indices.NumGridCells;
				WallGrid    walls        = comp.walls;
				ITFloatGrid fogGrid      = comp.sight.gridFog;
				IntVec3     pos          = this.pos.ToIntVec3();
				IntVec3     loc;

				ColorInt[] glowGrid = CombatAI.Compatibility.GlowGridCompat.GetGlowGrid(comp.glow);
				float      glowSky  = comp.SkyGlow;
				bool       changed  = false;

				void SetCell(int x, int z, float glowOffset, float visibilityOffset, bool allowLowerValues)
				{
                        int index = indices.CellToIndex(loc = pos + new IntVec3(x, 0, z));
					if (index >= 0 && index < numGridCells)
					{
                        	float old           = cells[x * SECTION_SIZE + z];
                        	bool  isWall        = walls != null && !walls.CanBeSeenOver(index);
						float visRLimit     = 0;
                        	float visibility    = fogGrid.Get(index);
						float visibilityAdj = 0;
						for (int i = 0; i < 9; i++)
						{
							int adjIndex = index + CellIndicesCompat.GetMapSizeX(indices) * (i / 3 - 1) + i % 3 - 1;
							if (adjIndex >= 0 && adjIndex < numGridCells && !isWall && walls != null && walls.CanBeSeenOver(adjIndex))
							{
								visibilityAdj += fogGrid.Get(adjIndex);
							}
						}
						visibility = Maths.Max(visibilityAdj / 9, visibility) + visibilityOffset;
                        	if (glowSky < 1)
                        	{
                        		ColorInt glow = default(ColorInt);
                        		if (glowGrid != null && index >= 0 && index < glowGrid.Length)
                        		{
                        			glow = glowGrid[index];
                        		}
                        		float glowMax = Mathf.Max(Maths.Max(glow.r, glow.g), glow.b);
                        		float glowFactor = !isWall ? 1f : Mathf.Clamp01(glowMax / 255f * 3.6f);
                        		visRLimit = Mathf.Lerp(0, 0.5f, 1 - (Maths.Max(glowFactor, glowSky) + glowOffset));
                        	}
						float val = Maths.Max(1 - visibility, 0);
						// Determine fog state from visibility first, update internal grid,
						// then decide final rendering value based on the new fog state.
						bool prevFog = comp.grid != null && comp.grid[index];
						bool newFog = visibility <= visRLimit + 1e-3f;
						if (allowLowerValues || old >= val)
						{
							comp.grid[index] = newFog;
							if (prevFog != newFog)
							{
								int idx = index;
								if (runOnMainThread)
								{
									comp.ApplyWarFogChangeMainThread(idx);
								}
								else
								{
									comp.asyncActions.EnqueueMainThreadAction(() => comp.ApplyWarFogChangeMainThread(idx));
								}
							}
						}
						// If the CAI DeepWarFog flag is set for this cell, or the vanilla FogGrid
						// marks this cell as unexplored, force full 'unexplored' rendering.
						try
						{
							if (comp.grid != null && comp.grid[index])
							{
								val = 1f;
							}
							else if (!comp.EffectiveUseVanillaUnexplored && comp.map?.fogGrid != null && comp.map.fogGrid.IsFogged(index))
							{
								val = 1f;
							}
						}
						catch { }
						if (old != val)
						{
							changed = true;
							if (allowLowerValues)
							{
								if (val > old)
								{
									cells[x * SECTION_SIZE + z] = Maths.Min(old + 0.5f, val);
								}
								else
								{
									cells[x * SECTION_SIZE + z] = Maths.Max(old - 0.5f, val);
								}
							}
							else
							{
								cells[x * SECTION_SIZE + z] = Maths.Min(old, val);
							}
						}
					}
					else
					{
						cells[x * SECTION_SIZE + z] = 0f;
					}
				}

				if (fogGrid != null)
				{
					for (int x = 0; x < SECTION_SIZE; x++)
					{
						for (int z = 0; z < SECTION_SIZE; z++)
						{
							SetCell(x, z, 0, 0, true);
						}
					}
					int ticks = comp.ticksGame;
					int i     = 0;
					while (i < extraCells.Count)
					{
						ITempCell tCell = extraCells[i];
						if (tCell.timestamp + tCell.duration < ticks)
						{
							extraCells.RemoveAt(i);
							changed = true;
							continue;
						}
						i++;
						float fade = Mathf.Lerp(0.7f, 1.0f, 1f - (float)(GenTicks.TicksGame - tCell.timestamp) / tCell.duration);
						SetCell(tCell.u, tCell.v, 0.5f * fade * tCell.val, fade * tCell.val, false);
					}
				}
				dirty = changed;
			}

			public void Draw(Rect screenRect)
			{
				GenDraw.DrawMeshNowOrLater(mesh, pos, Quaternion.identity, mat, false);
			}
		}

		private struct ITempSpot : IEquatable<ITempSpot>
		{
			public IntVec3 center;
			public float   radius;
			public int     duration;

			public override bool Equals(object obj)
			{
				return obj is ITempSpot other && other.Equals(this);
			}

			public bool Equals(ITempSpot other)
			{
				return center == other.center;
			}

			public override int GetHashCode()
			{
				return center.GetHashCode();
			}
		}

		private struct ITempCell
		{
			public byte  u;
			public byte  v;
			public float val;
			public int   timestamp;
			public short duration;
		}
	}
}
