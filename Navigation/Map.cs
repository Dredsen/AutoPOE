using AStar;
using AStar.Options;
using ExileCore;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using ExileCore.Shared.Interfaces;
using GameOffsets;
using GameOffsets.Native;
using System.Collections.Concurrent;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using System;

namespace AutoPOE.Navigation
{
    public class Map
    {
        private Random _random = new Random();

        // Use HashSet much faster than List<T>
        private HashSet<uint> _blacklistItemIds = new HashSet<uint>();

        private readonly WorldGrid _worldGrid;
        private readonly PathFinder _pathFinder;

        // Use ConcurrentBag for thread-safe additions during parallel processing, should fix crash
        private readonly ConcurrentDictionary<string, ConcurrentBag<Vector2>> _tiles;

        private Chunk[,] _chunks;

        public IReadOnlyList<Chunk> Chunks { get; private set; }

        public Map()
        {
            TerrainData terrain = Core.GameController.IngameState.Data.Terrain;

            int gridWidth = ((int)terrain.NumCols - 1) * 23;
            int gridHeight = ((int)terrain.NumRows - 1) * 23;

            if (gridWidth % 2 != 0)
            {
                gridWidth++;
            }

            _worldGrid = new WorldGrid(gridHeight, gridWidth + 1);

            _pathFinder = new PathFinder(_worldGrid, new PathFinderOptions()
            {
                PunishChangeDirection = false,
                UseDiagonals = true,
                SearchLimit = gridWidth * gridHeight
            });

            PopulateWorldGrid(terrain, _worldGrid, Core.GameController.Memory);
            ProcessTileData(terrain, _tiles = new ConcurrentDictionary<string, ConcurrentBag<Vector2>>(), Core.GameController.Memory);
            InitializeChunks(10, _worldGrid.Width, _worldGrid.Height);

            Chunks = _chunks.Cast<Chunk>().ToList().AsReadOnly();
        }


        private static void PopulateWorldGrid(TerrainData terrain, WorldGrid worldGrid, IMemory memory)
        {
            byte[] layerMeleeBytes = memory.ReadBytes(terrain.LayerMelee.First, terrain.LayerMelee.Size);
            int currentByteOffset = 0;

            for (int row = 0; row < worldGrid.Height; ++row)
            {
                for (int column = 0; column < worldGrid.Width; column += 2)
                {
                    if (currentByteOffset + (column >> 1) >= layerMeleeBytes.Length) break;

                    byte tileValue = layerMeleeBytes[currentByteOffset + (column >> 1)];
                    worldGrid[row, column] = (short)((tileValue & 0xF) > 0 ? 1 : 0);
                    if (column + 1 < worldGrid.Width)
                        worldGrid[row, column + 1] = (short)((tileValue >> 4) > 0 ? 1 : 0);
                }
                currentByteOffset += terrain.BytesPerRow;
            }
        }

        private static void ProcessTileData(TerrainData terrain, ConcurrentDictionary<string, ConcurrentBag<Vector2>> tiles, IMemory memory)
        {
            TileStructure[] tileData = memory.ReadStdVector<TileStructure>(terrain.TgtArray);
            System.Threading.Tasks.Parallel.ForEach(Partitioner.Create(0, tileData.Length), (range, loopState) =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    var tgtTileStruct = memory.Read<TgtTileStruct>(tileData[i].TgtFilePtr);
                    string detailName = memory.Read<TgtDetailStruct>(tgtTileStruct.TgtDetailPtr).name.ToString(memory);
                    string tilePath = tgtTileStruct.TgtPath.ToString(memory);
                    Vector2i tileGridPosition = new Vector2i(
                        i % terrain.NumCols * 23,
                        i / terrain.NumCols * 23
                    );

                    if (!string.IsNullOrEmpty(tilePath))
                        tiles.GetOrAdd(tilePath, _ => new ConcurrentBag<Vector2>())
                        .Add(tileGridPosition);

                    if (!string.IsNullOrEmpty(detailName))
                        tiles.GetOrAdd(detailName, _ => new ConcurrentBag<Vector2>())
                        .Add(tileGridPosition);
                }
            });
        }

        private void InitializeChunks(int chunkResolution, int worldGridWidth, int worldGridHeight)
        {
            int chunksX = (int)Math.Ceiling((double)worldGridWidth / chunkResolution);
            int chunksY = (int)Math.Ceiling((double)worldGridHeight / chunkResolution);
            _chunks = new Chunk[chunksX, chunksY];

            for (int x = 0; x < chunksX; ++x)
            {
                for (int y = 0; y < chunksY; ++y)
                {
                    int chunkStartX = x * chunkResolution;
                    int chunkStartY = y * chunkResolution;
                    int chunkEndX = Math.Min(chunkStartX + chunkResolution, worldGridWidth);
                    int chunkEndY = Math.Min(chunkStartY + chunkResolution, worldGridHeight);

                    int totalWeight = 0;
                    for (int col = chunkStartX; col < chunkEndX; ++col)
                    {
                        for (int row = chunkStartY; row < chunkEndY; ++row)
                        {
                            totalWeight += _worldGrid[row, col];
                        }
                    }

                    _chunks[x, y] = new Chunk()
                    {
                        Position = new Vector2(
                            (float)chunkStartX + (chunkResolution / 2f),
                            (float)chunkStartY + (chunkResolution / 2f)
                        ),
                        Weight = totalWeight
                    };
                }
            }
        }

        public Vector2? FindTilePositionByName(string searchString)
        {
            var playerPos = Core.GameController.Player.GridPosNum;

            if (_tiles.TryGetValue(searchString, out var results) && !results.IsEmpty)
                return results.OrderBy(I => playerPos.Distance(I))
                    .FirstOrDefault();

            var matchingPair = _tiles.FirstOrDefault(kvp => kvp.Key.Contains(searchString));
            return matchingPair.Key != null && !matchingPair.Value.IsEmpty
                ? (Vector2?)matchingPair.Value.OrderBy(I => playerPos.Distance(I))
                    .FirstOrDefault()
                : null;
        }

        public void ResetAllChunks()
        {
            foreach (var chunk in _chunks)
                chunk.IsRevealed = false;
        }

        public void UpdateRevealedChunks()
        {
            var playerPos = Core.GameController.Player.GridPosNum;
            foreach (var chunk in Chunks.Where(c => !c.IsRevealed))
            {
                if (playerPos.Distance(chunk.Position) < Core.Settings.ViewDistance)
                {
                    chunk.IsRevealed = true;
                }
            }
        }

        public Chunk? GetNextUnrevealedChunk()
        {
            return Chunks
                .Where(c => !c.IsRevealed && c.Weight > 0)
                .OrderBy(c => c.Position.Distance(Core.GameController.Player.GridPosNum))
                .ThenByDescending(c => c.Weight)
                .FirstOrDefault();
        }


        public Path? FindPath(Vector2 start, Vector2 end)
        {
            Point[] pathPoints = _pathFinder.FindPath(new Point((int)start.X, (int)start.Y), new Point((int)end.X, (int)end.Y));
            if (pathPoints == null || pathPoints.Length == 0)
            {
                return null;
            }

            List<Vector2> pathVectors = new List<Vector2>(pathPoints.Length);
            foreach (Point p in pathPoints)
                pathVectors.Add(new Vector2((float)p.X, (float)p.Y));

            return new Path(pathVectors);
        }



        private T? FindClosestGeneric<T>(IEnumerable<T> source, Func<T, bool> predicate, Func<T, float> distanceSelector) where T : class
        {
            return source.Where(predicate)
                .OrderBy(I => _random.Next()) // Why are we using random here?
                .MinBy(distanceSelector);
        }

        // Caching for performance and possible crash from threading issues
        private (DateTime Timestamp, ItemsOnGroundLabelElement.VisibleGroundItemDescription? Item) _cachedClosestItem;
        private (DateTime Timestamp, Entity? Monster) _cachedClosestMonster;
        private const int CACHE_DURATION_MS = 10;

        public Entity? ClosestTargetableMonster
        {
            get
            {
                // If cache is still valid, return cached value
                if (DateTime.Now < _cachedClosestMonster.Timestamp.AddMilliseconds(CACHE_DURATION_MS))
                    return _cachedClosestMonster.Monster;

                Entity? monster = null;
                try
                {
                    // Prevents crashes if the collection is modified by another thread
                    monster = FindClosestGeneric(Core.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster],
                        m => m.IsAlive && m.IsTargetable && m.IsHostile && !m.IsDead,
                        m => m.DistancePlayer);
                }
                catch (Exception)
                {
                    // Might need to add a log if this happens a lot
                }

                _cachedClosestMonster = (DateTime.Now, monster);
                return monster;
            }
        }


        public (Vector2 Position, float Weight) FindBestFightingPosition()
        {
            var playerPos = Core.GameController.Player.GridPosNum;
            var bestPos = playerPos;
            var bestWeight = GetPositionFightWeight(bestPos);

            var candidateMonsters = Core.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
                .Where(m => m.IsHostile && m.IsAlive && m.GridPosNum.Distance(playerPos) > Core.Settings.CombatDistance.Value * 1);

            foreach (var monster in candidateMonsters)
            {
                var testPos = monster.GridPosNum;
                var testWeight = GetPositionFightWeight(testPos);

                if (testWeight > bestWeight)
                {
                    bestWeight = testWeight;
                    bestPos = testPos;
                }
            }

            return (bestPos, bestWeight);
        }

        public float GetPositionFightWeight(Vector2 position)
        {
            return Core.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
                .Where(m => m.IsHostile && m.IsAlive && !m.IsDead && m.GridPosNum.Distance(position) < Core.Settings.CombatDistance)
                .Sum(m => GetMonsterRarityWeight(m.Rarity));
        }

        public static int GetMonsterRarityWeight(MonsterRarity rarity)
        {
            return rarity switch
            {
                MonsterRarity.Magic => 3,
                MonsterRarity.Rare => 10,
                MonsterRarity.Unique => 25,
                _ => 1,
            };
        }

        public void BlacklistItemId(uint id)
        {
            _blacklistItemIds.Add(id);
        }


        public ItemsOnGroundLabelElement.VisibleGroundItemDescription? ClosestValidGroundItem
        {
            get
            {
                if (DateTime.Now < _cachedClosestItem.Timestamp.AddMilliseconds(CACHE_DURATION_MS))
                    return _cachedClosestItem.Item;

                ItemsOnGroundLabelElement.VisibleGroundItemDescription? item = null;
                try
                {
                    // Prevents crashes if the collection is modified by another thread
                    item = FindClosestGeneric(Core.GameController.IngameState.IngameUi.ItemsOnGroundLabelElement.VisibleGroundItemLabels,
                        i => i != null &&
                             i.Label != null &&
                             i.Entity != null &&
                             i.Label.IsVisibleLocal &&
                             i.Label.Text != null &&
                             !i.Label.Text.EndsWith(" Gold") &&
                             !_blacklistItemIds.Contains(i.Entity.Id), // O(1) check
                        i => i.Entity.DistancePlayer);
                }
                catch (Exception)
                {
                    // Again need to log if this happens a lot
                }

                _cachedClosestItem = (DateTime.Now, item);
                return item;
            }
        }


        public Vector2 GetSimulacrumCenter()
        {
            switch (Core.GameController.Area.CurrentArea.Name)
            {
                case "The Bridge Enraptured":
                    return new Vector2(551, 624);
                case "Oriath Delusion":
                    //return new Vector2(587, 253); Might still need
                    return new Vector2(494, 288);
                case "The Syndrome Encampment":
                    return new Vector2(316, 253);
                case "Hysteriagate":
                    return new Vector2(183, 269);
                case "Lunacy's Watch":
                    return new Vector2(270, 687);
                default: return Vector2.Zero;
            }
        }
    }
}