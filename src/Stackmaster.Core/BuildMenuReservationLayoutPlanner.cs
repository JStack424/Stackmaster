using System;

namespace Stackmaster.Core
{
    /// <summary>Pure local-UI geometry for the optional row anchored above the actual build menu.</summary>
    public static class BuildMenuReservationLayoutPlanner
    {
        public const float Gap = 8f;
        public const float Height = 54f;
        public const float SafeMargin = 8f;

        public static BuildMenuReservationLayout Plan(
            float menuLeft,
            float menuRight,
            float menuTop,
            float safeLeft,
            float safeRight,
            float safeTop)
        {
            RequireFinite(menuLeft, nameof(menuLeft));
            RequireFinite(menuRight, nameof(menuRight));
            RequireFinite(menuTop, nameof(menuTop));
            RequireFinite(safeLeft, nameof(safeLeft));
            RequireFinite(safeRight, nameof(safeRight));
            RequireFinite(safeTop, nameof(safeTop));
            if (menuRight < menuLeft || safeRight < safeLeft)
                throw new ArgumentOutOfRangeException();

            var allowedLeft = safeLeft + SafeMargin;
            var allowedRight = safeRight - SafeMargin;
            var availableWidth = Math.Max(0f, allowedRight - allowedLeft);
            var width = Math.Min(menuRight - menuLeft, availableWidth);
            var left = Math.Max(allowedLeft, Math.Min(menuLeft, allowedRight - width));
            var bottom = menuTop + Gap;
            var visible = width > 0f && bottom + Height <= safeTop - SafeMargin;
            return new BuildMenuReservationLayout(visible, left, bottom, width, Height);
        }

        private static void RequireFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public sealed class BuildMenuReservationLayout
    {
        public BuildMenuReservationLayout(bool visible, float left, float bottom, float width, float height)
        {
            Visible = visible;
            Left = left;
            Bottom = bottom;
            Width = width;
            Height = height;
        }

        public bool Visible { get; }
        public float Left { get; }
        public float Bottom { get; }
        public float Width { get; }
        public float Height { get; }
    }
}
