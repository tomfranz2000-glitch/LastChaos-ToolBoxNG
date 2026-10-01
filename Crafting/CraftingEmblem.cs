using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace LastChaos_ToolBoxNG.Crafting;

// Read-only first-frame preview; no SlimDX device and no texture conversion.
// Version-4 header layout and flags follow Engine/Graphics/Texture.cpp.
public static class CraftingEmblem
{
    public static Bitmap Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Texture exceeds the preview size limit.");
        using var reader = new BinaryReader(stream);
        string Tag() => Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (Tag() != "TVER") throw new InvalidDataException("Not a TVER texture. Preview supports the deployed version-4 interface textures.");
        uint version = reader.ReadUInt32();
        if ((version & 65535) != 4 || (version >> 16) > 1 || Tag() != "TDAT") throw new InvalidDataException("Unsupported texture version for preview.");
        uint width, height, shift, flags, frames;
        if ((version >> 16) == 1) {
            width = reader.ReadUInt32() ^ 303316286U; shift = reader.ReadUInt32() ^ 1431797889U;
            height = reader.ReadUInt32() ^ 2560279492U; _ = reader.ReadUInt32();
            flags = reader.ReadUInt32() ^ 505432394U; frames = reader.ReadUInt32() ^ 1633913997U;
        } else {
            flags = reader.ReadUInt32(); width = reader.ReadUInt32(); height = reader.ReadUInt32();
            _ = reader.ReadUInt32(); shift = reader.ReadUInt32(); frames = reader.ReadUInt32();
        }
        if (shift > 15 || frames < 1) throw new InvalidDataException("Invalid texture dimensions.");
        width >>= (int)shift; height >>= (int)shift;
        if (width is < 1 or > 2048 || height is < 1 or > 2048) throw new InvalidDataException("Preview dimensions must be 1–2048 pixels.");
        int w = (int)width, h = (int)height;
        byte[] pixels = new byte[w * h * 4];
        string format = Tag();
        if (format == "FRMS") {
            for (int n = 0; n < w * h; n++) {
                byte r = reader.ReadByte(), g = reader.ReadByte(), b = reader.ReadByte();
                byte a = (flags & 1) != 0 ? reader.ReadByte() : (byte)255;
                pixels[n * 4] = b; pixels[n * 4 + 1] = g; pixels[n * 4 + 2] = r; pixels[n * 4 + 3] = a;
            }
        } else if (format == "FRMC") {
            int size = reader.ReadInt32();
            bool interpolated = (flags & 33) == 33;
            bool alpha = (flags & 1) != 0 && (interpolated || (flags & 8) == 0);
            int required = ((w + 3) / 4) * ((h + 3) / 4) * (alpha ? 16 : 8);
            if (size < required + 4 || size > stream.Length - stream.Position) throw new InvalidDataException("Truncated compressed texture.");
            int firstMipSize = reader.ReadInt32();
            if(firstMipSize < required || firstMipSize > size - 4) throw new InvalidDataException("Invalid compressed mip size.");
            DecodeBlocks(reader, pixels, w, h, alpha, interpolated);
        } else throw new InvalidDataException("This texture has no previewable first frame.");
        var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new(0, 0, w, h), ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); } finally { bitmap.UnlockBits(data); }
        return bitmap;
    }
    private static void DecodeBlocks(BinaryReader r, byte[] pixels, int w, int h, bool alpha, bool interpolated)
    {
        for (int y = 0; y < h; y += 4) for (int x = 0; x < w; x += 4) {
            byte[] opacity = Enumerable.Repeat((byte)255, 16).ToArray();
            if (alpha && !interpolated) {
                ulong bits = r.ReadUInt64();
                for (int i = 0; i < 16; i++) opacity[i] = (byte)(((bits >> (4 * i)) & 15) * 17);
            } else if (alpha) {
                byte[] values = new byte[8]; values[0] = r.ReadByte(); values[1] = r.ReadByte();
                if (values[0] > values[1]) for (int i = 2; i < 8; i++) values[i] = (byte)(((8 - i) * values[0] + (i - 1) * values[1]) / 7);
                else { for (int i = 2; i < 6; i++) values[i] = (byte)(((6 - i) * values[0] + (i - 1) * values[1]) / 5); values[7] = 255; }
                ulong bits = 0; for (int i = 0; i < 6; i++) bits |= (ulong)r.ReadByte() << (8 * i);
                for (int i = 0; i < 16; i++) opacity[i] = values[(int)((bits >> (3 * i)) & 7)];
            }
            ushort c0 = r.ReadUInt16(), c1 = r.ReadUInt16(); uint selectors = r.ReadUInt32();
            int[][] colors = [Expand(c0), Expand(c1), new int[3], new int[3]];
            for (int c = 0; c < 3; c++) {
                colors[2][c] = c0 > c1 || alpha ? (2 * colors[0][c] + colors[1][c]) / 3 : (colors[0][c] + colors[1][c]) / 2;
                colors[3][c] = c0 > c1 || alpha ? (colors[0][c] + 2 * colors[1][c]) / 3 : 0;
            }
            for (int i = 0; i < 16; i++) {
                int px = x + i % 4, py = y + i / 4, pick = (int)((selectors >> (2 * i)) & 3);
                if (px >= w || py >= h) continue;
                int n = (py * w + px) * 4;
                pixels[n] = (byte)colors[pick][2]; pixels[n + 1] = (byte)colors[pick][1]; pixels[n + 2] = (byte)colors[pick][0];
                pixels[n + 3] = !alpha && c0 <= c1 && pick == 3 ? (byte)0 : opacity[i];
            }
        }
    }
    private static int[] Expand(ushort c) => [((c >> 11) & 31) * 255 / 31, ((c >> 5) & 63) * 255 / 63, (c & 31) * 255 / 31];
}
