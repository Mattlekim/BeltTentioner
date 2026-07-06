using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Microsoft.Xna.Framework.Graphics;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// Builds a real MonoGame <see cref="SpriteFont"/> at runtime from any
    /// installed system font, so no content pipeline / .xnb files are needed.
    /// Glyphs (ASCII 32–126) are rasterized with GDI+ into a texture atlas and
    /// fed to SpriteFont's public constructor. Use the result with
    /// SpriteBatch.DrawString as usual.
    /// </summary>
    public static class RuntimeSpriteFont
    {
        public static SpriteFont Bake(GraphicsDevice device, string fontFamily, float sizePt,
                                      System.Drawing.FontStyle style = System.Drawing.FontStyle.Regular)
        {
            const char first = ' ', last = '~';
            int count = last - first + 1;

            using var font = new Font(fontFamily, sizePt, style, GraphicsUnit.Pixel);
            using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

            // Measure every glyph first to size the atlas.
            var advances = new float[count];
            int maxW = 1, lineH;
            using (var probe = Graphics.FromImage(new Bitmap(1, 1)))
            {
                probe.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                lineH = (int)Math.Ceiling(font.GetHeight(probe));
                for (int i = 0; i < count; i++)
                {
                    var sz = probe.MeasureString(((char)(first + i)).ToString(), font, PointF.Empty, format);
                    advances[i] = Math.Max(1f, sz.Width);
                    maxW = Math.Max(maxW, (int)Math.Ceiling(sz.Width));
                }
            }

            // Grid atlas: generous 2px padding so antialiased edges never bleed.
            const int pad = 2;
            int cellW = maxW + pad * 2, cellH = lineH + pad * 2;
            int cols = 12, rows = (count + cols - 1) / cols;

            using var bmp = new Bitmap(cols * cellW, rows * cellH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (int i = 0; i < count; i++)
                {
                    int cx = (i % cols) * cellW + pad, cy = (i / cols) * cellH + pad;
                    g.DrawString(((char)(first + i)).ToString(), font, Brushes.White, cx, cy, format);
                }
            }

            // Copy into a Texture2D, premultiplying alpha (SpriteBatch's default
            // AlphaBlend state expects premultiplied color).
            var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var pixels = new byte[bmp.Width * bmp.Height * 4];
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            finally { bmp.UnlockBits(data); }
            for (int p = 0; p < pixels.Length; p += 4)
            {
                byte b = pixels[p], g2 = pixels[p + 1], r = pixels[p + 2], a = pixels[p + 3];
                pixels[p]     = (byte)(r * a / 255); // GDI+ is BGRA; texture is RGBA
                pixels[p + 1] = (byte)(g2 * a / 255);
                pixels[p + 2] = (byte)(b * a / 255);
                pixels[p + 3] = a;
            }
            var texture = new Texture2D(device, bmp.Width, bmp.Height, false, SurfaceFormat.Color);
            texture.SetData(pixels);

            // Assemble the SpriteFont.
            var chars = new List<char>(count);
            var glyphBounds = new List<XnaRectangle>(count);
            var cropping = new List<XnaRectangle>(count);
            var kerning = new List<XnaVector3>(count);
            for (int i = 0; i < count; i++)
            {
                int w = (int)Math.Ceiling(advances[i]);
                chars.Add((char)(first + i));
                glyphBounds.Add(new XnaRectangle((i % cols) * cellW + pad, (i / cols) * cellH + pad, w, lineH));
                cropping.Add(new XnaRectangle(0, 0, w, lineH));
                kerning.Add(new XnaVector3(0f, w, advances[i] - w)); // advance = bearing + width + trail
            }
            return new SpriteFont(texture, glyphBounds, cropping, chars,
                                  lineH, 0f, kerning, defaultCharacter: '?');
        }
    }
}
