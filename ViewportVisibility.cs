namespace OriathHub.Plugins.NinjaPricer
{
    using OriathHub.RemoteObjects.UiElement;

    /// <summary>
    ///     Viewport-intersection visibility test for a cell already resolved as a
    ///     <see cref="UiElementBase"/> — culls cells scrolled out of view. Takes the already-resolved
    ///     element directly instead of constructing a second UiElementBase at its address: a fresh,
    ///     parentless UiElementBase resolves its ancestor chain through UiElementParents'
    ///     StandaloneParents dictionary cache instead of reusing the caller's live knownParent
    ///     reference — wasted work the caller already paid for, and one that silently reports
    ///     IsVisible=false (hiding an otherwise-valid cell) whenever StandaloneParents hasn't cached
    ///     that chain yet.
    /// </summary>
    internal static class ViewportVisibility
    {
        /// <summary>Gets whether <paramref name="element"/> is visible and intersects <paramref name="viewport"/>.</summary>
        public static bool IsVisible(UiElementBase element, UiElementBase viewport)
        {
            if (!element.IsVisible || !viewport.IsVisible)
            {
                return false;
            }

            var pos = element.Position;
            var size = element.Size;
            var vpPos = viewport.Position;
            var vpSize = viewport.Size;

            if (size.X <= 0 || size.Y <= 0 || vpSize.X <= 0 || vpSize.Y <= 0)
            {
                return false;
            }

            var right = pos.X + size.X;
            var bottom = pos.Y + size.Y;
            var vpRight = vpPos.X + vpSize.X;
            var vpBottom = vpPos.Y + vpSize.Y;

            return pos.X < vpRight && right > vpPos.X && pos.Y < vpBottom && bottom > vpPos.Y;
        }
    }
}
