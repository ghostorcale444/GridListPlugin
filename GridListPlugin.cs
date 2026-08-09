using System;
using System.Collections.Generic;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Input;
using VRage.Plugins;
using VRage.Utils;
using VRageMath;

namespace GridListPlugin
{
    /// <summary>
    /// Pulsar plugin: Shift+Insert toggles a grid list overlay.
    /// Click any entry to teleport the spectator camera to that grid's center.
    /// </summary>
    public class GridListPlugin : IPlugin, IHandleInputPlugin
    {
        // ── State ─────────────────────────────────────────────────────────────
        private bool _visible          = false;
        private bool _shiftInsertWasDown = false;

        private const float PanelX     = 20f;
        private const float PanelY     = 80f;
        private const float PanelWidth = 420f;
        private const float RowHeight  = 28f;
        private const float HeaderH    = 36f;
        private const float PaddingX   = 10f;
        private const float ScrollbarW = 14f;

        private int _scrollOffset    = 0;
        private int _maxVisibleRows  = 16;

        private List<GridEntry> _gridEntries     = new List<GridEntry>();
        private int             _ticksSinceRefresh = 0;
        private const int       RefreshInterval  = 60;

        private bool _leftWasDown = false;

        // ── IPlugin ───────────────────────────────────────────────────────────
        public void Init(object gameInstance) { }

        public void Update()
        {
            if (!_visible) return;

            _ticksSinceRefresh++;
            if (_ticksSinceRefresh >= RefreshInterval)
            {
                RefreshGridList();
                _ticksSinceRefresh = 0;
            }
        }

        public void Dispose() { }

        // ── IHandleInputPlugin ────────────────────────────────────────────────
        public void HandleInput()
        {
            HandleToggleHotkey();
            if (!_visible) return;
            HandleMouseClick();
            HandleScroll();
        }

        // ── Toggle ────────────────────────────────────────────────────────────
        private void HandleToggleHotkey()
        {
            bool shift  = MyInput.Static.IsKeyPress(MyKeys.LeftShift) ||
                          MyInput.Static.IsKeyPress(MyKeys.RightShift);
            bool insert = MyInput.Static.IsKeyPress(MyKeys.Insert);
            bool combo  = shift && insert;

            if (combo && !_shiftInsertWasDown)
            {
                _visible = !_visible;
                if (_visible)
                {
                    _scrollOffset      = 0;
                    _ticksSinceRefresh = RefreshInterval;
                    RefreshGridList();
                }
            }

            _shiftInsertWasDown = combo;
        }

        // ── Grid snapshot ─────────────────────────────────────────────────────
        private void RefreshGridList()
        {
            _gridEntries.Clear();
            if (MySession.Static == null) return;

            foreach (MyEntity entity in MyEntities.GetEntities())
            {
                MyCubeGrid grid = entity as MyCubeGrid;
                if (grid == null || grid.MarkedForClose) continue;

                _gridEntries.Add(new GridEntry
                {
                    DisplayName = BuildGridLabel(grid),
                    WorldCenter = grid.PositionComp.WorldAABB.Center,
                    IsLarge     = grid.GridSizeEnum == MyCubeSize.Large,
                    BlockCount  = grid.BlocksCount,
                });
            }

            _gridEntries.Sort((a, b) =>
            {
                int sc = b.IsLarge.CompareTo(a.IsLarge);
                return sc != 0 ? sc : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });
        }

        private static string BuildGridLabel(MyCubeGrid grid)
        {
            string name = string.IsNullOrWhiteSpace(grid.DisplayName)
                ? string.Format("Grid_{0:X4}", grid.EntityId & 0xFFFF)
                : grid.DisplayName;

            string tag = grid.GridSizeEnum == MyCubeSize.Large ? "[L]" : "[S]";
            return string.Format("{0} {1}  ({2} blk)", tag, name, grid.BlocksCount);
        }

        // ── Mouse ─────────────────────────────────────────────────────────────
        private void HandleMouseClick()
        {
            bool leftDown = MyInput.Static.IsLeftMousePressed();

            if (leftDown && !_leftWasDown)
            {
                Vector2 mouse = MyInput.Static.GetMousePosition();
                int hitRow    = HitTestRow(mouse);
                if (hitRow >= 0)
                {
                    int gi = hitRow + _scrollOffset;
                    if (gi < _gridEntries.Count)
                        TeleportSpectatorTo(_gridEntries[gi]);
                }
            }

            _leftWasDown = leftDown;
        }

        private void HandleScroll()
        {
            int wheel = MyInput.Static.DeltaMouseScrollWheelValue();
            if (wheel != 0)
            {
                _scrollOffset -= Math.Sign(wheel) * 3;
                ClampScroll();
            }
        }

        private void ClampScroll()
        {
            int max = Math.Max(0, _gridEntries.Count - _maxVisibleRows);
            if (_scrollOffset < 0)   _scrollOffset = 0;
            if (_scrollOffset > max) _scrollOffset = max;
        }

        // ── Spectator teleport ────────────────────────────────────────────────
        private static void TeleportSpectatorTo(GridEntry entry)
        {
            if (MySession.Static == null) return;

            MySession.Static.SetCameraController(
                MyCameraControllerEnum.Spectator,
                null,
                entry.WorldCenter);

            Vector3D offset = new Vector3D(0, 150, -300);
            MySpectatorCameraController.Static.Position = entry.WorldCenter + offset;
            MySpectatorCameraController.Static.SetTarget(entry.WorldCenter, Vector3D.Up);
            MySpectatorCameraController.Static.SpectatorCameraMovement =
                MySpectatorCameraMovementEnum.UserControlled;
        }

        // ── Hit test ──────────────────────────────────────────────────────────
        private int HitTestRow(Vector2 mouse)
        {
            float panelH = HeaderH + _maxVisibleRows * RowHeight;
            if (mouse.X < PanelX || mouse.X > PanelX + PanelWidth) return -1;
            if (mouse.Y < PanelY + HeaderH || mouse.Y > PanelY + panelH) return -1;
            int row = (int)((mouse.Y - PanelY - HeaderH) / RowHeight);
            return row >= 0 ? row : -1;
        }

        // ── Draw ──────────────────────────────────────────────────────────────
        public void Draw()
        {
            if (!_visible || MySession.Static == null) return;

            ClampScroll();

            int   visible = Math.Min(_maxVisibleRows, _gridEntries.Count - _scrollOffset);
            float totalH  = HeaderH + _maxVisibleRows * RowHeight + 8f;

            DrawRect(PanelX, PanelY, PanelWidth, totalH, new Color(10, 10, 20, 210));
            DrawRect(PanelX, PanelY, PanelWidth, HeaderH, new Color(20, 80, 140, 230));
            DrawText(
                string.Format("  GRID LIST  ({0})   [Shift+Insert to close]", _gridEntries.Count),
                PanelX + PaddingX, PanelY + 8f, Color.White, 0.55f);

            Vector2 mouse = MyInput.Static.GetMousePosition();

            for (int i = 0; i < visible; i++)
            {
                int   gi   = i + _scrollOffset;
                float rowY = PanelY + HeaderH + i * RowHeight;

                Color bg = (gi % 2 == 0)
                    ? new Color(15, 15, 30, 180)
                    : new Color(25, 25, 45, 180);

                if (mouse.X >= PanelX && mouse.X <= PanelX + PanelWidth - ScrollbarW &&
                    mouse.Y >= rowY   && mouse.Y <  rowY + RowHeight)
                {
                    bg = new Color(40, 100, 180, 200);
                }

                DrawRect(PanelX, rowY, PanelWidth - ScrollbarW, RowHeight, bg);
                DrawText(_gridEntries[gi].DisplayName, PanelX + PaddingX, rowY + 6f, Color.Cyan, 0.48f);
            }

            // Scrollbar
            if (_gridEntries.Count > _maxVisibleRows)
            {
                float trackX = PanelX + PanelWidth - ScrollbarW;
                float trackH = _maxVisibleRows * RowHeight;
                DrawRect(trackX, PanelY + HeaderH, ScrollbarW, trackH, new Color(30, 30, 50, 200));

                float thumbFrac = (float)_maxVisibleRows / _gridEntries.Count;
                float thumbH    = Math.Max(20f, trackH * thumbFrac);
                float maxScroll = _gridEntries.Count - _maxVisibleRows;
                float thumbY    = PanelY + HeaderH + (_scrollOffset / maxScroll) * (trackH - thumbH);
                DrawRect(trackX + 2f, thumbY, ScrollbarW - 4f, thumbH, new Color(80, 160, 240, 220));
            }

            float footerY = PanelY + totalH - 22f;
            DrawRect(PanelX, footerY, PanelWidth, 22f, new Color(10, 10, 20, 180));
            DrawText("  Click row to spectate  ·  Scroll to scroll",
                PanelX + PaddingX, footerY + 4f, new Color(160, 160, 200, 200), 0.42f);
        }

        // ── Render helpers ────────────────────────────────────────────────────
        private static void DrawRect(float x, float y, float w, float h, Color color)
        {
            VRageRender.MyRenderProxy.DebugDrawAABB(
                new BoundingBoxD(new Vector3D(x, y, 0), new Vector3D(x + w, y + h, 0)),
                color.ToVector3(),
                1f,
                color.A / 255f,
                false);
        }

        private static void DrawText(string text, float x, float y, Color color, float scale)
        {
            VRageRender.MyRenderProxy.DebugDrawText2D(
                new Vector2(x, y),
                text,
                color,
                scale,
                VRage.Utils.MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP);
        }

        // ── Entry record ──────────────────────────────────────────────────────
        private struct GridEntry
        {
            public string  DisplayName;
            public Vector3D WorldCenter;
            public bool    IsLarge;
            public int     BlockCount;
        }
    }
}