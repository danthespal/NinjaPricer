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
    ///     Lists every loaded inventory — including open stash tabs — in a draggable window.
    ///
    ///     All memory reading lives in <see cref="StashReader"/> (the seam meant to become a framework
    ///     API); this class only owns the plugin lifecycle, the refresh coroutine, and the UI. Items
    ///     come from ServerData (clean identity for all loaded tabs); the stash UI is not used as the
    ///     item source because its per-cell item pointer is obfuscated — see
    ///     <c>.investigation/ninjapricer-stash-data-path.md</c>.
    /// </summary>
    public sealed class NinjaPricerPlugin : PluginBase
    {
        // Reading every inventory's item paths each frame is needless for a passive list; throttle.
        private const int RefreshEveryNFrames = 20;

        // Gold tint for rows at/above the highlight threshold.
        private static readonly Vector4 ValuableColor = new(1f, 0.84f, 0.2f, 1f);

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
        public override string Description => "Lists the items in your open stash tabs (and other inventories) in a window.";

        /// <inheritdoc/>
        public override string Author => "OriathHub";

        /// <inheritdoc/>
        public override string Version => "0.3.0";

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
            ImGui.Checkbox("Show stash-items window", ref this.settings.Show);
            ImGui.Checkbox("Draw price boxes over open stash tab", ref this.settings.ShowStashOverlay);
            ImGui.Checkbox("Draw price boxes over main inventory", ref this.settings.ShowInventoryOverlay);
            ImGui.Checkbox("While hovering, show only the hovered item's price (avoid covering tooltip)", ref this.settings.ShowOnlyHoveredItemPrice);
            ImGui.Checkbox("Hide empty inventories", ref this.settings.HideEmptyInventories);

            var unit = (int)this.settings.Unit;
            if (ImGui.Combo("Display unit", ref unit, "Exalted\0Divine\0"))
            {
                this.settings.Unit = (DisplayUnit)unit;
            }

            ImGui.Checkbox("Hide items with no price", ref this.settings.HideUnpriced);
            ImGui.SliderFloat("Hide rows below (ex)", ref this.settings.MinValueExalted, 0f, 50f, "%.2f");
            ImGui.Checkbox("Highlight valuable rows", ref this.settings.ColorByValue);
            if (this.settings.ColorByValue)
            {
                ImGui.SliderFloat("Highlight at (ex)", ref this.settings.HighlightThresholdExalted, 0f, 100f, "%.2f");
            }

            ImGui.Separator();
            ImGui.TextDisabled($"Pricing against '{Core.Prices.League}' (set the league in App Settings → Basic → poe.ninja Prices).");
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

            // The price-box overlays draw straight to the background draw list (independent of the list
            // window), so they run even when the list window is hidden.
            if (this.settings.ShowStashOverlay)
            {
                this.DrawStashOverlay();
            }

            if (this.settings.ShowInventoryOverlay)
            {
                this.DrawInventoryOverlay();
            }

            if (!this.settings.Show)
            {
                return;
            }

            ImGui.SetNextWindowBgAlpha(0.7f);
            if (ImGui.Begin("NinjaPricer — Stash Items"))
            {
                var league = Core.Prices.League;
                var priceStatus = Core.Prices.GetStatus(league);
                if (!priceStatus.HasData)
                {
                    ImGui.TextDisabled($"Prices: loading for '{league}'…");
                }
                else
                {
                    ImGui.TextDisabled($"Prices loaded ({league}) — Divine ~ {Core.Prices.GetDivineToExaltedRate(league):0.#} Exalted");
                }

                ImGui.Separator();

                // Divine display divides Exalted values by the loaded rate; fall back to Exalted if the
                // rate isn't loaded or is non-positive (avoids divide-by-zero before prices arrive).
                var divineRate = Core.Prices.GetDivineToExaltedRate(league);
                var toDivine = this.settings.Unit == DisplayUnit.Divine && divineRate > 0;
                var divisor = toDivine ? divineRate : 1.0;
                var unitLabel = toDivine ? "div" : "ex";

                var snap = this.snapshot;
                if (snap.Count == 0)
                {
                    ImGui.TextDisabled("No items found. Open your stash.");
                }

                var grandTotal = 0.0;
                foreach (var inv in snap)
                {
                    var tabTotal = 0.0;
                    foreach (var item in inv.Items)
                    {
                        tabTotal += item.ValueExalted;
                    }

                    grandTotal += tabTotal;

                    if (ImGui.CollapsingHeader($"{inv.Name} — {inv.Items.Count} items — {tabTotal / divisor:0.##} {unitLabel}##{inv.Id}"))
                    {
                        foreach (var item in inv.Items)
                        {
                            // Hide rows with no resolved price when the user opts out of unpriced items.
                            if (this.settings.HideUnpriced && item.ValueExalted <= 0)
                            {
                                continue;
                            }

                            // Min-value filter hides low/zero-value rows; 0 shows everything.
                            if (this.settings.MinValueExalted > 0 && item.ValueExalted < this.settings.MinValueExalted)
                            {
                                continue;
                            }

                            var name = item.DisplayName + StatsSuffix(item);
                            var label = item.ValueExalted > 0
                                ? $"• {name} — {item.ValueExalted / divisor:0.##} {unitLabel}"
                                : $"• {name}";

                            if (this.settings.ColorByValue && item.ValueExalted >= this.settings.HighlightThresholdExalted && item.ValueExalted > 0)
                            {
                                ImGui.TextColored(ValuableColor, label);
                            }
                            else
                            {
                                ImGui.TextUnformatted(label);
                            }
                        }
                    }
                }

                if (snap.Count > 0)
                {
                    ImGui.Separator();
                    ImGui.Text($"Total: {grandTotal / divisor:0.##} {(toDivine ? "Divine" : "Exalted")}");
                }
            }

            ImGui.End();
        }

        // Border tint for the price boxes drawn over stash items.
        private static readonly Vector4 OverlayBorderColor = new(1f, 0.84f, 0.2f, 0.9f);
        private static readonly Vector4 OverlayLabelBgColor = new(0f, 0f, 0f, 0.6f);
        private static readonly Vector4 OverlayLabelColor = new(1f, 1f, 1f, 1f);

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
            var border = ImGuiHelper.Color(OverlayBorderColor);
            var labelBg = ImGuiHelper.Color(OverlayLabelBgColor);
            var labelColor = ImGuiHelper.Color(OverlayLabelColor);

            void DrawBox(StashItem item, VisibleStashItem cell)
            {
                // Only box items that resolved to a price, honouring the same min-value filter as the list.
                if (item.ValueExalted <= 0)
                {
                    return;
                }

                if (this.settings.MinValueExalted > 0 && item.ValueExalted < this.settings.MinValueExalted)
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

                draw.AddRect(p0, p1, border);

                // Label sits just ABOVE the cell so it doesn't cover the item icon.
                var label = $"{item.ValueExalted / divisor:0.##} {unitLabel}";
                var textSize = ImGui.CalcTextSize(label);
                var labelTop = p0.Y - textSize.Y - 2f;
                draw.AddRectFilled(new Vector2(p0.X, labelTop), new Vector2(p0.X + textSize.X + 4f, p0.Y), labelBg);
                draw.AddText(new Vector2(p0.X + 2f, labelTop + 1f), labelColor, label);
            }

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
                    DrawBox(item, cell);
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
            var border = ImGuiHelper.Color(OverlayBorderColor);
            var labelBg = ImGuiHelper.Color(OverlayLabelBgColor);
            var labelColor = ImGuiHelper.Color(OverlayLabelColor);

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
                    DrawBox(item, cell);
                }
            }

            void DrawBox(StashItem item, VisibleStashItem cell)
            {
                if (item.ValueExalted <= 0)
                {
                    return;
                }

                if (this.settings.MinValueExalted > 0 && item.ValueExalted < this.settings.MinValueExalted)
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

                draw.AddRect(p0, p1, border);

                var label = $"{item.ValueExalted / divisor:0.##} {unitLabel}";
                var textSize = ImGui.CalcTextSize(label);
                var labelTop = p0.Y - textSize.Y - 2f;
                draw.AddRectFilled(new Vector2(p0.X, labelTop), new Vector2(p0.X + textSize.X + 4f, p0.Y), labelBg);
                draw.AddText(new Vector2(p0.X + 2f, labelTop + 1f), labelColor, label);
            }
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

        // Read-only stat annotation appended to a row (gem level / quality % / socket count). Each part
        // is shown only when present, so plain items render no suffix.
        private static string StatsSuffix(StashItem item)
        {
            var parts = new List<string>(2);
            if (item.GemLevel > 0)
            {
                parts.Add($"Lvl {item.GemLevel}");
            }

            if (item.Quality > 0)
            {
                parts.Add($"Q{item.Quality}%");
            }

            return parts.Count == 0 ? string.Empty : $" ({string.Join(", ", parts)})";
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
                    var fresh = this.reader.ReadAll(this.settings.HideEmptyInventories);
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
