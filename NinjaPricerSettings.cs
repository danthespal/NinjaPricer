namespace OriathHub.Plugins.NinjaPricer
{
    using System.Numerics;

    /// <summary>The currency unit prices are displayed in (values are always stored internally in Exalted).</summary>
    public enum DisplayUnit
    {
        /// <summary>Show values in Exalted Orbs (the internal/primary unit).</summary>
        Exalted = 0,

        /// <summary>Show values in Divine Orbs (converted via the loaded Divine→Exalted rate).</summary>
        Divine = 1,
    }

    /// <summary>Where the price label is drawn relative to the item's cell.</summary>
    public enum PriceLabelPosition
    {
        /// <summary>Just above the cell, outside it — keeps the item icon fully visible (the original layout).</summary>
        Above = 0,

        /// <summary>Inside the cell, anchored to the top edge.</summary>
        Top = 1,

        /// <summary>Centered inside the cell.</summary>
        Center = 2,

        /// <summary>Inside the cell, anchored to the bottom edge.</summary>
        Bottom = 3,
    }

    /// <summary>
    ///     Persisted settings for the <see cref="NinjaPricerPlugin"/>.
    ///     Plain public fields serialized via Newtonsoft.Json — the host convention.
    /// </summary>
    public sealed class NinjaPricerSettings
    {
        /// <summary>Draw price boxes over the items in the currently-open stash tab.</summary>
        public bool ShowStashOverlay = true;

        /// <summary>Draw price boxes over the items in the main inventory (right panel).</summary>
        public bool ShowInventoryOverlay = true;

        /// <summary>While an item is hovered, show only that item's price box and hide the others, so none cover the game's item tooltip.</summary>
        public bool ShowOnlyHoveredItemPrice = true;

        /// <summary>Currency unit prices are displayed in.</summary>
        public DisplayUnit Unit = DisplayUnit.Exalted;

        // --- Advanced appearance ---

        /// <summary>Draw the highlight border (the "yellow box") around each priced cell.</summary>
        public bool ShowItemBorder = true;

        /// <summary>Append the currency unit ("ex"/"div") after the price number.</summary>
        public bool ShowUnitSuffix = false;

        /// <summary>Where the price label is drawn relative to the cell.</summary>
        public PriceLabelPosition LabelPosition = PriceLabelPosition.Above;

        /// <summary>Price text colour.</summary>
        public Vector4 TextColor = new(1f, 1f, 1f, 1f);

        /// <summary>Colour of the filled background behind the price text.</summary>
        public Vector4 BackgroundColor = new(0f, 0f, 0f, 0.6f);
    }
}
