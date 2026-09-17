using System.Drawing;

namespace MelonLoader.Logging
{
    // MelonLoader's console colour type for compatibility
    public partial struct ColorARGB
    {
        private readonly byte _a;
        private readonly byte _r;
        private readonly byte _g;
        private readonly byte _b;

        public ColorARGB(byte a, byte r, byte g, byte b) { _a = a; _r = r; _g = g; _b = b; }
        public ColorARGB(byte r, byte g, byte b) : this(255, r, g, b) { }

        public byte A { get { return _a; } }
        public byte R { get { return _r; } }
        public byte G { get { return _g; } }
        public byte B { get { return _b; } }

        public static implicit operator ColorARGB(Color color) { return new ColorARGB(color.A, color.R, color.G, color.B); }
        public static implicit operator Color(ColorARGB color) { return Color.FromArgb(color._a, color._r, color._g, color._b); }

        public bool Equals(ColorARGB other) { return _a == other._a && _r == other._r && _g == other._g && _b == other._b; }
        public override bool Equals(object obj) { return obj is ColorARGB && Equals((ColorARGB)obj); }

        public override int GetHashCode() { return (_a << 24) | (_r << 16) | (_g << 8) | _b; }

        public static bool operator ==(ColorARGB left, ColorARGB right) { return left.Equals(right); }
        public static bool operator !=(ColorARGB left, ColorARGB right) { return !left.Equals(right); }

        public override string ToString()
        {
            return "#" + _a.ToString("X2") + _r.ToString("X2") + _g.ToString("X2") + _b.ToString("X2");
        }
    }
}
