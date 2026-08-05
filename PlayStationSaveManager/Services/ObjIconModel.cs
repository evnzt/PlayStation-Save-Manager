using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlayStationSaveManager.Services;

public sealed class ObjIconModel
{
    private readonly record struct Vec2(double X, double Y);
    private readonly record struct Vec3(double X, double Y, double Z);
    private readonly record struct Vertex(Vec3 Position, Vec2 Uv);
    private readonly record struct Triangle(Vertex A, Vertex B, Vertex C);
    private readonly record struct Projected(double X, double Y, double Z, double U, double V);

    private readonly Triangle[] _triangles;
    private readonly byte[] _texture;
    private readonly int _textureWidth;
    private readonly int _textureHeight;

    private ObjIconModel(Triangle[] triangles, byte[] texture, int textureWidth, int textureHeight)
    {
        _triangles = triangles;
        _texture = texture;
        _textureWidth = textureWidth;
        _textureHeight = textureHeight;
    }

    public static ObjIconModel Load(string objPath, string texturePath)
    {
        var positions = new List<Vec3>();
        var uvs = new List<Vec2>();
        var triangles = new List<Triangle>();

        foreach (var raw in File.ReadLines(objPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts[0] == "v" && parts.Length >= 4)
            {
                positions.Add(new Vec3(Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
            }
            else if (parts[0] == "vt" && parts.Length >= 3)
            {
                uvs.Add(new Vec2(Parse(parts[1]), 1.0 - Parse(parts[2])));
            }
            else if (parts[0] == "f" && parts.Length >= 4)
            {
                var vertices = parts.Skip(1).Select(token =>
                {
                    var indexes = token.Split('/');
                    var positionIndex = ParseIndex(indexes[0], positions.Count);
                    var uvIndex = indexes.Length > 1 && indexes[1].Length > 0
                        ? ParseIndex(indexes[1], uvs.Count)
                        : -1;
                    return new Vertex(
                        positions[positionIndex],
                        uvIndex >= 0 ? uvs[uvIndex] : new Vec2(0, 0));
                }).ToArray();

                for (var index = 1; index + 1 < vertices.Length; index++)
                    triangles.Add(new Triangle(vertices[0], vertices[index], vertices[index + 1]));
            }
        }

        if (triangles.Count == 0)
            throw new InvalidDataException("OBJ file contains no triangles.");

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(texturePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var texture = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(texture, converted.PixelWidth * 4, 0);

        return new ObjIconModel(
            triangles.ToArray(), texture, converted.PixelWidth, converted.PixelHeight);
    }

    private static double Parse(string value) =>
        double.Parse(value, CultureInfo.InvariantCulture);

    private static int ParseIndex(string value, int count)
    {
        var parsed = int.Parse(value, CultureInfo.InvariantCulture);
        return parsed > 0 ? parsed - 1 : count + parsed;
    }

    public BitmapSource Render(int width, int height, double rotationY)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);

        var all = _triangles.SelectMany(t => new[] { t.A.Position, t.B.Position, t.C.Position }).ToArray();
        var center = new Vec3(
            all.Average(v => v.X),
            all.Average(v => v.Y),
            all.Average(v => v.Z));

        var sinY = Math.Sin(rotationY);
        var cosY = Math.Cos(rotationY);
        const double rotationX = -0.22;
        var sinX = Math.Sin(rotationX);
        var cosX = Math.Cos(rotationX);

        Projected Transform(Vertex vertex)
        {
            var x = vertex.Position.X - center.X;
            var y = vertex.Position.Y - center.Y;
            var z = vertex.Position.Z - center.Z;

            var rx = x * cosY + z * sinY;
            var rz = -x * sinY + z * cosY;
            var ry = y * cosX - rz * sinX;
            rz = y * sinX + rz * cosX;

            return new Projected(rx, -ry, rz, vertex.Uv.X, vertex.Uv.Y);
        }

        var transformed = _triangles
            .Select(t => (A: Transform(t.A), B: Transform(t.B), C: Transform(t.C)))
            .ToArray();

        var points = transformed.SelectMany(t => new[] { t.A, t.B, t.C }).ToArray();
        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        var scale = 0.78 * Math.Min(
            width / Math.Max(0.001, maxX - minX),
            height / Math.Max(0.001, maxY - minY));
        var centerX = (minX + maxX) * 0.5;
        var centerY = (minY + maxY) * 0.5;

        Projected Project(Projected point)
        {
            var perspective = 1.0 / Math.Max(0.45, 1.0 + point.Z * 0.08);
            return point with
            {
                X = width * 0.5 - (point.X - centerX) * scale * perspective,
                Y = height * 0.52 - (point.Y - centerY) * scale * perspective
            };
        }

        var projected = transformed
            .Select(t => (A: Project(t.A), B: Project(t.B), C: Project(t.C)))
            .ToArray();

        var pixels = new byte[width * height * 4];
        var zBuffer = new double[width * height];
        Array.Fill(zBuffer, double.PositiveInfinity);

        foreach (var triangle in projected)
            Rasterize(triangle.A, triangle.B, triangle.C, pixels, zBuffer, width, height);

        var output = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        output.Freeze();
        return output;
    }

    private void Rasterize(
        Projected a, Projected b, Projected c,
        byte[] pixels, double[] zBuffer, int width, int height)
    {
        var denominator = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (Math.Abs(denominator) < 0.00001) return;

        var minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
        var maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        var minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));

        var faceNx = (b.Y - a.Y) * (c.Z - a.Z) - (b.Z - a.Z) * (c.Y - a.Y);
        var faceNy = (b.Z - a.Z) * (c.X - a.X) - (b.X - a.X) * (c.Z - a.Z);
        var faceNz = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        var normalLength = Math.Sqrt(faceNx * faceNx + faceNy * faceNy + faceNz * faceNz);
        var lighting = normalLength > 0.001
            ? 0.42 + 0.58 * Math.Max(0, -faceNz / normalLength)
            : 0.8;

        var uvDegenerate =
            Math.Abs(a.U - b.U) < 0.0001 && Math.Abs(a.V - b.V) < 0.0001 &&
            Math.Abs(a.U - c.U) < 0.0001 && Math.Abs(a.V - c.V) < 0.0001;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5;
                var py = y + 0.5;
                var w1 = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / denominator;
                var w2 = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / denominator;
                var w3 = 1 - w1 - w2;
                if (w1 < -0.001 || w2 < -0.001 || w3 < -0.001) continue;

                var depth = w1 * a.Z + w2 * b.Z + w3 * c.Z;
                var index = y * width + x;
                if (depth >= zBuffer[index]) continue;
                zBuffer[index] = depth;

                double u;
                double v;
                if (uvDegenerate)
                {
                    u = (x - minX) / (double)Math.Max(1, maxX - minX);
                    v = (y - minY) / (double)Math.Max(1, maxY - minY);
                }
                else
                {
                    u = w1 * a.U + w2 * b.U + w3 * c.U;
                    v = w1 * a.V + w2 * b.V + w3 * c.V;
                }

                var tx = Math.Clamp((int)Math.Round(u * (_textureWidth - 1)), 0, _textureWidth - 1);
                var ty = Math.Clamp((int)Math.Round(v * (_textureHeight - 1)), 0, _textureHeight - 1);
                var textureOffset = (ty * _textureWidth + tx) * 4;

                var outputOffset = index * 4;
                pixels[outputOffset] = (byte)Math.Clamp(_texture[textureOffset] * lighting, 0, 255);
                pixels[outputOffset + 1] = (byte)Math.Clamp(_texture[textureOffset + 1] * lighting, 0, 255);
                pixels[outputOffset + 2] = (byte)Math.Clamp(_texture[textureOffset + 2] * lighting, 0, 255);
                pixels[outputOffset + 3] = _texture[textureOffset + 3];
            }
        }
    }
}
