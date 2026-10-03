using System.Collections.Generic;
using System.Drawing;

namespace WinASM65.Monitor.Abstractions
{
    /// <summary>
    /// A source of frames for the video pane: geometry, palette, and a capture.
    ///
    /// The frame is a <see cref="Color"/> two-dimensional array because that is what
    /// the shell's <c>ImageView</c> takes, which is the plan's D2 decision, and
    /// <c>System.Drawing.Color</c> is already in use there — so this adds a
    /// dependency to nothing. Row 0 is the top row, column 0 the leftmost pixel, and
    /// both indices are in pixels, not tiles.
    ///
    /// A machine with no frames does not implement this interface at all. The host
    /// then states that there is no video rather than showing an empty pane, which is
    /// the whole point of splitting it out of <see cref="IExecutionAdapter"/>: the
    /// virtual target executes the CPU and has no PPU, so video is absent from its
    /// capabilities instead of faked, and MesenCE is dropped from video entirely,
    /// because a frozen machine has nothing to show.
    ///
    /// Nothing here throttles. A 60 Hz source outruns the render loop, so frame rate
    /// is the host's to police and the pane's to report.
    /// </summary>
    public interface IVideoProvider
    {
        /// <summary>Name of the source, for the status line and the capability table.</summary>
        string DisplayName { get; }

        /// <summary>Frame width in pixels, constant for the life of the provider.</summary>
        int Width { get; }

        /// <summary>Frame height in pixels, constant for the life of the provider.</summary>
        int Height { get; }

        /// <summary>
        /// The palette the machine's colours are expressed in, indexed by colour
        /// index. Empty when the source has no fixed palette.
        /// </summary>
        IReadOnlyList<Color> Palette { get; }

        /// <summary>
        /// Captures the current frame.
        ///
        /// Returns an array of <see cref="Width"/> by <see cref="Height"/> pixels. The
        /// caller owns it, so a provider that reuses one buffer must return a copy —
        /// and a provider that has no frame must say so rather than return an empty
        /// array, which would render as a valid black screen.
        /// </summary>
        Color[,] CaptureFrame();
    }
}