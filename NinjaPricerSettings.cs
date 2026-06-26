namespace OriathHub.Plugins.NinjaPricer
{
    /// <summary>The currency unit prices are displayed in (values are always stored internally in Exalted).</summary>
    public enum DisplayUnit
    {
        /// <summary>Show values in Exalted Orbs (the internal/primary unit).</summary>
        Exalted = 0,

        /// <summary>Show values in Divine Orbs (converted via the loaded Divine→Exalted rate).</summary>
        Divine = 1,
    }

    /// <summary>
    ///     Persisted settings for the <see cref="NinjaPricerSettings"/>.
    ///     Plain public fields serialized via Newtonsoft.Json — the host convention.
    /// </summary>
    public sealed class NinjaPricerSettings
    {
        /// <summary>Show the stash-items window while in-game.</summary>
        public bool Show = true;

        /// <summary>Draw price boxes over the items in the currently-open stash tab.</summary>
        public bool ShowStashOverlay = true;

        /// <summary>Draw price boxes over the items in the main inventory (right panel).</summary>
        public bool ShowInventoryOverlay = true;

        /// <summary>Hide inventories that currently hold no items (e.g. empty equipment slots).</summary>
        public bool HideEmptyInventories = true;

        /// <summary>While an item is hovered, show only that item's price box and hide the others, so none cover the game's item tooltip.</summary>
        public bool ShowOnlyHoveredItemPrice = true;

        /// <summary>Currency unit prices are displayed in.</summary>
        public DisplayUnit Unit = DisplayUnit.Exalted;

        /// <summary>Hide item rows worth less than this (in Exalted) from the list; 0 shows everything.</summary>
        public float MinValueExalted = 0f;

        /// <summary>Hide item rows that have no poe.ninja price (value 0) — e.g. unpriced/unknown items.</summary>
        public bool HideUnpriced = false;

        /// <summary>Tint rows worth at least <see cref="HighlightThresholdExalted"/> (in Exalted) to flag valuables.</summary>
        public bool ColorByValue = true;

        /// <summary>Exalted value at or above which a row is tinted as valuable (when <see cref="ColorByValue"/> is on).</summary>
        public float HighlightThresholdExalted = 1f;
    }
}
