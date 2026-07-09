using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// Vertical list of <see cref="MonoXRControl"/>s with one selected (highlighted)
    /// item. Up/Down move the selection (wrapping, skipping disabled items);
    /// Increase/Decrease go to the selected control — a slider steps its value,
    /// a checkbox toggles. Wire it up from the hosting overlay with
    /// <c>OverlayNavigation.Navigated += menu.HandleNavigation;</c> and call
    /// <see cref="Update"/>/<see cref="Draw"/> from the overlay's own passes.
    /// Items are laid out automatically inside <see cref="MonoXRControl.Bounds"/>.
    /// </summary>
    public sealed class MonoXRMenuControl : MonoXRControl
    {
        private readonly List<MonoXRControl> _items = new();
        private int _selected = -1;

        public int ItemHeight { get; set; } = 64;
        public int ItemSpacing { get; set; } = 8;

        public IReadOnlyList<MonoXRControl> Items => _items;

        public MonoXRControl? SelectedItem =>
            _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

        public void Add(MonoXRControl item)
        {
            _items.Add(item);
            if (_selected < 0 && item.IsEnabled)
                _selected = _items.Count - 1;
            SyncSelection();
        }

        public void Clear()
        {
            _items.Clear();
            _selected = -1;
        }

        public void HandleNavigation(OverlayNavAction action)
        {
            switch (action)
            {
                case OverlayNavAction.Up: MoveSelection(-1); break;
                case OverlayNavAction.Down: MoveSelection(+1); break;
                case OverlayNavAction.PreviousControl: MoveSelection(-1); break;
                case OverlayNavAction.NextControl: MoveSelection(+1); break;
                case OverlayNavAction.Increase: SelectedItem?.Increase(); break;
                case OverlayNavAction.Decrease: SelectedItem?.Decrease(); break;
            }
        }

        // Forward adjust actions so a menu can itself sit inside another menu.
        public override void Increase() => SelectedItem?.Increase();
        public override void Decrease() => SelectedItem?.Decrease();

        /// <summary>Moves the selection by <paramref name="direction"/>, wrapping and skipping disabled items.</summary>
        public void MoveSelection(int direction)
        {
            if (_items.Count == 0) return;
            int start = _selected < 0 ? 0 : _selected;
            int i = start;
            for (int step = 0; step < _items.Count; step++)
            {
                i = (i + direction + _items.Count) % _items.Count;
                if (_items[i].IsEnabled) break;
            }
            _selected = i;
            SyncSelection();
        }

        public override void Update(GameTime gameTime)
        {
            LayoutItems();
            foreach (var item in _items)
                item.Update(gameTime);
        }

        public override void Draw(SpriteBatch spriteBatch, SpriteFont font, Texture2D white)
        {
            LayoutItems();
            foreach (var item in _items)
                item.Draw(spriteBatch, font, white);
        }

        /// <summary>Stacks items top-to-bottom inside <see cref="MonoXRControl.Bounds"/>.</summary>
        private void LayoutItems()
        {
            int y = Bounds.Y;
            foreach (var item in _items)
            {
                int h = item.PreferredHeight > 0 ? item.PreferredHeight : ItemHeight;
                item.Bounds = new XnaRectangle(Bounds.X, y, Bounds.Width, h);
                y += h + ItemSpacing;
            }
        }

        private void SyncSelection()
        {
            for (int i = 0; i < _items.Count; i++)
                _items[i].IsSelected = i == _selected;
        }
    }
}
