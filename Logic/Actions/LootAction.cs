using ExileCore.PoEMemory.Elements;
using ExileCore.Shared.Helpers;
using System.ComponentModel.Design;
using System.Numerics;
using AutoPOE.Logic;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using ExileCore.Shared.Enums;
using System.Linq;
using ExileCore;

namespace AutoPOE.Logic.Actions
{
    public class LootAction : IAction
    {
        private Navigation.Path? _currentPath;
        private uint? _targetItemId;

        private int _consecutiveFailedClicks = 0;
        private int _hardStuckAttempts = 0;
        private const int MAX_FAILED_CLICKS = 3;

        private DateTime _lootCheckCooldown = DateTime.MinValue;
        private const int LOOT_COOLDOWN_MS = 2000;

        public async Task<ActionResultType> Tick()
        {
            var playerPos = Core.GameController.Player.GridPosNum;
            var visibleLabels = Core.GameController.IngameState.IngameUi.ItemsOnGroundLabelsVisible;

            LabelOnGround targetItem = null;

            if (_targetItemId.HasValue)
            {
                targetItem = visibleLabels.FirstOrDefault(x => x.ItemOnGround.Id == _targetItemId.Value);
            }

            if (targetItem == null)
            {
                var bestItem = Core.Map.ClosestValidGroundItem;

                if (bestItem != null)
                {
                    _targetItemId = bestItem.Entity.Id;
                    targetItem = visibleLabels.FirstOrDefault(x => x.ItemOnGround.Id == _targetItemId.Value);

                    _currentPath = null;
                    _consecutiveFailedClicks = 0;
                    _hardStuckAttempts = 0;
                }
            }

            if (targetItem == null)
            {
                _targetItemId = null;

                if (_lootCheckCooldown == DateTime.MinValue)
                {
                    _lootCheckCooldown = DateTime.Now.AddMilliseconds(LOOT_COOLDOWN_MS);
                    return ActionResultType.Running;
                }

                if (DateTime.Now < _lootCheckCooldown)
                    return ActionResultType.Running;

                _lootCheckCooldown = DateTime.MinValue;
                return ActionResultType.Success;
            }

            _lootCheckCooldown = DateTime.MinValue;

            if (Core.Settings.LootItemsUnstick && (DateTime.Now > SimulacrumState.LastMovedAt.AddSeconds(15) || DateTime.Now > SimulacrumState.LastToggledLootAt.AddSeconds(10)))
            {
                await Controls.UseKey(Keys.Z);
                await Task.Delay(100);
                await Controls.UseKey(Keys.Z);
                SimulacrumState.LastToggledLootAt = DateTime.Now;
            }

            if (playerPos.Distance(targetItem.ItemOnGround.GridPosNum) < Core.Settings.NodeSize)
            {
                _currentPath = null;
                _consecutiveFailedClicks++;

                if (_consecutiveFailedClicks >= MAX_FAILED_CLICKS)
                {
                    _hardStuckAttempts++;
                    _consecutiveFailedClicks = 0;

                    if (_hardStuckAttempts == 1)
                    {
                        DebugWindow.LogMsg($"[LootAction] Item stuck behind UI? Toggling Z. (Id: {_targetItemId})", 2, SharpDX.Color.Yellow);
                        await Controls.UseKey(Keys.Z);
                        await Task.Delay(150);
                        await Controls.UseKey(Keys.Z);
                        await Task.Delay(300);
                    }
                    else if (_hardStuckAttempts == 2)
                    {
                        DebugWindow.LogMsg($"[LootAction] Item still stuck. Wiggling player. (Id: {_targetItemId})", 2, SharpDX.Color.Orange);

                        var sharpCenter = Core.GameController.Window.GetWindowRectangle().Center;
                        var screenCenter = new Vector2(sharpCenter.X, sharpCenter.Y);

                        var randomOffset = new Vector2((float)(new Random().NextDouble() - 0.5f) * 25, (float)(new Random().NextDouble() - 0.5f) * 25);

                        await Controls.ClickScreenPos(screenCenter + randomOffset);
                        await Task.Delay(500);
                    }
                    else
                    {
                        DebugWindow.LogMsg($"[LootAction] Item {_targetItemId} is unlootable. Blacklisting.", 2, SharpDX.Color.Red);
                        Core.Map.BlacklistItemId(_targetItemId.Value);
                        _targetItemId = null;
                        return ActionResultType.Running;
                    }

                    return ActionResultType.Running;
                }

                if (targetItem.Label != null)
                {
                    var labelRect = targetItem.Label.GetClientRect();
                    await Controls.ClickScreenPos(new Vector2(labelRect.Center.X, labelRect.Center.Y));
                }

                return ActionResultType.Running;
            }

            if (_currentPath == null)
            {
                _currentPath = Core.Map.FindPath(playerPos, targetItem.ItemOnGround.GridPosNum);
                if (_currentPath == null)
                {
                    DebugWindow.LogMsg($"[LootAction] No path to item {_targetItemId}. Blacklisting.", 2, SharpDX.Color.Red);
                    Core.Map.BlacklistItemId(_targetItemId.Value);
                    _consecutiveFailedClicks = 0;
                    return ActionResultType.Failure;
                }
            }

            await _currentPath.FollowPath();
            return ActionResultType.Running;
        }

        public void Render()
        {
            if (_targetItemId.HasValue)
            {
                Core.Graphics.DrawText($"Looting ID: {_targetItemId} | Fails: {_consecutiveFailedClicks} | StuckLvl: {_hardStuckAttempts}", new Vector2(500, 550));
            }
            _currentPath?.Render();
        }
    }
}