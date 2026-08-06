using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlayStationSaveManager.Services;

public sealed class Ps2IconModel
{
    public sealed record AnimationKey(float Time, float Value);
    public sealed record AnimationFrame(int ShapeId, IReadOnlyList<AnimationKey> Keys);

    public required short[][] Shapes { get; init; }
    public required short[] NormalsUv { get; init; }
    public required byte[] VertexColors { get; init; }
    public required ushort[] Texture { get; init; }
    public required int VertexCount { get; init; }
    public required int FrameLength { get; init; }
    public required float AnimationSpeed { get; init; }
    public required int PlayOffset { get; init; }
    public required IReadOnlyList<AnimationFrame> Frames { get; init; }
    public Ps2IconRenderSettings RenderSettings { get; set; } =
        Ps2IconRenderSettings.Default;

    // Renderer V2 decisions are computed once from a canonical pose,
    // preventing visible mode changes during animation or rotation.
    internal bool? V2UseTextureMask { get; set; }
    internal object V2DecisionSync { get; } = new();

    public static Ps2IconModel Parse(byte[] data)
    {
        using var stream = new MemoryStream(data, false);
        using var reader = new BinaryReader(stream);

        if (stream.Length < 20) throw new InvalidDataException("Icon model is too small.");
        var magic = reader.ReadUInt32();
        if (magic != 0x00010000) throw new InvalidDataException("Invalid PS2 icon magic.");

        var shapeCount = checked((int)reader.ReadUInt32());
        var textureType = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        var vertexCount = checked((int)reader.ReadUInt32());

        if (shapeCount <= 0 || shapeCount > 64 || vertexCount <= 0 || vertexCount > 200000)
            throw new InvalidDataException("Unsupported PS2 icon geometry.");

        var shapes = Enumerable.Range(0, shapeCount)
            .Select(_ => new short[vertexCount * 3]).ToArray();
        var normalUv = new short[vertexCount * 5];
        var colors = new byte[vertexCount * 4];

        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            for (var shape = 0; shape < shapeCount; shape++)
            {
                var offset = vertex * 3;
                shapes[shape][offset] = reader.ReadInt16();
                shapes[shape][offset + 1] = reader.ReadInt16();
                shapes[shape][offset + 2] = reader.ReadInt16();
                _ = reader.ReadUInt16();
            }

            var nu = vertex * 5;
            normalUv[nu] = reader.ReadInt16();
            normalUv[nu + 1] = reader.ReadInt16();
            normalUv[nu + 2] = reader.ReadInt16();
            _ = reader.ReadUInt16();
            normalUv[nu + 3] = reader.ReadInt16();
            normalUv[nu + 4] = reader.ReadInt16();

            var c = vertex * 4;
            colors[c] = reader.ReadByte();
            colors[c + 1] = reader.ReadByte();
            colors[c + 2] = reader.ReadByte();
            colors[c + 3] = reader.ReadByte();
        }

        var frames = new List<AnimationFrame>();
        var frameLength = 0;
        var animationSpeed = 1.0f;
        var playOffset = 0;
        if (stream.Position + 20 <= stream.Length)
        {
            var animationTag = reader.ReadUInt32();
            frameLength = checked((int)reader.ReadUInt32());
            animationSpeed = reader.ReadSingle();
            playOffset = checked((int)reader.ReadUInt32());
            var frameCount = checked((int)reader.ReadUInt32());

            if (animationTag == 1 && frameCount >= 0 && frameCount < 1024)
            {
                for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    if (stream.Position + 16 > stream.Length) break;
                    var shapeId = checked((int)reader.ReadUInt32());
                    var keyCountStored = checked((int)reader.ReadUInt32());
                    _ = reader.ReadUInt32();
                    _ = reader.ReadUInt32();
                    var keyCount = Math.Max(0, keyCountStored - 1);
                    var keys = new List<AnimationKey>(keyCount);
                    for (var key = 0; key < keyCount && stream.Position + 8 <= stream.Length; key++)
                        keys.Add(new AnimationKey(reader.ReadSingle(), reader.ReadSingle()));
                    frames.Add(new AnimationFrame(shapeId, keys));
                }
            }
        }

        var texture = new ushort[128 * 128];
        Array.Fill(texture, (ushort)0xFFFF);

        if ((textureType & 0x04) != 0 && stream.Position < stream.Length)
        {
            byte[] textureBytes;
            var remaining = stream.Length - stream.Position;
            var compressedTexture = (textureType & 0x08) != 0;

            if (compressedTexture)
            {
                var textureStart = stream.Position;
                byte[]? decompressed = null;
                var decompressedLength = 0;

                // Most compressed icons store a 32-bit compressed-size field.
                // Some games, including Final Fantasy X, begin the RLE stream
                // immediately and pad it to the end of the icon file instead.
                if (remaining >= 4)
                {
                    var declaredSize = reader.ReadUInt32();
                    var availableAfterSize = stream.Length - stream.Position;

                    if (declaredSize > 0 &&
                        declaredSize <= availableAfterSize)
                    {
                        try
                        {
                            var compressed =
                                reader.ReadBytes((int)declaredSize);
                            decompressed =
                                DecompressTexture(
                                    compressed,
                                    out decompressedLength);
                        }
                        catch (InvalidDataException)
                        {
                            decompressed = null;
                        }
                    }
                }

                if (decompressed is null)
                {
                    stream.Position = textureStart;
                    var compressed =
                        reader.ReadBytes(
                            checked((int)(stream.Length - stream.Position)));

                    try
                    {
                        decompressed =
                            DecompressTexture(
                                compressed,
                                out decompressedLength);
                    }
                    catch (InvalidDataException)
                    {
                        // A small number of icons incorrectly advertise the
                        // compression flag while containing a normal raw texture.
                        stream.Position = textureStart;
                        if (stream.Length - stream.Position < 32768)
                            throw;

                        textureBytes = reader.ReadBytes(32768);
                        Buffer.BlockCopy(
                            textureBytes,
                            0,
                            texture,
                            0,
                            Math.Min(textureBytes.Length, 32768));
                        decompressed = null;
                    }
                }

                if (decompressed is not null)
                {
                    PlaceDecompressedTexture(
                        decompressed,
                        decompressedLength,
                        normalUv,
                        texture);
                }
            }
            else
            {
                if (remaining < 32768)
                    throw new InvalidDataException("Icon texture is incomplete.");

                textureBytes = reader.ReadBytes(32768);
                Buffer.BlockCopy(
                    textureBytes,
                    0,
                    texture,
                    0,
                    Math.Min(textureBytes.Length, 32768));
            }
        }

        return new Ps2IconModel
        {
            Shapes = shapes,
            NormalsUv = normalUv,
            VertexColors = colors,
            Texture = texture,
            VertexCount = vertexCount,
            FrameLength = frameLength,
            AnimationSpeed =
                float.IsFinite(animationSpeed) &&
                animationSpeed > 0
                    ? animationSpeed
                    : 1.0f,
            PlayOffset = playOffset,
            Frames = frames
        };
    }

    private static byte[] DecompressTexture(
        byte[] compressed,
        out int bytesWritten)
    {
        var output = new byte[32768];
        var source = 0;
        var target = 0;

        while (source + 1 < compressed.Length && target < output.Length)
        {
            var code = compressed[source] | (compressed[source + 1] << 8);
            source += 2;

            if ((code & 0x8000) != 0)
            {
                var byteCount = (0x10000 - code) * 2;
                if (source + byteCount > compressed.Length ||
                    target + byteCount > output.Length)
                {
                    throw new InvalidDataException(
                        "Invalid literal run in icon texture.");
                }

                Buffer.BlockCopy(
                    compressed,
                    source,
                    output,
                    target,
                    byteCount);
                source += byteCount;
                target += byteCount;
            }
            else
            {
                var repetitions = code;
                if (repetitions == 0)
                    continue;

                if (source + 2 > compressed.Length ||
                    target + repetitions * 2 > output.Length)
                {
                    throw new InvalidDataException(
                        "Invalid repeated run in icon texture.");
                }

                var lo = compressed[source++];
                var hi = compressed[source++];
                for (var index = 0; index < repetitions; index++)
                {
                    output[target++] = lo;
                    output[target++] = hi;
                }
            }
        }

        if (target == 0)
            throw new InvalidDataException("Compressed icon texture is empty.");

        bytesWritten = target;
        return output;
    }

    private static void PlaceDecompressedTexture(
        byte[] decompressed,
        int decompressedLength,
        short[] normalUv,
        ushort[] destination)
    {
        var destinationBytes = new byte[32768];
        var targetOffset = 0;

        // Some icons compress only the texture rows actually referenced by
        // their UV coordinates. FFX expands to 128x64 pixels and maps those
        // pixels to V=0.5..1.0, so the decoded rows belong in the lower half
        // of the full 128x128 texture rather than at row zero.
        if (decompressedLength > 0 &&
            decompressedLength < destinationBytes.Length &&
            normalUv.Length >= 5)
        {
            var minimumV = short.MaxValue;
            for (var index = 4; index < normalUv.Length; index += 5)
                minimumV = Math.Min(minimumV, normalUv[index]);

            var firstRow = Math.Clamp(
                (int)Math.Round(minimumV / 4096.0 * 128.0),
                0,
                127);
            var candidateOffset = firstRow * 128 * 2;

            if (candidateOffset + decompressedLength <=
                destinationBytes.Length)
            {
                targetOffset = candidateOffset;
            }
        }

        Buffer.BlockCopy(
            decompressed,
            0,
            destinationBytes,
            targetOffset,
            Math.Min(
                decompressedLength,
                destinationBytes.Length - targetOffset));

        Buffer.BlockCopy(
            destinationBytes,
            0,
            destination,
            0,
            destinationBytes.Length);
    }

    public BitmapSource Render(
        int width,
        int height,
        double elapsedSeconds,
        double rotationY)
    {
        try
        {
            var rendered =
                Ps2IconRendererV2.Render(
                    this,
                    width,
                    height,
                    elapsedSeconds,
                    rotationY);

            if (rendered is not null)
                return rendered;
        }
        catch
        {
            // The Golden Base renderer remains the safety net for
            // unusual or partially malformed icons.
        }

        return RenderLegacy(
            width,
            height,
            elapsedSeconds,
            rotationY);
    }

    internal BitmapSource RenderLegacy(
        int width,
        int height,
        double elapsedSeconds,
        double rotationY)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        var vertices = BuildAnimatedVertices(elapsedSeconds);
        var projected = new ProjectedVertex[VertexCount];

        var sinY = Math.Sin(rotationY);
        var cosY = Math.Cos(rotationY);
        const double rotationX = -0.18;
        var sinX = Math.Sin(rotationX);
        var cosX = Math.Cos(rotationX);

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;

        for (var index = 0; index < VertexCount; index++)
        {
            var p = index * 3;
            var x = vertices[p] / 4096.0;
            var y = -vertices[p + 1] / 4096.0;
            var z = -vertices[p + 2] / 4096.0;

            var rx = x * cosY + z * sinY;
            var rz = -x * sinY + z * cosY;
            var ry = y * cosX - rz * sinX;
            rz = y * sinX + rz * cosX;

            projected[index] = new ProjectedVertex(rx, ry, rz, 0, 0);
            minX = Math.Min(minX, rx);
            maxX = Math.Max(maxX, rx);
            minY = Math.Min(minY, ry);
            maxY = Math.Max(maxY, ry);
        }

        var spanX = Math.Max(0.001, maxX - minX);
        var spanY = Math.Max(0.001, maxY - minY);
        var scale = 0.78 * Math.Min(width / spanX, height / spanY);
        var centerX = (minX + maxX) * 0.5;
        var centerY = (minY + maxY) * 0.5;

        for (var index = 0; index < VertexCount; index++)
        {
            var source = projected[index];
            var perspective = 1.0 / Math.Max(0.4, 1.0 + source.Z * 0.08);
            var sx = width * 0.5 - (source.X - centerX) * scale * perspective;
            var sy = height * 0.52 - (source.Y - centerY) * scale * perspective;
            projected[index] = source with { ScreenX = sx, ScreenY = sy };
        }

        var pixels = new byte[width * height * 4];
        var zBuffer = new double[width * height];
        Array.Fill(zBuffer, double.PositiveInfinity);

        for (var triangle = 0; triangle + 2 < VertexCount; triangle += 3)
            RasterTriangle(triangle, triangle + 1, triangle + 2, projected, pixels, zBuffer, width, height);

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private short[] BuildAnimatedVertices(double elapsedSeconds)
    {
        if (Shapes.Length == 1 || Frames.Count == 0 || FrameLength <= 0)
            return Shapes[0];

        var animationTime = (elapsedSeconds * 12.0) % FrameLength;
        var weights = new Dictionary<int, double>();

        foreach (var frame in Frames)
        {
            if (frame.ShapeId < 0 || frame.ShapeId >= Shapes.Length) continue;
            var keys = frame.Keys.ToList();
            if (frame.ShapeId == 0) keys.Add(new AnimationKey(0, 1));
            if (keys.Count == 0) continue;

            AnimationKey? previous = null;
            AnimationKey? next = null;
            double previousTime = 0, nextTime = 0;

            foreach (var key in keys)
            {
                var before = key.Time <= animationTime ? key.Time : key.Time - FrameLength;
                if (previous is null || before > previousTime)
                {
                    previous = key;
                    previousTime = before;
                }

                var after = key.Time >= animationTime ? key.Time : key.Time + FrameLength;
                if (next is null || after < nextTime)
                {
                    next = key;
                    nextTime = after;
                }
            }

            if (previous is null || next is null) continue;
            var progress = nextTime > previousTime
                ? (animationTime - previousTime) / (nextTime - previousTime)
                : 0;
            weights[frame.ShapeId] = (1 - progress) * previous.Value + progress * next.Value;
        }

        if (weights.Count == 0 || weights.Values.Sum() <= 0)
            weights = new Dictionary<int, double> { [0] = 1 };

        var total = weights.Values.Sum();
        var output = new short[VertexCount * 3];
        for (var index = 0; index < output.Length; index++)
        {
            double value = 0;
            foreach (var pair in weights)
                value += (pair.Value / total) * Shapes[pair.Key][index];
            output[index] = (short)Math.Clamp((int)Math.Round(value), short.MinValue, short.MaxValue);
        }
        return output;
    }

    private void RasterTriangle(
        int ia, int ib, int ic, ProjectedVertex[] projected,
        byte[] pixels, double[] zBuffer, int width, int height)
    {
        var a = projected[ia];
        var b = projected[ib];
        var c = projected[ic];

        var denominator = (b.ScreenY - c.ScreenY) * (a.ScreenX - c.ScreenX) +
                          (c.ScreenX - b.ScreenX) * (a.ScreenY - c.ScreenY);
        if (Math.Abs(denominator) < 0.00001) return;

        var minX = Math.Max(0, (int)Math.Floor(Math.Min(a.ScreenX, Math.Min(b.ScreenX, c.ScreenX))));
        var maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(a.ScreenX, Math.Max(b.ScreenX, c.ScreenX))));
        var minY = Math.Max(0, (int)Math.Floor(Math.Min(a.ScreenY, Math.Min(b.ScreenY, c.ScreenY))));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(a.ScreenY, Math.Max(b.ScreenY, c.ScreenY))));
        if (minX > maxX || minY > maxY) return;

        var pa = GetModelVertex(ia);
        var pb = GetModelVertex(ib);
        var pc = GetModelVertex(ic);
        var ux = pb.Y * pc.Z - pb.Z * pc.Y;
        var uy = pb.Z * pc.X - pb.X * pc.Z;
        var uz = pb.X * pc.Y - pb.Y * pc.X;
        var length = Math.Sqrt(ux * ux + uy * uy + uz * uz);
        var light = length > 0.001 ? 0.42 + 0.58 * Math.Max(0, -uz / length) : 0.75;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5;
                var py = y + 0.5;
                var w1 = ((b.ScreenY - c.ScreenY) * (px - c.ScreenX) +
                          (c.ScreenX - b.ScreenX) * (py - c.ScreenY)) / denominator;
                var w2 = ((c.ScreenY - a.ScreenY) * (px - c.ScreenX) +
                          (a.ScreenX - c.ScreenX) * (py - c.ScreenY)) / denominator;
                var w3 = 1.0 - w1 - w2;
                if (w1 < -0.001 || w2 < -0.001 || w3 < -0.001) continue;

                var depth = w1 * a.Z + w2 * b.Z + w3 * c.Z;
                var pixelIndex = y * width + x;
                if (depth >= zBuffer[pixelIndex]) continue;
                zBuffer[pixelIndex] = depth;

                var uvA = GetUv(ia);
                var uvB = GetUv(ib);
                var uvC = GetUv(ic);
                var u = w1 * uvA.U + w2 * uvB.U + w3 * uvC.U;
                var v = w1 * uvA.V + w2 * uvB.V + w3 * uvC.V;
                var tx = Math.Clamp((int)Math.Round(u * 127), 0, 127);
                var ty = Math.Clamp((int)Math.Round(v * 127), 0, 127);
                var texel = Texture[ty * 128 + tx];

                var tr = (texel & 0x1F) * 255 / 31;
                var tg = ((texel >> 5) & 0x1F) * 255 / 31;
                var tb = ((texel >> 10) & 0x1F) * 255 / 31;

                var colorA = GetColor(ia);
                var colorB = GetColor(ib);
                var colorC = GetColor(ic);
                var vr = Math.Clamp((w1 * colorA.R + w2 * colorB.R + w3 * colorC.R) / 128.0, 0.15, 1.5);
                var vg = Math.Clamp((w1 * colorA.G + w2 * colorB.G + w3 * colorC.G) / 128.0, 0.15, 1.5);
                var vb = Math.Clamp((w1 * colorA.B + w2 * colorB.B + w3 * colorC.B) / 128.0, 0.15, 1.5);
                var alpha = Math.Clamp((w1 * colorA.A + w2 * colorB.A + w3 * colorC.A) / 128.0, 0.25, 1.0);

                var offset = pixelIndex * 4;
                pixels[offset] = (byte)Math.Clamp(tb * vb * light, 0, 255);
                pixels[offset + 1] = (byte)Math.Clamp(tg * vg * light, 0, 255);
                pixels[offset + 2] = (byte)Math.Clamp(tr * vr * light, 0, 255);
                pixels[offset + 3] = (byte)(alpha * 255);
            }
        }
    }

    private (double X, double Y, double Z) GetModelVertex(int index)
    {
        var p = index * 3;
        return (Shapes[0][p] / 4096.0, -Shapes[0][p + 1] / 4096.0, -Shapes[0][p + 2] / 4096.0);
    }

    private (double U, double V) GetUv(int index)
    {
        var offset = index * 5;
        return (NormalsUv[offset + 3] / 4096.0, NormalsUv[offset + 4] / 4096.0);
    }

    private (byte R, byte G, byte B, byte A) GetColor(int index)
    {
        var offset = index * 4;
        return (VertexColors[offset], VertexColors[offset + 1], VertexColors[offset + 2],
            VertexColors[offset + 3] == 0 ? (byte)128 : VertexColors[offset + 3]);
    }

    private readonly record struct ProjectedVertex(
        double X, double Y, double Z, double ScreenX, double ScreenY);
}
