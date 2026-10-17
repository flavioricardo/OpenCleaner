// Gera assets\OpenCleaner.ico: ícone Lucide "brush-cleaning" em branco sobre um círculo
// verde-azulado, em PNG embutido no ICO (16, 24, 32, 48, 64 e 256 px). Compilado por tools\make-icon.ps1.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using OpenCleaner.UI;

static class MakeIcon
{
    static byte[] Png(int s)
    {
        using (var bmp = new Bitmap(s, s))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var rect = new RectangleF(0.5f, 0.5f, s - 1, s - 1);
                using (var b = new LinearGradientBrush(rect, Color.FromArgb(0, 184, 160), Color.FromArgb(0, 105, 140), 45f))
                    g.FillEllipse(b, rect);
                // Em tamanhos pequenos o traço precisa ser mais grosso para continuar legível.
                float stroke = s <= 16 ? 2.8f : s <= 32 ? 2.4f : 2f;
                float pad = s * 0.22f;
                Lucide.Draw(g, "brush-cleaning", new RectangleF(pad, pad, s - 2 * pad, s - 2 * pad), Color.White, stroke);
            }
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }

    static int Main(string[] args)
    {
        string output = args[0];
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var images = new List<byte[]>();
        foreach (var s in sizes) images.Add(Png(s));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        using (var w = new BinaryWriter(File.Create(output)))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write((uint)images[i].Length); w.Write((uint)offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
        // Segundo argumento opcional: prévia em PNG 256 px para conferir o desenho.
        if (args.Length > 1) File.WriteAllBytes(args[1], images[images.Count - 1]);
        Console.WriteLine("Icone gerado: " + output);
        return 0;
    }
}
