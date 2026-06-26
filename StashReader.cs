namespace OriathHub.Plugins.NinjaPricer
{
    using OriathHub;
    using OriathHub.RemoteEnums;
    using OriathHub.RemoteObjects.Components;
    using OriathHub.RemoteObjects.States.InGameStateObjects;
    using System;
    using System.Collections.Generic;

    /// <summary>
    ///     Reads every loaded inventory — including open stash tabs — through the host's public
    ///     <see cref="ServerData"/> API. The host owns the ServerData→inventory→item memory walk and
    ///     exposes each item as an <see cref="Item"/> entity, so this reader just enumerates the
    ///     inventories and reads the components it needs off each item via <c>TryGetComponent</c>.
    ///
    ///     This replaced an earlier hand-rolled raw-memory walk now that the framework exposes the
    ///     inventory API (<see cref="ServerData.AvailableInventories"/>/<see cref="ServerData.GetInventory"/>)
    ///     and the item components (Stack/Mods/Quality/SkillGem/RenderItem).
    /// </summary>
    public sealed class StashReader
    {
        /// <summary>
        ///     Reads a snapshot of every loaded inventory. When <paramref name="skipEmpty"/> is set,
        ///     inventories with no items are omitted. Returns an empty list when not in a game.
        /// </summary>
        public List<StashInventorySnapshot> ReadAll(bool skipEmpty)
        {
            var result = new List<StashInventorySnapshot>();

            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                return result;
            }

            var serverData = Core.States.InGameStateObject.CurrentAreaInstance.ServerDataObject;
            foreach (var name in serverData.AvailableInventories)
            {
                var inventory = serverData.GetInventory(name);
                var items = ReadInventoryItems(inventory);
                if (items.Count == 0 && skipEmpty)
                {
                    continue;
                }

                result.Add(new StashInventorySnapshot
                {
                    Id = (int)name,
                    InventoryAddress = inventory.Address,
                    Name = LabelFor(name),
                    Cols = inventory.TotalBoxes.X,
                    Rows = inventory.TotalBoxes.Y,
                    Items = items,
                });
            }

            return result;
        }

        // Reads the distinct, valid items of one inventory into the plugin's DTO.
        private static List<StashItem> ReadInventoryItems(Inventory inventory)
        {
            var items = new List<StashItem>();
            foreach (var entry in inventory.Entries)
            {
                var item = entry.Item;
                if (!item.IsValid || string.IsNullOrEmpty(item.Path))
                {
                    continue;
                }

                var stashItem = BuildStashItem(item, (entry.X, entry.Y, entry.Width, entry.Height));
                if (Core.Prices.TryGetPrice(item, Core.Prices.League, out var quote))
                {
                    stashItem.DisplayName = quote.DisplayName;
                    stashItem.ValueExalted = quote.ExaltedValue;
                }

                items.Add(stashItem);
            }

            return items;
        }

        // Reads the components the pricer/UI need off a host item entity.
        private static StashItem BuildStashItem(Item item, (int X, int Y, int W, int H) pos)
        {
            var stackCount = 1;
            var maxStackCount = 0;
            if (item.TryGetComponent<Stack>(out var stack))
            {
                if (stack.Count > 0)
                {
                    stackCount = stack.Count;
                }

                maxStackCount = stack.MaxCount;
            }

            var rarity = item.TryGetComponent<Mods>(out var mods) ? (int)mods.Rarity : 0;
            var resourcePath = item.TryGetComponent<RenderItem>(out var render) ? render.ResourcePath : string.Empty;
            var quality = item.TryGetComponent<Quality>(out var qualityComp) ? qualityComp.ItemQuality : 0;
            var gemLevel = item.TryGetComponent<SkillGem>(out var gem) ? gem.Level : 0;

            return new StashItem
            {
                EntityAddress = item.Address,
                Path = item.Path,
                DisplayName = Leaf(item.Path),
                StackCount = stackCount,
                MaxStackCount = maxStackCount,
                RarityValue = rarity,
                ResourcePath = resourcePath,
                Quality = quality,
                GemLevel = gemLevel,
                SlotX = pos.X,
                SlotY = pos.Y,
                SlotW = pos.W,
                SlotH = pos.H,
            };
        }

        // The last path segment is the readable item id (e.g. "CurrencyUpgradeToMagic").
        private static string Leaf(string path)
        {
            var i = path.LastIndexOf('/');
            return i >= 0 && i < path.Length - 1 ? path[(i + 1)..] : path;
        }

        // Friendly label: the InventoryName enum name when known, else the raw stash-tab id.
        private static string LabelFor(InventoryName name) =>
            Enum.IsDefined(typeof(InventoryName), name) ? name.ToString() : $"Inventory {(int)name}";
    }
}
