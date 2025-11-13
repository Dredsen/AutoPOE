using ExileCore.Shared.Helpers;
using System;
using System.Numerics;
using AutoPOE.Logic;
using System.Threading.Tasks;
using ExileCore.Shared.Enums;
using System.Linq;
using ExileCore;

namespace AutoPOE.Logic.Actions
{
    public class LeaveMapAction : IAction
    {
        public LeaveMapAction()
        {
            if (SimulacrumState.PortalPosition != null)
            {
                _currentPath = Core.Map.FindPath(Core.GameController.Player.GridPosNum, SimulacrumState.PortalPosition.Value);
                _currentPathTarget = SimulacrumState.PortalPosition.Value; // Store the target
            }
        }

        private Random _random = new Random();
        private Navigation.Path? _currentPath;
        private Vector2? _currentPathTarget; // The destination of our current path

        public async Task<ActionResultType> Tick()
        {
            if (Core.Map.ClosestValidGroundItem != null)
                return ActionResultType.Exception;

            var playerPos = Core.GameController.Player.GridPosNum;

            if (DateTime.Now > SimulacrumState.LastMovedAt.AddSeconds(2))
            {
                var nextPos = Core.GameController.Player.GridPosNum + new Vector2(_random.Next(-50, 50), _random.Next(-50, 50));
                await Controls.UseKeyAtGridPos(nextPos, Core.Settings.GetNextMovementSkill());
                return ActionResultType.Running;
            }

            var townPortal = Core.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.TownPortal]
                .OrderBy(e => e.DistancePlayer)
                .FirstOrDefault();

            Vector2? targetPosition = null;
            if (townPortal != null)
            {
                targetPosition = townPortal.GridPosNum;
            }
            else if (SimulacrumState.PortalPosition != null)
            {
                targetPosition = SimulacrumState.PortalPosition.Value;
            }
            else
            {
                return ActionResultType.Exception;
            }

            if (playerPos.Distance(targetPosition.Value) < Core.Settings.NodeSize * 2)
            {
                if (townPortal != null)
                {
                    await Controls.ClickScreenPos(Controls.GetScreenByWorldPos(townPortal.BoundsCenterPosNum));
                    return ActionResultType.Success;
                }

                _currentPath = null;
                _currentPathTarget = null;
                return ActionResultType.Running;
            }

            // Check if we need a new path:
            // 1. No path
            // 2. Path is finished
            // 3. Our target has changed (e.g., portal entity appeared)
            if (_currentPath == null || _currentPath.IsFinished || _currentPathTarget != targetPosition)
            {
                _currentPath = Core.Map.FindPath(playerPos, targetPosition.Value);
                _currentPathTarget = targetPosition.Value; // Store the new target

                if (_currentPath == null)
                {
                    return ActionResultType.Exception;
                }
            }

            await _currentPath.FollowPath();
            return ActionResultType.Running;
        }

        public void Render()
        {
            _currentPath?.Render();
        }
    }
}