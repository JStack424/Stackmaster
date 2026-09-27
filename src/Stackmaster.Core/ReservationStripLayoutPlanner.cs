using System;

namespace Stackmaster.Core
{
    /// <summary>
    /// Pure geometry for the optional reservation strip. The label's preferred rendered width is
    /// authoritative; the icon viewport always begins after that width plus a visible internal gap.
    /// Screen-space room is converted to player-local UI units so UI scale changes cannot re-create
    /// the label/icon overlap.
    /// </summary>
    public static class ReservationStripLayoutPlanner
    {
        public const float PreviousRootOffsetX = 8f;
        public const float PreviousRootOffsetY = 0f;
        public const float RootOffsetX = PreviousRootOffsetX + 12f;
        public const float RootOffsetY = PreviousRootOffsetY - 10f;
        public const float RootHeight = 54f;
        public const float SafeRightMargin = 8f;
        public const float LabelLeftInset = 8f;
        public const float LabelRenderPadding = 2f;
        public const float LabelToIconGap = 8f;
        public const float ViewportRightInset = 4f;

        public static ReservationStripLayout Plan(
            float availableScreenPixels,
            float playerScaleX,
            float preferredLabelWidth)
        {
            RequireFiniteNonNegative(availableScreenPixels, nameof(availableScreenPixels));
            RequireFiniteNonNegative(preferredLabelWidth, nameof(preferredLabelWidth));
            if (float.IsNaN(playerScaleX) || float.IsInfinity(playerScaleX) || playerScaleX <= 0f)
                throw new ArgumentOutOfRangeException(nameof(playerScaleX));

            var availableLocalWidth = availableScreenPixels / playerScaleX;
            var rootWidth = Math.Max(0f, availableLocalWidth - RootOffsetX - SafeRightMargin);
            var labelWidth = (float)Math.Ceiling(preferredLabelWidth + LabelRenderPadding);
            var viewportLeft = LabelLeftInset + labelWidth + LabelToIconGap;
            var unclampedViewportRight = rootWidth - ViewportRightInset;
            var viewportRight = Math.Max(viewportLeft, unclampedViewportRight);

            return new ReservationStripLayout(
                RootOffsetX,
                RootOffsetY,
                rootWidth,
                labelWidth,
                viewportLeft,
                viewportRight);
        }

        private static void RequireFiniteNonNegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public sealed class ReservationStripLayout
    {
        public ReservationStripLayout(
            float rootOffsetX,
            float rootOffsetY,
            float rootWidth,
            float labelWidth,
            float viewportLeft,
            float viewportRight)
        {
            RootOffsetX = rootOffsetX;
            RootOffsetY = rootOffsetY;
            RootWidth = rootWidth;
            LabelWidth = labelWidth;
            ViewportLeft = viewportLeft;
            ViewportRight = viewportRight;
        }

        public float RootOffsetX { get; }
        public float RootOffsetY { get; }
        public float RootWidth { get; }
        public float LabelWidth { get; }
        public float ViewportLeft { get; }
        public float ViewportRight { get; }
    }
}
