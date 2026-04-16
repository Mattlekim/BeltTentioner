using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace belttentiontest.Controls
{
    /// <summary>
    /// A GroupBox that can be collapsed to just its header row via a toggle button (?/?).
    /// When collapsed the control shrinks to the header height; when expanded it restores
    /// its original full height. All child controls are hidden while collapsed so they do
    /// not participate in tab-order or layout.
    /// </summary>
    public class CollapsibleGroupBox : GroupBox
    {
        // Height of just the header strip (title + toggle button).
        private const int HeaderHeight = 22;

        private bool _collapsed = false;
        private bool _collapsible = true;
        private int _expandedHeight;
        private Button _toggleButton;

        public CollapsibleGroupBox()
        {
            _toggleButton = new Button
            {
                Text = "-",
                FlatStyle = FlatStyle.Flat,
                Size = new Size(25, 20),
                TabStop = false,
                Cursor = Cursors.Hand,
                ForeColor = Color.FromArgb(160, 160, 190),
                BackColor = Color.FromArgb(30, 30, 50),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.TopCenter,
            };
            _toggleButton.FlatAppearance.BorderSize = 0;
            _toggleButton.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 90);
            _toggleButton.Click += ToggleButton_Click;

            // DesignMode is always false in constructors; use LicenseManager instead.
            if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
                Controls.Add(_toggleButton);
        }

        /// <summary>Gets or sets whether the group box is currently collapsed.</summary>
        public bool Collapsed
        {
            get => _collapsed;
            set
            {
                if (_collapsed == value) return;
                _collapsed = value;
                ApplyState();
            }
        }

        /// <summary>
        /// Gets or sets whether the group box can be collapsed by the user.
        /// When false the toggle button is hidden and the box cannot be collapsed.
        /// </summary>
        public bool Collapsible
        {
            get => _collapsible;
            set
            {
                if (_collapsible == value) return;
                _collapsible = value;
                if (DesignMode) return;

                if (!_collapsible)
                {
                    // Ensure expanded before hiding the button.
                    if (_collapsed) Collapsed = false;
                    _toggleButton.Visible = false;
                }
                else
                {
                    _toggleButton.Visible = true;
                }
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (!DesignMode) PositionToggleButton();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            // Remember the full height whenever we are expanded.
            if (!DesignMode && !_collapsed && Height > HeaderHeight)
                _expandedHeight = Height;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (DesignMode) return;
            _expandedHeight = Height;
            // Late-add the toggle button in case it was skipped in the constructor.
            if (!Controls.Contains(_toggleButton))
                Controls.Add(_toggleButton);
            PositionToggleButton();
        }

        private void PositionToggleButton()
        {
            if (_toggleButton == null) return;
            // Place the button at the far-right of the header strip.
            _toggleButton.Location = new Point(Width - _toggleButton.Width - 4, 3);
            _toggleButton.BringToFront();
        }

        private void ToggleButton_Click(object? sender, EventArgs e)
        {
            if (_collapsible) Collapsed = !_collapsed;
        }

        private void ApplyState()
        {
            if (DesignMode) return;
            SuspendLayout();
            if (_collapsed)
            {
                // Hide every child except the toggle button.
                foreach (Control c in Controls)
                    if (c != _toggleButton) c.Visible = false;

                Height = HeaderHeight;
                _toggleButton.Text = "+";  // pointing down = click to expand
                _toggleButton.ForeColor = Color.FromArgb(80, 200, 120);
            }
            else
            {
                Height = _expandedHeight;
                _toggleButton.Text = "-";  // pointing up = click to collapse
                _toggleButton.ForeColor = Color.FromArgb(160, 160, 190);

                foreach (Control c in Controls)
                    c.Visible = true;
            }
            ResumeLayout();

            // Ask the parent panel to re-flow positions so siblings below shift up/down.
            if (Parent != null)
                RearrangeSiblings();
        }

        /// <summary>
        /// Shifts every sibling CollapsibleGroupBox that sits strictly below this
        /// control so that they close the gap (or make room) created by this
        /// control's height change. The top position of this control and all
        /// controls above it are never touched.
        /// </summary>
        private void RearrangeSiblings()
        {
            if (Parent == null) return;

            const int gap = 6;

            // Collect all sibling CollapsibleGroupBoxes sorted by their designer Top.
            var siblings = new System.Collections.Generic.List<CollapsibleGroupBox>();
            foreach (Control c in Parent.Controls)
                if (c is CollapsibleGroupBox cgb)
                    siblings.Add(cgb);

            // Sort by the original (design-time) top stored in Tag; fall back to
            // current Top only for controls that have not been registered yet.
            siblings.Sort((a, b) =>
            {
                int ta = a.Tag is int i1 ? i1 : a.Top;
                int tb = b.Tag is int i2 ? i2 : b.Top;
                return ta.CompareTo(tb);
            });

            if (siblings.Count == 0) return;

            // Find this control in the sorted list and restack everything below it.
            int idx = siblings.IndexOf(this);
            if (idx < 0) return;

            // Walk downward from the control immediately after this one.
            int y = this.Top + this.Height + gap;
            for (int i = idx + 1; i < siblings.Count; i++)
            {
                siblings[i].Top = y;
                y += siblings[i].Height + gap;
            }

            Parent.Invalidate();
        }

        /// <summary>
        /// Call this after all siblings have been added to the parent and their initial
        /// positions are set. It records each sibling's sort-order (by current Top) so
        /// that <see cref="RearrangeSiblings"/> can always restore the correct order
        /// even while Tops are being mutated.
        /// </summary>
        public static void RegisterSiblingOrder(System.Collections.Generic.IEnumerable<CollapsibleGroupBox> boxes)
        {
            int i = 0;
            foreach (var b in boxes)
                b.Tag = i++;
        }
    }
}
