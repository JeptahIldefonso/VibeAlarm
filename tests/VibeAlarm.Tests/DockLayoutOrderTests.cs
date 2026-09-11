using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace VibeAlarm.Tests
{
    /// <summary>
    /// Locks the WinForms dock-order rule the shell depends on: docking is applied in
    /// REVERSE z-order — the LAST control added to a container docks FIRST. Therefore the
    /// Dock.Fill child must be added FIRST so it docks LAST and receives only the space left
    /// after its Top/Bottom/Left/Right siblings claim their strips.
    ///
    /// MainForm follows this pattern in every mixed-dock container (Form: mainContainer Fill
    /// before sidebarPanel Left; mainContainer: contentPanel Fill before statusBarPanel Bottom
    /// and headerPanel Top; sidebarPanel: navFlow Fill before bottomHost Bottom). These tests
    /// prove the pattern lays out without overlap — and that the reversed order (Fill added
    /// last) produces exactly the overlapping-layers bug it is sometimes mistaken to fix.
    /// </summary>
    public class DockLayoutOrderTests
    {
        [Fact]
        public void Fill_added_first_receives_only_the_space_left_by_edges()
        {
            using var host = new Panel { Size = new Size(800, 600) };
            using var content = new Panel { Dock = DockStyle.Fill };
            using var status = new Panel { Dock = DockStyle.Bottom, Height = 64 };
            using var header = new Panel { Dock = DockStyle.Top, Height = 92 };

            // The app's order (BuildMainWorkspaceLayout): Fill FIRST, edges after.
            host.Controls.Add(content);
            host.Controls.Add(status);
            host.Controls.Add(header);
            host.PerformLayout();

            Assert.Equal(0, header.Top);
            Assert.Equal(600 - 64, status.Top);
            Assert.Equal(new Rectangle(0, 92, 800, 600 - 92 - 64), content.Bounds);
        }

        [Fact]
        public void Fill_added_last_overlaps_the_edges_documenting_the_failure_mode()
        {
            using var host = new Panel { Size = new Size(800, 600) };
            using var content = new Panel { Dock = DockStyle.Fill };
            using var status = new Panel { Dock = DockStyle.Bottom, Height = 64 };
            using var header = new Panel { Dock = DockStyle.Top, Height = 92 };

            // The reversed order: the Fill control docks FIRST and claims the entire
            // container; the edge panels then sit ON TOP of it as overlapping layers.
            host.Controls.Add(status);
            host.Controls.Add(header);
            host.Controls.Add(content);
            host.PerformLayout();

            Assert.Equal(600, content.Height); // Fill swallowed everything — edges overlap it
        }
    }
}
