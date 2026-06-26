namespace OriathHub.Plugins.NinjaPricer
{
    using System;

    /// <summary>
    ///     One item read from a stash/inventory. A pure data model — the component values are read
    ///     once by <see cref="StashReader"/> off the host's <c>Item</c> entity, then consumed by the
    ///     pricer. Free of any reading or rendering concern.
    /// </summary>
    public sealed class StashItem
    {
        /// <summary>The backing item entity address used to join ServerData items to visible UI cells.</summary>
        public IntPtr EntityAddress { get; init; }

        /// <summary>The full metadata path, e.g. <c>Metadata/Items/Currency/CurrencyUpgradeToMagic</c>.</summary>
        public string Path { get; init; } = string.Empty;

        /// <summary>
        ///     Readable name. Starts as the path leaf (e.g. <c>CurrencyUpgradeToMagic</c>) and is upgraded
        ///     to the localized base name (e.g. <c>Orb of Augmentation</c>) by the pricer once resolved.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Current stack size; 1 for non-stackable items.</summary>
        public int StackCount { get; init; } = 1;

        /// <summary>Max stack size for the base type; 0 when unknown/not stackable.</summary>
        public int MaxStackCount { get; init; }

        /// <summary>Rarity value (0=Normal,1=Magic,2=Rare,3=Unique).</summary>
        public int RarityValue { get; init; }

        /// <summary>RenderItem art path (identifies uniques); empty when absent.</summary>
        public string ResourcePath { get; init; } = string.Empty;

        /// <summary>poe.ninja value of this item's stack in Exalted Orbs; 0 until priced (set by the pricer).</summary>
        public double ValueExalted { get; set; }

        /// <summary>Quality % (display only); 0 when the item has no quality.</summary>
        public int Quality { get; init; }

        /// <summary>Gem level (display only); 0 for non-gems.</summary>
        public int GemLevel { get; init; }

        /// <summary>Grid column of the item's top-left cell within its inventory (0-based).</summary>
        public int SlotX { get; init; }

        /// <summary>Grid row of the item's top-left cell within its inventory (0-based).</summary>
        public int SlotY { get; init; }

        /// <summary>Item width in grid cells (1 for a single-cell item).</summary>
        public int SlotW { get; init; } = 1;

        /// <summary>Item height in grid cells (1 for a single-cell item).</summary>
        public int SlotH { get; init; } = 1;
    }
}
