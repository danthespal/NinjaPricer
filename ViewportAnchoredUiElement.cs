namespace OriathHub.Plugins.NinjaPricer
{
    using OriathHub.RemoteObjects.UiElement;
    using System;

    /// <summary>
    ///     A <see cref="UiElementBase"/> whose <see cref="IsVisible"/> also requires intersection
    ///     with a given viewport element — useful for culling cells that are scrolled out of view.
    /// </summary>
    internal sealed class ViewportAnchoredUiElement(IntPtr address, UiElementBase viewport)
        : UiElementBase(address)
    {
        private readonly UiElementBase _viewport = viewport;

        /// <summary>Gets whether the element is visible AND intersects the viewport.</summary>
        public new bool IsVisible => GetVisibility() != Visibility.Hidden;

        /// <summary>Gets whether the element is visible but not fully contained by the viewport.</summary>
        public bool IsClipped => GetVisibility() == Visibility.Partial;

        private enum Visibility { Hidden, Partial, Full }

        private Visibility GetVisibility()
        {
            if (!base.IsVisible || _viewport is not { IsVisible: true })
                return Visibility.Hidden;

            var pos = Position;
            var size = Size;
            var vpPos = _viewport.Position;
            var vpSize = _viewport.Size;

            if (size.X <= 0 || size.Y <= 0 || vpSize.X <= 0 || vpSize.Y <= 0)
                return Visibility.Hidden;

            var right = pos.X + size.X;
            var bottom = pos.Y + size.Y;
            var vpRight = vpPos.X + vpSize.X;
            var vpBottom = vpPos.Y + vpSize.Y;

            if (pos.X >= vpRight || right <= vpPos.X || pos.Y >= vpBottom || bottom <= vpPos.Y)
                return Visibility.Hidden;

            if (pos.X >= vpPos.X && pos.Y >= vpPos.Y && right <= vpRight && bottom <= vpBottom)
                return Visibility.Full;

            return Visibility.Partial;
        }
    }
}
