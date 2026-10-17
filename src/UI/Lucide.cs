using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Linq;

namespace OpenCleaner.UI
{
    /// <summary>
    /// Desenha ícones Lucide (https://lucide.dev, licença ISC) a partir dos SVGs originais,
    /// embutidos como recurso "OpenCleaner.icons.&lt;nome&gt;.svg". Os ícones são traços
    /// numa grade 24x24, então basta converter cada elemento num GraphicsPath e desenhar
    /// com caneta arredondada.
    /// </summary>
    static class Lucide
    {
        class Icon
        {
            public GraphicsPath Stroke = new GraphicsPath();
            public GraphicsPath Fill = new GraphicsPath();
        }

        static readonly Dictionary<string, Icon> cache = new Dictionary<string, Icon>();

        static Icon Get(string name)
        {
            Icon icon;
            lock (cache)
            {
                if (cache.TryGetValue(name, out icon)) return icon;
                icon = Load(name);
                cache[name] = icon;
                return icon;
            }
        }

        static Icon Load(string name)
        {
            var icon = new Icon();
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("OpenCleaner.icons." + name + ".svg"))
            {
                if (s == null) return icon;
                XElement root;
                using (var r = new StreamReader(s)) root = XElement.Parse(r.ReadToEnd());
                foreach (var el in root.Descendants())
                {
                    var target = (string)el.Attribute("fill") == "currentColor" ? icon.Fill : icon.Stroke;
                    target.StartFigure();
                    switch (el.Name.LocalName)
                    {
                        case "path": SvgPath.Append(target, (string)el.Attribute("d") ?? ""); break;
                        case "circle":
                        {
                            float cx = F(el, "cx"), cy = F(el, "cy"), r = F(el, "r");
                            target.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);
                            break;
                        }
                        case "ellipse":
                        {
                            float cx = F(el, "cx"), cy = F(el, "cy"), rx = F(el, "rx"), ry = F(el, "ry");
                            target.AddEllipse(cx - rx, cy - ry, 2 * rx, 2 * ry);
                            break;
                        }
                        case "rect":
                            AddRect(target, F(el, "x"), F(el, "y"), F(el, "width"), F(el, "height"),
                                    el.Attribute("rx") != null ? F(el, "rx") : F(el, "ry"),
                                    el.Attribute("ry") != null ? F(el, "ry") : F(el, "rx"));
                            break;
                        case "line":
                            target.AddLine(F(el, "x1"), F(el, "y1"), F(el, "x2"), F(el, "y2"));
                            break;
                        case "polyline":
                        case "polygon":
                        {
                            var n = SvgPath.Numbers((string)el.Attribute("points") ?? "");
                            var pts = new List<PointF>();
                            for (int i = 0; i + 1 < n.Count; i += 2) pts.Add(new PointF(n[i], n[i + 1]));
                            if (pts.Count > 1) target.AddLines(pts.ToArray());
                            if (el.Name.LocalName == "polygon") target.CloseFigure();
                            break;
                        }
                    }
                }
            }
            return icon;
        }

        static float F(XElement el, string attr)
        {
            var a = el.Attribute(attr);
            return a == null ? 0f : float.Parse(a.Value, CultureInfo.InvariantCulture);
        }

        static void AddRect(GraphicsPath p, float x, float y, float w, float h, float rx, float ry)
        {
            rx = Math.Min(rx, w / 2);
            ry = Math.Min(ry, h / 2);
            if (rx <= 0 || ry <= 0) { p.AddRectangle(new RectangleF(x, y, w, h)); return; }
            p.AddArc(x, y, 2 * rx, 2 * ry, 180, 90);
            p.AddArc(x + w - 2 * rx, y, 2 * rx, 2 * ry, 270, 90);
            p.AddArc(x + w - 2 * rx, y + h - 2 * ry, 2 * rx, 2 * ry, 0, 90);
            p.AddArc(x, y + h - 2 * ry, 2 * rx, 2 * ry, 90, 90);
            p.CloseFigure();
        }

        /// <summary>Desenha o ícone centralizado no retângulo.</summary>
        public static void Draw(Graphics g, string name, RectangleF bounds, Color color, float strokeWidth = 2f)
        {
            var icon = Get(name);
            float size = Math.Min(bounds.Width, bounds.Height);
            float scale = size / 24f;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TranslateTransform(bounds.X + (bounds.Width - size) / 2, bounds.Y + (bounds.Height - size) / 2);
            g.ScaleTransform(scale, scale);
            using (var pen = new Pen(color, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawPath(pen, icon.Stroke);
            if (icon.Fill.PointCount > 0)
                using (var b = new SolidBrush(color)) g.FillPath(b, icon.Fill);
            g.Restore(state);
        }

        public static Bitmap Render(string name, int size, Color color, float strokeWidth = 2f)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp)) Draw(g, name, new RectangleF(0, 0, size, size), color, strokeWidth);
            return bmp;
        }
    }

    /// <summary>Converte o atributo "d" de um path SVG em figuras de um GraphicsPath.</summary>
    static class SvgPath
    {
        public static List<float> Numbers(string s)
        {
            var list = new List<float>();
            int i = 0;
            while (i < s.Length)
            {
                float v;
                if (TryNumber(s, ref i, out v)) list.Add(v);
                else i++;
            }
            return list;
        }

        static void SkipSeparators(string s, ref int i)
        {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++;
        }

        static bool TryNumber(string s, ref int i, out float value)
        {
            SkipSeparators(s, ref i);
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            bool digits = false, dot = false;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsDigit(c)) { digits = true; i++; }
                else if (c == '.' && !dot) { dot = true; i++; }
                else break;
            }
            if (digits && i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                int save = i++;
                if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
                if (i < s.Length && char.IsDigit(s[i])) { while (i < s.Length && char.IsDigit(s[i])) i++; }
                else i = save;
            }
            if (!digits) { i = start; value = 0; return false; }
            value = float.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
            return true;
        }

        // Flags de arco podem vir coladas ("a1 1 0 011 1"): lê um único caractere 0/1.
        static bool TryFlag(string s, ref int i, out bool flag)
        {
            SkipSeparators(s, ref i);
            flag = false;
            if (i < s.Length && (s[i] == '0' || s[i] == '1')) { flag = s[i] == '1'; i++; return true; }
            return false;
        }

        public static void Append(GraphicsPath path, string d)
        {
            int i = 0;
            char cmd = ' ';
            PointF cur = PointF.Empty, start = PointF.Empty, lastCtrl = PointF.Empty;
            char lastCmd = ' ';

            while (true)
            {
                SkipSeparators(d, ref i);
                if (i >= d.Length) break;
                if (char.IsLetter(d[i]) && d[i] != 'e' && d[i] != 'E') cmd = d[i++];
                else if (cmd == ' ') break;

                bool rel = char.IsLower(cmd);
                char up = char.ToUpperInvariant(cmd);
                float x, y, x1, y1, x2, y2;
                Func<float, float> RX = v => rel ? cur.X + v : v;
                Func<float, float> RY = v => rel ? cur.Y + v : v;

                switch (up)
                {
                    case 'M':
                        if (!TryNumber(d, ref i, out x) || !TryNumber(d, ref i, out y)) return;
                        cur = new PointF(RX(x), RY(y));
                        start = cur;
                        path.StartFigure();
                        cmd = rel ? 'l' : 'L'; // pares seguintes são linhas
                        lastCmd = 'M';
                        continue;
                    case 'L':
                        if (!TryNumber(d, ref i, out x) || !TryNumber(d, ref i, out y)) return;
                        Line(path, ref cur, new PointF(RX(x), RY(y)));
                        break;
                    case 'H':
                        if (!TryNumber(d, ref i, out x)) return;
                        Line(path, ref cur, new PointF(RX(x), cur.Y));
                        break;
                    case 'V':
                        if (!TryNumber(d, ref i, out y)) return;
                        Line(path, ref cur, new PointF(cur.X, RY(y)));
                        break;
                    case 'C':
                        if (!TryNumber(d, ref i, out x1) || !TryNumber(d, ref i, out y1) || !TryNumber(d, ref i, out x2) ||
                            !TryNumber(d, ref i, out y2) || !TryNumber(d, ref i, out x) || !TryNumber(d, ref i, out y)) return;
                        {
                            var c1 = new PointF(RX(x1), RY(y1));
                            var c2 = new PointF(RX(x2), RY(y2));
                            var end = new PointF(RX(x), RY(y));
                            path.AddBezier(cur, c1, c2, end);
                            lastCtrl = c2;
                            cur = end;
                        }
                        break;
                    case 'S':
                        if (!TryNumber(d, ref i, out x2) || !TryNumber(d, ref i, out y2) || !TryNumber(d, ref i, out x) || !TryNumber(d, ref i, out y)) return;
                        {
                            var c1 = "CS".IndexOf(lastCmd) >= 0 ? new PointF(2 * cur.X - lastCtrl.X, 2 * cur.Y - lastCtrl.Y) : cur;
                            var c2 = new PointF(RX(x2), RY(y2));
                            var end = new PointF(RX(x), RY(y));
                            path.AddBezier(cur, c1, c2, end);
                            lastCtrl = c2;
                            cur = end;
                        }
                        break;
                    case 'Q':
                        if (!TryNumber(d, ref i, out x1) || !TryNumber(d, ref i, out y1) || !TryNumber(d, ref i, out x) || !TryNumber(d, ref i, out y)) return;
                        {
                            var q = new PointF(RX(x1), RY(y1));
                            var end = new PointF(RX(x), RY(y));
                            Quad(path, cur, q, end);
                            lastCtrl = q;
                            cur = end;
                        }
                        break;
                    case 'T':
                        if (!TryNumber(d, ref i, out x) || !TryNumber(d, ref i, out y)) return;
                        {
                            var q = "QT".IndexOf(lastCmd) >= 0 ? new PointF(2 * cur.X - lastCtrl.X, 2 * cur.Y - lastCtrl.Y) : cur;
                            var end = new PointF(RX(x), RY(y));
                            Quad(path, cur, q, end);
                            lastCtrl = q;
                            cur = end;
                        }
                        break;
                    case 'A':
                    {
                        float rx, ry, rot;
                        bool large, sweep;
                        if (!TryNumber(d, ref i, out rx) || !TryNumber(d, ref i, out ry) || !TryNumber(d, ref i, out rot) ||
                            !TryFlag(d, ref i, out large) || !TryFlag(d, ref i, out sweep) ||
                            !TryNumber(d, ref i, out x) || !TryNumber(d, ref i, out y)) return;
                        var end = new PointF(RX(x), RY(y));
                        Arc(path, cur, end, rx, ry, rot, large, sweep);
                        cur = end;
                        break;
                    }
                    case 'Z':
                        path.CloseFigure();
                        cur = start;
                        lastCmd = 'Z';
                        cmd = ' ';
                        continue;
                    default:
                        return;
                }
                lastCmd = up;
            }
        }

        static void Line(GraphicsPath p, ref PointF cur, PointF to)
        {
            p.AddLine(cur, to);
            cur = to;
        }

        static void Quad(GraphicsPath p, PointF a, PointF q, PointF b)
        {
            p.AddBezier(a, new PointF(a.X + 2f / 3 * (q.X - a.X), a.Y + 2f / 3 * (q.Y - a.Y)),
                           new PointF(b.X + 2f / 3 * (q.X - b.X), b.Y + 2f / 3 * (q.Y - b.Y)), b);
        }

        /// <summary>Arco elíptico SVG (parametrização por extremos) aproximado por Béziers cúbicas.</summary>
        static void Arc(GraphicsPath p, PointF a, PointF b, double rx, double ry, double rotDeg, bool large, bool sweep)
        {
            if (rx == 0 || ry == 0 || (a.X == b.X && a.Y == b.Y)) { if (a != b) p.AddLine(a, b); return; }
            rx = Math.Abs(rx); ry = Math.Abs(ry);
            double phi = rotDeg * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (a.X - b.X) / 2.0, dy = (a.Y - b.Y) / 2.0;
            double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
            double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
            if (lambda > 1) { double s = Math.Sqrt(lambda); rx *= s; ry *= s; }
            double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
            double den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            double coef = Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
            double cxp = coef * rx * y1 / ry, cyp = -coef * ry * x1 / rx;
            double cx = cos * cxp - sin * cyp + (a.X + b.X) / 2.0;
            double cy = sin * cxp + cos * cyp + (a.Y + b.Y) / 2.0;

            Func<double, double, double, double, double> angle = (ux, uy, vx, vy) =>
            {
                double ang = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
                return ang;
            };
            double t1 = angle(1, 0, (x1 - cxp) / rx, (y1 - cyp) / ry);
            double dt = angle((x1 - cxp) / rx, (y1 - cyp) / ry, (-x1 - cxp) / rx, (-y1 - cyp) / ry);
            if (!sweep && dt > 0) dt -= 2 * Math.PI;
            else if (sweep && dt < 0) dt += 2 * Math.PI;

            int segs = (int)Math.Ceiling(Math.Abs(dt) / (Math.PI / 2));
            double step = dt / segs;
            double k = 4.0 / 3 * Math.Tan(step / 4);
            var from = a;
            for (int s = 0; s < segs; s++)
            {
                double ta = t1 + s * step, tb = ta + step;
                double ca = Math.Cos(ta), sa = Math.Sin(ta), cb = Math.Cos(tb), sb = Math.Sin(tb);
                Func<double, double, PointF> map = (ex, ey) =>
                    new PointF((float)(cx + rx * ex * cos - ry * ey * sin), (float)(cy + rx * ex * sin + ry * ey * cos));
                var c1 = map(ca - k * sa, sa + k * ca);
                var c2 = map(cb + k * sb, sb - k * cb);
                var to = s == segs - 1 ? b : map(cb, sb);
                p.AddBezier(from, c1, c2, to);
                from = to;
            }
        }
    }

    /// <summary>Controle que exibe um ícone Lucide.</summary>
    class IconBox : Control
    {
        string icon;
        public float StrokeWidth = 2f;

        public IconBox(string icon, int size, Color color)
        {
            this.icon = icon;
            Size = new Size(size, size);
            ForeColor = color;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        public string Icon
        {
            get { return icon; }
            set { icon = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Lucide.Draw(e.Graphics, icon, new RectangleF(0, 0, Width, Height), ForeColor, StrokeWidth);
        }
    }
}
