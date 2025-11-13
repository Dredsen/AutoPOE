using AStar;
using ExileCore.PoEMemory.Components;
using ExileCore.Shared.Helpers;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using ExileCore;

namespace AutoPOE.Navigation
{
    public class Path
    {
        public bool IsFinished => _nodes.Count == 0;
        private readonly List<Vector2> _nodes;
        public Vector2? Next => _nodes.Count > 0 ? _nodes.First() : null;
        public Vector2 Destination => _nodes.LastOrDefault();
        public DateTime ExpiresAt { get; internal set; } = DateTime.Now.AddSeconds(5);

        public Path(List<Vector2> nodes)
        {
            _nodes = nodes ?? new List<Vector2>();
            if (_nodes.Count < 2)
                return;

            const float Epsilon = 0.001f;
            for (int i = _nodes.Count - 2; i >= 0; i--)
            {
                if (i + 2 < _nodes.Count)
                {
                    var currentSegmentDirection = Vector2.Normalize(_nodes[i + 1] - _nodes[i]);
                    var nextSegmentDirection = Vector2.Normalize(_nodes[i + 2] - _nodes[i + 1]);
                    var dotProduct = Vector2.Dot(currentSegmentDirection, nextSegmentDirection);

                    if (Math.Abs(dotProduct - 1.0f) < Epsilon)
                        _nodes.RemoveAt(i + 1);
                }
            }
        }

        public async Task FollowPath()
        {
            if (DateTime.Now > ExpiresAt)
            {
                _nodes.Clear();
                return;
            }

            if (IsFinished) return;

            var playerPos = Core.GameController.Player.GridPosNum;

            while (!IsFinished && playerPos.Distance(Next.Value) < Core.Settings.NodeSize.Value)
            {
                _nodes.RemoveAt(0);
            }

            if (IsFinished) return;

            var skill = Core.Settings.GetNextMovementSkill();
            await Controls.UseKeyAtGridPos(_nodes.First(), skill);
        }

        public void Render()
        {
            if (IsFinished) return;
            var camera = Core.GameController.IngameState.Camera;

            List<Vector2> nodesToRender;
            try
            {
                nodesToRender = _nodes.ToList();
            }
            catch (Exception)
            {
                return;
            }

            if (nodesToRender.Count == 0) return;

            var playerPos = Core.GameController.Player.GridPosNum;
            var p1_active = Controls.GetScreenByGridPos(playerPos);
            var p2_active = Controls.GetScreenByGridPos(nodesToRender[0]);
            Core.Graphics.DrawLine(p1_active, p2_active, 2, SharpDX.Color.Green);

            for (int i = 0; i < nodesToRender.Count - 1; i++)
            {
                var p1 = Controls.GetScreenByGridPos(nodesToRender[i]);
                var p2 = Controls.GetScreenByGridPos(nodesToRender[i + 1]);
                Core.Graphics.DrawLine(p1, p2, 2, SharpDX.Color.White);
            }
        }
    }
}