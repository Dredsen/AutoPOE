using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using AutoPOE.Navigation;
using ExileCore.Shared.Helpers;
using AutoPOE.Logic;
using ExileCore;
using ExileCore.Shared.Enums;

namespace AutoPOE.Logic.Actions
{
    public class ExploreAction : IAction
    {
        private List<Vector2> _blacklistedChunks = new List<Vector2>();

        // Failsafe fields
        private int _consecutiveFailures = 0;
        private const int MAX_CONSECUTIVE_FAILURES = 100; // Give up after 100 ticks of finding no valid path

        public ExploreAction()
        {
            Core.Map.ResetAllChunks();
            _blacklistedChunks.Clear();
            _currentPath = Core.Map.FindPath(Core.GameController.Player.GridPosNum, Core.Map.GetSimulacrumCenter());
        }

        private Random _random = new Random();
        private Navigation.Path? _currentPath;

        public async Task<ActionResultType> Tick()
        {
            Core.Map.UpdateRevealedChunks();
            var playerPos = Core.GameController.Player.GridPosNum;

            if (DateTime.Now > SimulacrumState.LastMovedAt.AddSeconds(2))
            {
                var nextPos = Core.GameController.Player.GridPosNum + new Vector2(_random.Next(-50, 50), _random.Next(-50, 50));
                await Controls.UseKeyAtGridPos(nextPos, Core.Settings.GetNextMovementSkill());
                return ActionResultType.Running;
            }

            if (_currentPath != null && !_currentPath.IsFinished)
            {
                await _currentPath.FollowPath();
                _consecutiveFailures = 0; // We are on a valid path, reset counter
                return ActionResultType.Running;
            }

            // Path is finished or null, find a new target chunk
            var nextChunk = Core.Map.GetNextUnrevealedChunk();
            if (nextChunk == null)
                return ActionResultType.Success; // No more chunks

            // Check if chunk is blacklisted and skip if it is.
            if (_blacklistedChunks.Contains(nextChunk.Position))
            {
                _consecutiveFailures++;
                if (_consecutiveFailures > MAX_CONSECUTIVE_FAILURES)
                {
                    _blacklistedChunks.Clear(); // Clear for next run
                    return ActionResultType.Success; // Got stuck
                }
                return ActionResultType.Running; // Go next tick and skip the chunk 
            }

            _currentPath = Core.Map.FindPath(Core.GameController.Player.GridPosNum, nextChunk.Position);

            if (_currentPath == null)
            {
                // couldnt find good path, blacklist this chunk
                _blacklistedChunks.Add(nextChunk.Position);
                _consecutiveFailures++;
            }
            else
            {
                // We found a new valid path
                _consecutiveFailures = 0;
            }

            return ActionResultType.Running;
        }

        public void Render()
        {
            Core.Graphics.DrawText($"Area Name: '{Core.GameController.Area.CurrentArea.Name}' Path: {_currentPath?.Next}", new Vector2(115, 115));
            _currentPath?.Render();
        }
    }
}