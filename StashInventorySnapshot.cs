namespace OriathHub.Plugins.NinjaPricer
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    ///     One inventory's contents as read from ServerData. Open stash tabs appear here alongside the
    ///     player's own inventories (see <c>.investigation/ninjapricer-stash-data-path.md</c> §3), so a
    ///     single snapshot type covers both. Part of the would-be framework API surface.
    /// </summary>
    public sealed class StashInventorySnapshot
    {
        /// <summary>The raw ServerData inventory id (stash tabs use dynamic ids beyond the named enum).</summary>
        public int Id { get; init; }

        /// <summary>The backing ServerData <c>InventoryStruct</c> address.</summary>
        public IntPtr InventoryAddress { get; init; }

        /// <summary>A friendly label resolved by the labelling layer (enum name when known, else the raw id).</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Inventory grid width in cells (<c>TotalBoxes.X</c>).</summary>
        public int Cols { get; init; }

        /// <summary>Inventory grid height in cells (<c>TotalBoxes.Y</c>).</summary>
        public int Rows { get; init; }

        /// <summary>The items currently held in this inventory.</summary>
        public IReadOnlyList<StashItem> Items { get; init; } = new List<StashItem>();
    }
}
