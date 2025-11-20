using ExileCore;
using ExileCore.PoEMemory.Elements;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoPOE.Logic;

namespace AutoPOE.Logic.Actions
{
    public class LootAction : IAction
    {
        private Navigation.Path? _currentPath;
        private uint? _targetItemId;

        private int _consecutiveFailedClicks = 0;
        private const int MAX_FAILED_CLICKS = 10;

        private DateTime _targetAcquiredTime;
        private const int MAX_TIME_PER_ITEM_SECONDS = 20;

        public async Task<ActionResultType> Tick()
        {
            var item = Core.Map.ClosestValidGroundItem;
            var playerPos = Core.GameController.Player.GridPosNum;

            if (item == null)
            {
                _targetItemId = null;
                return ActionResultType.Success;
            }

            if (item.Entity.Id != _targetItemId)
            {
                _targetItemId = item.Entity.Id;
                _currentPath = null;
                _targetAcquiredTime = DateTime.Now;
                _consecutiveFailedClicks = 0;
            }

            if (_targetItemId.HasValue && (DateTime.Now - _targetAcquiredTime).TotalSeconds > MAX_TIME_PER_ITEM_SECONDS)
            {
                Core.Map.BlacklistItemId(_targetItemId.Value);
                _targetItemId = null;
                return ActionResultType.Running;
            }

            if (Core.Settings.LootItemsUnstick && (DateTime.Now > SimulacrumState.LastMovedAt.AddSeconds(3) || DateTime.Now > SimulacrumState.LastToggledLootAt.AddSeconds(5)))
            {
                await Controls.UseKey(Keys.Z);
                await Task.Delay(Core.Settings.ActionFrequency);
                await Controls.UseKey(Keys.Z);
                await Task.Delay(500);
                SimulacrumState.LastToggledLootAt = DateTime.Now;
            }

            if (playerPos.Distance(item.Entity.GridPosNum) < Core.Settings.NodeSize)
            {
                _currentPath = null;
                _consecutiveFailedClicks++;

                if (_consecutiveFailedClicks >= MAX_FAILED_CLICKS)
                {
                    await Controls.UseKey(Keys.Z);
                    await Task.Delay(10);
                    await Controls.UseKey(Keys.Z);
                    await Task.Delay(10);

                    _consecutiveFailedClicks = 0;
                }

                var labelCenter = item.ClientRect.Center;

                await Controls.ClickScreenPos(new Vector2(labelCenter.X, labelCenter.Y));

                await Task.Delay(30);

                return ActionResultType.Running;
            }

            if (_currentPath == null)
            {
                _currentPath = Core.Map.FindPath(playerPos, item.Entity.GridPosNum);
                if (_currentPath == null)
                {
                    Core.Map.BlacklistItemId(_targetItemId.Value);
                    return ActionResultType.Failure;
                }
            }

            await _currentPath.FollowPath();

            if (_currentPath.IsFinished)
            {
                _currentPath = null;
            }

            return ActionResultType.Running;
        }

        public void Render()
        {
            if (_targetItemId.HasValue)
            {
                var timeLeft = Math.Max(0, MAX_TIME_PER_ITEM_SECONDS - (DateTime.Now - _targetAcquiredTime).TotalSeconds);
                Core.Graphics.DrawText($"Looting ID: {_targetItemId} | Fails: {_consecutiveFailedClicks} | TTL: {timeLeft:F1}s", new Vector2(500, 550));
            }
            _currentPath?.Render();
        }
    }
}