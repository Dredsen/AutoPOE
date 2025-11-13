using AutoPOE.Logic.Actions;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using System.Numerics;

namespace AutoPOE.Logic.Sequences
{
    public class DebugSequence : ISequence
    {
        private Task<ActionResultType> _currentTask;
        private IAction _currentAction;

        Vector2 PumpPosition = Vector2.Zero;
        long activatedState, readyToBuildState, readyToStartState;
        string countDown = string.Empty;
        Dictionary<Vector2, string> chests = [];

        /// <summary>
        /// object detections and states are working. next would be building out the actual implementation. 
        /// 
        /// 
        ///     For map items: we want Entity -> Item Info -> Tags: "can_be_infected_map"
        ///     
        /// On entering map we need to path to the pump. It will already be visible. 
        /// After a small delay, the 'ready to start' state should change. BoundsCenterPosNum of pump works to start. 
        /// After a small delay we can click the 'skip waiting' button in the bottom right
        /// We then wait till countodwn is done + some configurable delay to let mobs finish spawning and being killed. 
        /// We do a quick sweep through the map killing any remaining monsters
        /// 
        /// Finally we can loot all chests and items on the map and leave. 
        /// We will be caching the chest locations as we see them and the visual range is enough that we shouldn't miss any (1-2 at most in some layouts). 
        ///     
        /// </summary>
        public void Tick()
        {
            var pump = Core.GameController.EntityListWrapper.OnlyValidEntities.FirstOrDefault(I => I.Type == EntityType.IngameIcon && I.Path.EndsWith("/BlightPump"));
            if(pump != null)
            {
                PumpPosition = pump.GridPosNum;

                var states = pump.GetComponent<StateMachine>();
                if(states != null)
                {
                    activatedState = states.States.FirstOrDefault(I => I.Name == "activated")?.Value ?? 0;
                    readyToBuildState = states.States.FirstOrDefault(I => I.Name == "ready_to_start")?.Value ?? 0;
                    readyToStartState = states.States.FirstOrDefault(I => I.Name == "ready_to_build")?.Value ?? 0;
                }
            }
       
            countDown= Core.GameController.IngameState.IngameUi.Parent.GetChildFromIndices(1,25, 4, 0, 0, 0, 0)?.Text??"";

            var visibleChests = Core.GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Chest]
                .Where(I =>!I.IsOpened).ToList();

            foreach(var chest in visibleChests)
            {
                if(!chests.ContainsKey(chest.GridPosNum))
                    chests.Add(chest.GridPosNum, chest.Path);
            }

            //Handle removing close by chests no longer visible. 
            foreach (var chestPos in chests.Keys.ToArray())
            {
                if (chestPos.Distance(Core.GameController.Player.GridPosNum) < Core.Settings.ViewDistance)
                {
                    if (!visibleChests.Any(I => I.GridPosNum == chestPos))
                        chests.Remove(chestPos);
                }
            }
        }


        public void Render()
        {

            //Try to draw the click position to start pump to make sure we're hitting the right target. 

            var pump = Core.GameController.EntityListWrapper.OnlyValidEntities.FirstOrDefault(I => I.Type == EntityType.IngameIcon && I.Path.EndsWith("/BlightPump"));
            if(pump != null)
            {
                //get the bounds cente rpos. 
                
                Core.Graphics.DrawCircle(
                    Core.GameController.IngameState.Camera.WorldToScreen(pump.BoundsCenterPosNum),
                    15,
                    SharpDX.Color.Red);
            }


            Core.Graphics.DrawText($"Countdown: {countDown} Active: {activatedState}. Ready to start {readyToBuildState}. Chests: {chests.Count}", new Vector2(100, 100), SharpDX.Color.White);
             _currentAction?.Render();
        }
    }
}
