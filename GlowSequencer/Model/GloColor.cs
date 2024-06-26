﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GlowSequencer.Model
{
    public enum ColorTransformMode
    {
        // Warning: Serialized by string in TransferSettings, careful with renaming!
        None,
        Brighten,
        Darken
    }

    public struct GloColor
    {
        /// <summary>Threshold of the brightest channel of a color. In bright mode, all darker colors will be brigthened to reach this threshold.</summary>
        private const int TOO_DARK_THRESHOLD = 20;
        /// <summary>Threshold of the brightest channel of a color. In dark mode, all brighter colors will be darkened to reach this threshold.</summary>
        private const int TOO_BRIGHT_THRESHOLD = 10;

        public static GloColor Black { get { return GloColor.FromRGB(0, 0, 0); } }
        public static GloColor White { get { return GloColor.FromRGB(255, 255, 255); } }


        // values are allowed to be out of [0,255] range
        public int r;
        public int g;
        public int b;

        private GloColor(int r, int g, int b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
        }

        public void Normalize()
        {
            if (r < 0) r = 0;
            if (g < 0) g = 0;
            if (b < 0) b = 0;
            if (r > 255) r = 255;
            if (g > 255) g = 255;
            if (b > 255) b = 255;
        }

        public string ToHexString()
        {
            return r.ToString("x2") + g.ToString("x2") + b.ToString("x2");
        }

        public override string ToString()
        {
            return string.Format("({0}, {1}, {2})", r, g, b);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is GloColor))
                return false;

            GloColor other = (GloColor)obj;
            return r == other.r && g == other.g && b == other.b;
        }

        public override int GetHashCode()
        {
            int h = 23 * r;
            h = 23 * h + g;
            h = 23 * h + b;
            return h;
        }


        // Note: Removed operator because it makes it easy to introduce rounding errors.
        //public static GloColor operator *(double a, GloColor c)
        //{
        //    int r = (int)Math.Round(a * c.r);
        //    int g = (int)Math.Round(a * c.g);
        //    int b = (int)Math.Round(a * c.b);
        //    return new GloColor(r, g, b);
        //}
        //public static GloColor operator *(GloColor c, double a)
        //{
        //    return a * c;
        //}

        public static GloColor operator *(GloColor c1, GloColor c2)
        {
            return new GloColor(c1.r * c2.r / 255, c1.g * c2.g / 255, c1.b * c2.b / 255);
        }

        public static GloColor operator +(GloColor c1, GloColor c2)
        {
            return new GloColor(c1.r + c2.r, c1.g + c2.g, c1.b + c2.b);
        }

        public static GloColor operator -(GloColor c1, GloColor c2)
        {
            return new GloColor(c1.r - c2.r, c1.g - c2.g, c1.b - c2.b);
        }

        public static bool operator ==(GloColor c1, GloColor c2)
        {
            return c1.Equals(c2);
        }
        public static bool operator !=(GloColor c1, GloColor c2)
        {
            return !c1.Equals(c2);
        }

        public static GloColor FromRGB(int r, int g, int b)
        {
            var col = new GloColor(r, g, b);
            col.Normalize();
            return col;
        }


        public static GloColor FromHexString(string str)
        {
            if (str == null)
                throw new ArgumentNullException("str");

            if (str.StartsWith("0x"))
                str = str.Substring(2);

            int num = int.Parse(str, System.Globalization.NumberStyles.HexNumber);
            int r = (num >> 16) & 0xFF;
            int g = (num >> 8) & 0xFF;
            int b = (num >> 0) & 0xFF;

            return new GloColor(r, g, b);
        }

        public static GloColor Blend(GloColor c1, GloColor c2, double pct)
        {
            if (pct <= 0)
                return c1;
            if (pct >= 1)
                return c2;
            if (c1 == c2)
                return c1;

            //return (1 - pct) * c1 + pct * c2;
            int r = (int)Math.Round((1.0 - pct) * c1.r + pct * c2.r);
            int g = (int)Math.Round((1.0 - pct) * c1.g + pct * c2.g);
            int b = (int)Math.Round((1.0 - pct) * c1.b + pct * c2.b);
            return new GloColor(r, g, b);
        }

        /// <summary>Returns a copy of this color, potentially adjusted to bright/dark mode.</summary>
        public static GloColor TransformToMode(GloColor color, ColorTransformMode mode)
        {
            if (mode == ColorTransformMode.None)
                return color;

            int brightestValue = Math.Max(color.r, Math.Max(color.g, color.b));
            // Do not modify black or colors that are already bright/dark enough.
            if (brightestValue == 0)
                return color;
            if (mode == ColorTransformMode.Brighten && brightestValue >= TOO_DARK_THRESHOLD)
                return color;
            if (mode == ColorTransformMode.Darken && brightestValue <= TOO_BRIGHT_THRESHOLD)
                return color;

            int targetBrightness = (mode == ColorTransformMode.Brighten ? TOO_DARK_THRESHOLD : TOO_BRIGHT_THRESHOLD);
            float factor = (float)targetBrightness / brightestValue;
            return FromRGB(
                    (int)Math.Round(color.r * factor),
                    (int)Math.Round(color.g * factor),
                    (int)Math.Round(color.b * factor));
        }

    }
}
