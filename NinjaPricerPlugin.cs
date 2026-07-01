namespace OriathHub.Plugins.NinjaPricer
{
    using Coroutine;
    using ImGuiNET;
    using OriathHub;
    using OriathHub.CoroutineEvents;
    using OriathHub.Plugin;
    using OriathHub.RemoteEnums;
    using OriathHub.RemoteObjects.States.InGameStateObjects;
    using OriathHub.Utils;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Numerics;

    /// <summary>
    ///     Draws poe.ninja price boxes over the items in the open stash tab and the main inventory.
    ///
    ///     All memory reading lives in <see cref="StashReader"/> (the seam meant to become a framework
    ///     API); this class only owns the plugin lifecycle, the refresh coroutine, and the overlay. Items
    ///     come from ServerData (clean identity for all loaded tabs); the stash UI is not used as the
    ///     item source because its per-cell item pointer is obfuscated — see
    ///     <c>.investigation/ninjapricer-stash-data-path.md</c>.
    /// </summary>
    public sealed class NinjaPricerPlugin : PluginBase
    {
        // Reading every inventory's item paths each frame is needless for a passive overlay; throttle.
        private const int RefreshEveryNFrames = 20;

        private readonly StashReader reader = new();
        private NinjaPricerSettings settings = new();
        private ActiveCoroutine? refreshCoroutine;
        private IDisposable? visibleStashLease;
        private IDisposable? visibleInventoryLease;
        private int framesUntilRefresh;

        // Set once per frame in DrawUI: true when the cursor is over any tracked item (an item is hovered,
        // so the tooltip is open). When set, both overlays draw a box only for the hovered cell and skip
        // the rest, so the tooltip stays uncovered while the inspected item's price stays visible.
        private bool showOnlyHoveredBox;

        // Latest read snapshot. Coroutines and DrawUI both run on the render thread, so a plain field
        // swap is safe — no locking needed.
        private List<StashInventorySnapshot> snapshot = new();

        private FileInfo SettingsFile => new(Path.Combine(this.DllDirectory, "config", "settings.json"));

        /// <inheritdoc/>
        public override string Name => "NinjaPricer";

        /// <inheritdoc/>
        public override string Description => "Draws poe.ninja price boxes over your open stash tab and main inventory.";

        /// <inheritdoc/>
        public override string Author => "OriathHub";

        /// <inheritdoc/>
        public override string Version => "1.0.0";

        /// <inheritdoc/>
        public override void OnEnable(bool isGameOpened)
        {
            this.settings = JsonHelper.CreateOrLoadJsonFile<NinjaPricerSettings>(this.SettingsFile);

            this.visibleStashLease = ImportantUiElements.RequestVisibleStashItems();
            this.visibleInventoryLease = ImportantUiElements.RequestVisibleInventoryItems();

            this.refreshCoroutine = CoroutineHandler.Start(this.RefreshSnapshot(), "NinjaPricer.RefreshSnapshot");
        }

        /// <inheritdoc/>
        public override void OnDisable()
        {
            this.refreshCoroutine?.Cancel();
            this.refreshCoroutine = null;
            this.visibleStashLease?.Dispose();
            this.visibleStashLease = null;
            this.visibleInventoryLease?.Dispose();
            this.visibleInventoryLease = null;
            this.snapshot = new();
        }

        /// <inheritdoc/>
        public override void SaveSettings()
        {
            JsonHelper.SaveToFile(this.settings, this.SettingsFile);
        }

        /// <inheritdoc/>
        public override void DrawSettings()
        {
            if (!ImGui.BeginTabBar("NinjaPricerSettings"))
            {
                return;
            }

            if (ImGui.BeginTabItem("General"))
            {
                ImGui.Checkbox("Draw price boxes over open stash tab", ref this.settings.ShowStashOverlay);
                ImGui.Checkbox("Draw price boxes over main inventory", ref this.settings.ShowInventoryOverlay);
                ImGui.Checkbox("While hovering, show only the hovered item's price (avoid covering tooltip)", ref this.settings.ShowOnlyHoveredItemPrice);

                var unit = (int)this.settings.Unit;
                if (ImGui.Combo("Display unit", ref unit, "Exalted\0Divine\0"))
                {
                    this.settings.Unit = (DisplayUnit)unit;
                }

                ImGui.Separator();
                ImGui.TextDisabled($"Pricing against '{Core.Prices.League}' (set the league in App Settings > Basic > poe.ninja Prices).");
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Advanced"))
            {
                ImGui.Checkbox("Show border box around items", ref this.settings.ShowItemBorder);
                ImGui.Checkbox("Show currency suffix on prices (ex / div)", ref this.settings.ShowUnitSuffix);

                var position = (int)this.settings.LabelPosition;
                if (ImGui.Combo("Price text position", ref position, "Above cell\0Top\0Center\0Bottom\0"))
                {
                    this.settings.LabelPosition = (PriceLabelPosition)position;
                }

                ImGui.ColorEdit4("Price text colour", ref this.settings.TextColor);
                ImGui.ColorEdit4("Price background colour", ref this.settings.BackgroundColor);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        /// <inheritdoc/>
        public override void DrawUI()
        {
            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                return;
            }

            // The game's item tooltip (ItemPopup) opens whenever an item is hovered and can overlap many
            // cells — including ones far from the cursor. Compute once per frame whether an item is hovered;
            // if so, both overlays draw only the hovered cell's box so the others don't cover the tooltip.
            this.showOnlyHoveredBox = this.settings.ShowOnlyHoveredItemPrice && this.CursorOverAnyTrackedItem();

            // The price-box overlays draw straight to the background draw list.
            if (this.settings.ShowStashOverlay)
            {
                this.DrawStashOverlay();
            }

            if (this.settings.ShowInventoryOverlay)
            {
                this.DrawInventoryOverlay();
            }
        }

        // Tint of the highlight border (the "yellow box"). Text and background colours are user-configurable.
        private static readonly Vector4 OverlayBorderColor = new(1f, 0.84f, 0.2f, 0.9f);

        // Draws a price box over each priced item in the currently-open stash tab. The resolver accepts
        // only occupied cells whose item entity exists in the matching ServerData inventory; the cell's own
        // UI rectangle supplies the screen geometry.
        private void DrawStashOverlay()
        {
            var tab = Core.States.InGameStateObject.GameUi.VisibleStashItems;
            if (tab.Count == 0)
            {
                return;
            }

            StashInventorySnapshot? inv = null;
            foreach (var s in this.snapshot)
            {
                if (s.Id == (int)tab[0].Inventory)
                {
                    inv = s;
                    break;
                }
            }

            if (inv == null || inv.Items.Count == 0)
            {
                return;
            }

            var divineRate = Core.Prices.GetDivineToExaltedRate(Core.Prices.League);
            var toDivine = this.settings.Unit == DisplayUnit.Divine && divineRate > 0;
            var divisor = toDivine ? divineRate : 1.0;
            var unitLabel = toDivine ? "div" : "ex";

            var draw = ImGui.GetBackgroundDrawList();

            // Bind only cells whose occupied item entity is present in the matching ServerData inventory.
            var itemsByEntity = new Dictionary<IntPtr, StashItem>(inv.Items.Count);
            foreach (var item in inv.Items)
            {
                itemsByEntity[item.EntityAddress] = item;
            }

            var viewport = Core.States.InGameStateObject.GameUi.LeftPanel;
            var drawnEntities = new HashSet<IntPtr>();
            foreach (var cell in tab)
            {
                if (!new ViewportAnchoredUiElement(cell.Element.Address, viewport).IsVisible)
                {
                    continue;
                }

                var entityAddress = cell.Entry.Item.Address;
                if (drawnEntities.Add(entityAddress) &&
                    itemsByEntity.TryGetValue(entityAddress, out var item))
                {
                    this.DrawPriceBox(draw, item, cell, divisor, unitLabel);
                }
            }
        }

        // Draws a price box over each priced item in the main inventory (right panel).
        private void DrawInventoryOverlay()
        {
            var cells = Core.States.InGameStateObject.GameUi.VisibleInventoryItems;
            if (cells.Count == 0)
            {
                return;
            }

            var snap = this.snapshot;
            StashInventorySnapshot? inv = null;
            foreach (var s in snap)
            {
                if (s.Id == (int)InventoryName.MainInventory1)
                {
                    inv = s;
                    break;
                }
            }

            if (inv == null || inv.Items.Count == 0)
            {
                return;
            }

            var divineRate = Core.Prices.GetDivineToExaltedRate(Core.Prices.League);
            var toDivine = this.settings.Unit == DisplayUnit.Divine && divineRate > 0;
            var divisor = toDivine ? divineRate : 1.0;
            var unitLabel = toDivine ? "div" : "ex";

            var draw = ImGui.GetBackgroundDrawList();

            var itemsByEntity = new Dictionary<IntPtr, StashItem>(inv.Items.Count);
            foreach (var item in inv.Items)
            {
                itemsByEntity[item.EntityAddress] = item;
            }

            var drawnEntities = new HashSet<IntPtr>();
            foreach (var cell in cells)
            {
                var entityAddress = cell.Entry.Item.Address;
                if (drawnEntities.Add(entityAddress) &&
                    itemsByEntity.TryGetValue(entityAddress, out var item))
                {
                    this.DrawPriceBox(draw, item, cell, divisor, unitLabel);
                }
            }
        }

        // Draws one item's price box: the optional highlight border plus the price label placed per the
        // configured position, using the user's text/background colours. Shared by both overlays.
        private void DrawPriceBox(ImDrawListPtr draw, StashItem item, VisibleStashItem cell, double divisor, string unitLabel)
        {
            // Only box items that resolved to a price.
            if (item.ValueExalted <= 0)
            {
                return;
            }

            var p0 = cell.Element.Position;
            var p1 = p0 + cell.Element.Size;

            // While an item is hovered (set once per frame), show the price only for the cell under the
            // cursor and hide the rest, so the others don't draw over the tooltip.
            if (this.showOnlyHoveredBox && !CursorInside(p0, p1))
            {
                return;
            }

            if (this.settings.ShowItemBorder)
            {
                draw.AddRect(p0, p1, ImGuiHelper.Color(OverlayBorderColor));
            }

            var label = this.settings.ShowUnitSuffix
                ? $"{item.ValueExalted / divisor:0.##} {unitLabel}"
                : $"{item.ValueExalted / divisor:0.##}";
            var textSize = ImGui.CalcTextSize(label);
            var origin = this.LabelOrigin(p0, p1, textSize);

            draw.AddRectFilled(
                new Vector2(origin.X - 2f, origin.Y - 1f),
                new Vector2(origin.X + textSize.X + 2f, origin.Y + textSize.Y + 1f),
                ImGuiHelper.Color(this.settings.BackgroundColor));
            draw.AddText(origin, ImGuiHelper.Color(this.settings.TextColor), label);
        }

        // Top-left screen position for the price label, per the configured position. "Above" left-aligns
        // the label just outside the top edge (the original layout); the inside positions centre it
        // horizontally so it reads cleanly over the icon.
        private Vector2 LabelOrigin(Vector2 p0, Vector2 p1, Vector2 textSize)
        {
            var centeredX = p0.X + (((p1.X - p0.X) - textSize.X) * 0.5f);
            return this.settings.LabelPosition switch
            {
                PriceLabelPosition.Top => new Vector2(centeredX, p0.Y + 1f),
                PriceLabelPosition.Center => new Vector2(centeredX, p0.Y + (((p1.Y - p0.Y) - textSize.Y) * 0.5f)),
                PriceLabelPosition.Bottom => new Vector2(centeredX, p1.Y - textSize.Y - 1f),
                _ => new Vector2(p0.X + 2f, p0.Y - textSize.Y - 2f),
            };
        }

        // True when the cursor is over any tracked occupied cell (stash or main inventory), i.e. an item
        // is hovered and the game's tooltip is open. Cell positions and the mouse are both in screen
        // space, so each test is a direct hit test. Stash cells are gated on viewport visibility so a
        // scrolled-out cell can't register a false hit.
        private bool CursorOverAnyTrackedItem()
        {
            var gameUi = Core.States.InGameStateObject.GameUi;

            var stash = gameUi.VisibleStashItems;
            if (stash.Count > 0)
            {
                var viewport = gameUi.LeftPanel;
                foreach (var cell in stash)
                {
                    if (new ViewportAnchoredUiElement(cell.Element.Address, viewport).IsVisible &&
                        CursorInside(cell.Element.Position, cell.Element.Position + cell.Element.Size))
                    {
                        return true;
                    }
                }
            }

            foreach (var cell in gameUi.VisibleInventoryItems)
            {
                if (CursorInside(cell.Element.Position, cell.Element.Position + cell.Element.Size))
                {
                    return true;
                }
            }

            return false;
        }

        // True when the mouse cursor is within the given screen rectangle. Cell positions and the mouse
        // are both in screen space, so this is a direct hit test.
        private static bool CursorInside(Vector2 p0, Vector2 p1)
        {
            var m = ImGui.GetMousePos();
            return m.X >= p0.X && m.X <= p1.X && m.Y >= p0.Y && m.Y <= p1.Y;
        }

        /// <summary>
        ///     Reads the snapshot off the per-frame data phase (the correct place for memory reads —
        ///     before drawing, on the render thread), throttled to every N frames.
        /// </summary>
        private IEnumerator<Wait> RefreshSnapshot()
        {
            while (true)
            {
                yield return new Wait(OriathEvents.PerFrameDataUpdate);

                if (Core.States.GameCurrentState != GameStateTypes.InGameState)
                {
                    if (this.snapshot.Count != 0)
                    {
                        this.snapshot = new();
                    }

                    continue;
                }

                if (this.framesUntilRefresh-- > 0)
                {
                    continue;
                }

                this.framesUntilRefresh = RefreshEveryNFrames;

                try
                {
                    // Empty inventories carry no items to price, so always skip them.
                    var fresh = this.reader.ReadAll(skipEmpty: true);
                    this.snapshot = fresh;
                }
                catch (Exception ex)
                {
                    Log.Error($"[NinjaPricer.RefreshSnapshot] {ex}");
                }
            }
        }
    }
}
