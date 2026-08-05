using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlayStationSaveManager.Services;

internal static class Ps2IconRendererV2
{
    private readonly record struct Vertex(
        double X,
        double Y,
        double Z,
        double NormalX,
        double NormalY,
        double NormalZ,
        double U,
        double V,
        double R,
        double G,
        double B,
        double A);

    private readonly record struct Projected(
        double X,
        double Y,
        double Depth,
        double ReciprocalW,
        Vertex Source);

    public static BitmapSource? Render(
        Ps2IconModel model,
        int width,
        int height,
        double elapsedSeconds,
        double rotationY)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);

        if (model.VertexCount < 3 ||
            model.Shapes.Length == 0)
        {
            return null;
        }

        var animated = BuildAnimatedVertices(
            model,
            elapsedSeconds);

        var thinCardDepthBias =
            GetThinCardDepthBias(animated);

        var vertices = BuildVertices(
            model,
            animated,
            rotationY);

        var projected = Project(
            vertices,
            width,
            height);

        var useMasked =
            GetStableTextureMaskDecision(
                model,
                width,
                height);

        var selected = Rasterize(
            model,
            projected,
            width,
            height,
            useTextureMask: useMasked,
            useNeutralLighting: false,
            thinCardDepthBias: thinCardDepthBias);

        ApplyMildGamma(selected);

        if (CountVisible(selected) <
            Math.Max(8, width * height / 300))
        {
            return null;
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            selected,
            width * 4);

        bitmap.Freeze();
        return bitmap;
    }

    private static bool GetStableTextureMaskDecision(
        Ps2IconModel model,
        int width,
        int height)
    {
        if (model.V2UseTextureMask.HasValue)
            return model.V2UseTextureMask.Value;

        lock (model.V2DecisionSync)
        {
            if (model.V2UseTextureMask.HasValue)
                return model.V2UseTextureMask.Value;

            // Use a fixed frame and angle so the decision cannot change
            // as the icon animates or completes a 360-degree rotation.
            var canonicalVertices =
                BuildVertices(
                    model,
                    model.Shapes[0]
                        .Select(value => (double)value)
                        .ToArray(),
                    rotationY: 0.0);

            var canonicalProjected =
                Project(
                    canonicalVertices,
                    Math.Max(96, width),
                    Math.Max(96, height));

            var standard =
                Rasterize(
                    model,
                    canonicalProjected,
                    Math.Max(96, width),
                    Math.Max(96, height),
                    useTextureMask: false,
                    useNeutralLighting: false,
                    thinCardDepthBias: 0.0);

            var masked =
                Rasterize(
                    model,
                    canonicalProjected,
                    Math.Max(96, width),
                    Math.Max(96, height),
                    useTextureMask: true,
                    useNeutralLighting: false,
                    thinCardDepthBias: 0.0);

            model.V2UseTextureMask =
                ShouldUseMaskedResult(
                    standard,
                    masked,
                    Math.Max(96, width),
                    Math.Max(96, height));

            return model.V2UseTextureMask.Value;
        }
    }

    private static double GetThinCardDepthBias(
        double[] positions)
    {
        if (positions.Length < 9)
            return 0.0;

        double minX = double.MaxValue;
        double maxX = double.MinValue;
        double minY = double.MaxValue;
        double maxY = double.MinValue;
        double minZ = double.MaxValue;
        double maxZ = double.MinValue;

        for (var offset = 0;
             offset + 2 < positions.Length;
             offset += 3)
        {
            var x = positions[offset] / 4096.0;
            var y = positions[offset + 1] / 4096.0;
            var z = positions[offset + 2] / 4096.0;

            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z);
            maxZ = Math.Max(maxZ, z);
        }

        var spanX = Math.Max(0.0001, maxX - minX);
        var spanY = Math.Max(0.0001, maxY - minY);
        var spanZ = Math.Max(0.0, maxZ - minZ);
        var faceSpan = Math.Max(spanX, spanY);

        if (spanZ > faceSpan * 0.10)
            return 0.0;

        return Math.Max(
            faceSpan * 0.004,
            0.0005);
    }

    private static double[] BuildAnimatedVertices(
        Ps2IconModel model,
        double elapsedSeconds)
    {
        var shapeCount = model.Shapes.Length;
        var count = model.VertexCount * 3;

        if (shapeCount == 1 ||
            model.FrameLength <= 0)
        {
            return model.Shapes[0]
                .Select(value => (double)value)
                .ToArray();
        }

        var frame =
            (elapsedSeconds *
             60.0 *
             model.AnimationSpeed +
             model.PlayOffset) %
            model.FrameLength;

        if (frame < 0)
            frame += model.FrameLength;

        var framesPerShape =
            model.FrameLength /
            (double)shapeCount;

        if (framesPerShape <= 0)
            framesPerShape = 1;

        var shapePosition =
            frame /
            framesPerShape;

        var current =
            ((int)Math.Floor(shapePosition)) %
            shapeCount;
        var next =
            (current + 1) %
            shapeCount;
        var tween =
            shapePosition -
            Math.Floor(shapePosition);

        // Smoothstep removes the mechanical snapping visible with
        // simple linear timing while preserving the actual shapes.
        tween =
            tween * tween *
            (3.0 - 2.0 * tween);

        var output = new double[count];
        var a = model.Shapes[current];
        var b = model.Shapes[next];

        for (var index = 0; index < count; index++)
        {
            output[index] =
                a[index] +
                (b[index] - a[index]) *
                tween;
        }

        return output;
    }

    private static Vertex[] BuildVertices(
        Ps2IconModel model,
        double[] positions,
        double rotationY)
    {
        var output =
            new Vertex[model.VertexCount];

        var sinY = Math.Sin(rotationY);
        var cosY = Math.Cos(rotationY);
        const double rotationX = -0.18;
        var sinX = Math.Sin(rotationX);
        var cosX = Math.Cos(rotationX);

        for (var index = 0;
             index < model.VertexCount;
             index++)
        {
            var p = index * 3;
            var n = index * 5;
            var c = index * 4;

            var x = positions[p] / 4096.0;
            var y = -positions[p + 1] / 4096.0;
            var z = -positions[p + 2] / 4096.0;

            Rotate(
                x,
                y,
                z,
                sinY,
                cosY,
                sinX,
                cosX,
                out var rx,
                out var ry,
                out var rz);

            var nx =
                model.NormalsUv[n] /
                4096.0;
            var ny =
                -model.NormalsUv[n + 1] /
                4096.0;
            var nz =
                -model.NormalsUv[n + 2] /
                4096.0;

            Rotate(
                nx,
                ny,
                nz,
                sinY,
                cosY,
                sinX,
                cosX,
                out var rnx,
                out var rny,
                out var rnz);

            Normalize(
                ref rnx,
                ref rny,
                ref rnz);

            // PS2 save icons commonly store material/GS values in
            // the fourth color byte rather than conventional desktop
            // alpha. Treat model surfaces as opaque; cutout transparency
            // is handled separately by the texture-mask render.
            const double alpha = 1.0;

            output[index] = new Vertex(
                rx,
                ry,
                rz,
                rnx,
                rny,
                rnz,
                model.NormalsUv[n + 3] /
                    4096.0,
                model.NormalsUv[n + 4] /
                    4096.0,
                Math.Clamp(
                    model.VertexColors[c] /
                    128.0,
                    0.0,
                    2.0),
                Math.Clamp(
                    model.VertexColors[c + 1] /
                    128.0,
                    0.0,
                    2.0),
                Math.Clamp(
                    model.VertexColors[c + 2] /
                    128.0,
                    0.0,
                    2.0),
                alpha);
        }

        return output;
    }

    private static void Rotate(
        double x,
        double y,
        double z,
        double sinY,
        double cosY,
        double sinX,
        double cosX,
        out double rx,
        out double ry,
        out double rz)
    {
        rx = x * cosY + z * sinY;
        var z1 = -x * sinY + z * cosY;
        ry = y * cosX - z1 * sinX;
        rz = y * sinX + z1 * cosX;
    }

    private static Projected[] Project(
        Vertex[] vertices,
        int width,
        int height)
    {
        var minX = vertices.Min(v => v.X);
        var maxX = vertices.Max(v => v.X);
        var minY = vertices.Min(v => v.Y);
        var maxY = vertices.Max(v => v.Y);

        var spanX =
            Math.Max(0.001, maxX - minX);
        var spanY =
            Math.Max(0.001, maxY - minY);
        var scale =
            0.78 *
            Math.Min(
                width / spanX,
                height / spanY);

        var centerX = (minX + maxX) * 0.5;
        var centerY = (minY + maxY) * 0.5;
        var output =
            new Projected[vertices.Length];

        for (var index = 0;
             index < vertices.Length;
             index++)
        {
            var vertex = vertices[index];
            var w =
                Math.Max(
                    0.35,
                    1.0 +
                    vertex.Z * 0.08);
            var reciprocalW = 1.0 / w;

            output[index] = new Projected(
                width * 0.5 -
                (vertex.X - centerX) *
                scale *
                reciprocalW,
                height * 0.52 -
                (vertex.Y - centerY) *
                scale *
                reciprocalW,
                vertex.Z,
                reciprocalW,
                vertex);
        }

        return output;
    }

    private static byte[] Rasterize(
        Ps2IconModel model,
        Projected[] projected,
        int width,
        int height,
        bool useTextureMask,
        bool useNeutralLighting,
        double thinCardDepthBias)
    {
        var pixels =
            new byte[width * height * 4];
        var zBuffer =
            new double[width * height];
        Array.Fill(
            zBuffer,
            double.PositiveInfinity);

        for (var triangle = 0;
             triangle + 2 < model.VertexCount;
             triangle += 3)
        {
            RasterTriangle(
                model,
                projected[triangle],
                projected[triangle + 1],
                projected[triangle + 2],
                pixels,
                zBuffer,
                width,
                height,
                useTextureMask,
                useNeutralLighting,
                thinCardDepthBias);
        }

        return pixels;
    }

    private static void RasterTriangle(
        Ps2IconModel model,
        Projected a,
        Projected b,
        Projected c,
        byte[] pixels,
        double[] zBuffer,
        int width,
        int height,
        bool useTextureMask,
        bool useNeutralLighting,
        double thinCardDepthBias)
    {
        var averageNormalZ =
            (a.Source.NormalZ +
             b.Source.NormalZ +
             c.Source.NormalZ) /
            3.0;

        if (thinCardDepthBias > 0.0 &&
            averageNormalZ >= -0.015)
        {
            return;
        }

        var denominator =
            (b.Y - c.Y) *
            (a.X - c.X) +
            (c.X - b.X) *
            (a.Y - c.Y);

        if (Math.Abs(denominator) <
            0.00001)
        {
            return;
        }

        var minX = Math.Max(
            0,
            (int)Math.Floor(
                Math.Min(
                    a.X,
                    Math.Min(b.X, c.X))));
        var maxX = Math.Min(
            width - 1,
            (int)Math.Ceiling(
                Math.Max(
                    a.X,
                    Math.Max(b.X, c.X))));
        var minY = Math.Max(
            0,
            (int)Math.Floor(
                Math.Min(
                    a.Y,
                    Math.Min(b.Y, c.Y))));
        var maxY = Math.Min(
            height - 1,
            (int)Math.Ceiling(
                Math.Max(
                    a.Y,
                    Math.Max(b.Y, c.Y))));

        for (var y = minY;
             y <= maxY;
             y++)
        {
            for (var x = minX;
                 x <= maxX;
                 x++)
            {
                var px = x + 0.5;
                var py = y + 0.5;

                var w1 =
                    ((b.Y - c.Y) *
                     (px - c.X) +
                     (c.X - b.X) *
                     (py - c.Y)) /
                    denominator;
                var w2 =
                    ((c.Y - a.Y) *
                     (px - c.X) +
                     (a.X - c.X) *
                     (py - c.Y)) /
                    denominator;
                var w3 = 1.0 - w1 - w2;

                if (w1 < -0.001 ||
                    w2 < -0.001 ||
                    w3 < -0.001)
                {
                    continue;
                }

                var reciprocalW =
                    w1 * a.ReciprocalW +
                    w2 * b.ReciprocalW +
                    w3 * c.ReciprocalW;

                if (reciprocalW <= 0.000001)
                    continue;

                var depth =
                    (w1 * a.Depth *
                         a.ReciprocalW +
                     w2 * b.Depth *
                         b.ReciprocalW +
                     w3 * c.Depth *
                         c.ReciprocalW) /
                    reciprocalW;

                if (thinCardDepthBias > 0.0)
                {
                    depth -=
                        thinCardDepthBias *
                        Math.Max(
                            0.0,
                            -averageNormalZ);
                }

                var pixelIndex =
                    y * width + x;

                if (depth >=
                    zBuffer[pixelIndex])
                {
                    continue;
                }

                var u =
                    (w1 * a.Source.U *
                         a.ReciprocalW +
                     w2 * b.Source.U *
                         b.ReciprocalW +
                     w3 * c.Source.U *
                         c.ReciprocalW) /
                    reciprocalW;
                var v =
                    (w1 * a.Source.V *
                         a.ReciprocalW +
                     w2 * b.Source.V *
                         b.ReciprocalW +
                     w3 * c.Source.V *
                         c.ReciprocalW) /
                    reciprocalW;

                var texel =
                    SampleTexture(
                        model.Texture,
                        u,
                        v,
                        useTextureMask);

                if (texel.A <= 0.001)
                    continue;

                var nx =
                    (w1 * a.Source.NormalX *
                         a.ReciprocalW +
                     w2 * b.Source.NormalX *
                         b.ReciprocalW +
                     w3 * c.Source.NormalX *
                         c.ReciprocalW) /
                    reciprocalW;
                var ny =
                    (w1 * a.Source.NormalY *
                         a.ReciprocalW +
                     w2 * b.Source.NormalY *
                         b.ReciprocalW +
                     w3 * c.Source.NormalY *
                         c.ReciprocalW) /
                    reciprocalW;
                var nz =
                    (w1 * a.Source.NormalZ *
                         a.ReciprocalW +
                     w2 * b.Source.NormalZ *
                         b.ReciprocalW +
                     w3 * c.Source.NormalZ *
                         c.ReciprocalW) /
                    reciprocalW;
                Normalize(
                    ref nx,
                    ref ny,
                    ref nz);

                var r =
                    Interpolate(
                        a.Source.R,
                        b.Source.R,
                        c.Source.R,
                        a.ReciprocalW,
                        b.ReciprocalW,
                        c.ReciprocalW,
                        w1,
                        w2,
                        w3,
                        reciprocalW);
                var g =
                    Interpolate(
                        a.Source.G,
                        b.Source.G,
                        c.Source.G,
                        a.ReciprocalW,
                        b.ReciprocalW,
                        c.ReciprocalW,
                        w1,
                        w2,
                        w3,
                        reciprocalW);
                var blue =
                    Interpolate(
                        a.Source.B,
                        b.Source.B,
                        c.Source.B,
                        a.ReciprocalW,
                        b.ReciprocalW,
                        c.ReciprocalW,
                        w1,
                        w2,
                        w3,
                        reciprocalW);
                var alpha = texel.A;

                if (alpha <= 0.001)
                    continue;

                var lighting =
                    CalculateLighting(
                        model.RenderSettings,
                        nx,
                        ny,
                        nz,
                        useNeutralLighting);

                var sourceB =
                    Math.Clamp(
                        texel.B *
                        blue *
                        lighting.B,
                        0.0,
                        255.0);
                var sourceG =
                    Math.Clamp(
                        texel.G *
                        g *
                        lighting.G,
                        0.0,
                        255.0);
                var sourceR =
                    Math.Clamp(
                        texel.R *
                        r *
                        lighting.R,
                        0.0,
                        255.0);

                Blend(
                    pixels,
                    pixelIndex,
                    sourceB,
                    sourceG,
                    sourceR,
                    alpha);

                if (alpha >= 0.995)
                    zBuffer[pixelIndex] = depth;
            }
        }
    }

    private static (
        double B,
        double G,
        double R,
        double A) SampleTexture(
        ushort[] texture,
        double u,
        double v,
        bool useMask)
    {
        u = Math.Clamp(u, 0.0, 1.0);
        v = Math.Clamp(v, 0.0, 1.0);

        var fx = u * 127.0;
        var fy = v * 127.0;
        var x0 = Math.Clamp(
            (int)Math.Floor(fx),
            0,
            127);
        var y0 = Math.Clamp(
            (int)Math.Floor(fy),
            0,
            127);
        var x1 = Math.Min(127, x0 + 1);
        var y1 = Math.Min(127, y0 + 1);
        var tx = fx - x0;
        var ty = fy - y0;

        var p00 = Decode(
            texture[y0 * 128 + x0],
            useMask);
        var p10 = Decode(
            texture[y0 * 128 + x1],
            useMask);
        var p01 = Decode(
            texture[y1 * 128 + x0],
            useMask);
        var p11 = Decode(
            texture[y1 * 128 + x1],
            useMask);

        return (
            Bilinear(
                p00.B,
                p10.B,
                p01.B,
                p11.B,
                tx,
                ty),
            Bilinear(
                p00.G,
                p10.G,
                p01.G,
                p11.G,
                tx,
                ty),
            Bilinear(
                p00.R,
                p10.R,
                p01.R,
                p11.R,
                tx,
                ty),
            Bilinear(
                p00.A,
                p10.A,
                p01.A,
                p11.A,
                tx,
                ty));
    }

    private static (
        double B,
        double G,
        double R,
        double A) Decode(
        ushort value,
        bool useMask)
    {
        var r =
            (value & 0x1F) *
            255.0 /
            31.0;
        var g =
            ((value >> 5) & 0x1F) *
            255.0 /
            31.0;
        var b =
            ((value >> 10) & 0x1F) *
            255.0 /
            31.0;
        var alpha =
            !useMask ||
            (value & 0x8000) != 0
                ? 1.0
                : 0.0;

        return (b, g, r, alpha);
    }

    private static (
        double R,
        double G,
        double B) CalculateLighting(
        Ps2IconRenderSettings settings,
        double nx,
        double ny,
        double nz,
        bool useNeutralLighting)
    {
        if (useNeutralLighting)
        {
            var facing =
                Math.Max(
                    0.0,
                    -nz);

            var neutral =
                0.72 +
                0.28 * facing;

            return (
                neutral,
                neutral,
                neutral);
        }

        var r = settings.AmbientR;
        var g = settings.AmbientG;
        var b = settings.AmbientB;

        foreach (var light in settings.Lights)
        {
            var lx = light.X;
            var ly = light.Y;
            var lz = light.Z;
            Normalize(
                ref lx,
                ref ly,
                ref lz);

            var diffuse =
                Math.Max(
                    0.0,
                    nx * lx +
                    ny * ly +
                    nz * lz);

            r += (float)(diffuse * light.R);
            g += (float)(diffuse * light.G);
            b += (float)(diffuse * light.B);
        }

        return (
            Math.Clamp(r, 0.32, 2.0),
            Math.Clamp(g, 0.32, 2.0),
            Math.Clamp(b, 0.32, 2.0));
    }

    private static void ApplyMildGamma(
        byte[] pixels)
    {
        const double inverseGamma =
            1.0 / 1.12;

        for (var offset = 0;
             offset + 3 < pixels.Length;
             offset += 4)
        {
            if (pixels[offset + 3] == 0)
                continue;

            pixels[offset] =
                GammaCorrect(
                    pixels[offset],
                    inverseGamma);
            pixels[offset + 1] =
                GammaCorrect(
                    pixels[offset + 1],
                    inverseGamma);
            pixels[offset + 2] =
                GammaCorrect(
                    pixels[offset + 2],
                    inverseGamma);
        }
    }

    private static byte GammaCorrect(
        byte value,
        double inverseGamma)
    {
        var normalized =
            value / 255.0;

        return ToByte(
            Math.Pow(
                normalized,
                inverseGamma) *
            255.0);
    }

    private static bool ShouldUseMaskedResult(
        byte[] standard,
        byte[] masked,
        int width,
        int height)
    {
        var standardVisible =
            CountVisible(standard);
        var maskedVisible =
            CountVisible(masked);

        if (standardVisible == 0 ||
            maskedVisible <
            standardVisible * 0.42)
        {
            return false;
        }

        var standardBorder =
            CountBorderVisible(
                standard,
                width,
                height);
        var maskedBorder =
            CountBorderVisible(
                masked,
                width,
                height);

        var borderImprovement =
            standardBorder -
            maskedBorder;

        var substantialBorderCleanup =
            standardBorder >
                Math.Max(
                    10,
                    standardVisible * 0.08) &&
            borderImprovement >
                standardBorder * 0.35;

        var bodyPreserved =
            maskedVisible >
            standardVisible * 0.55;

        return substantialBorderCleanup &&
               bodyPreserved;
    }

    private static int CountVisible(
        byte[] pixels)
    {
        var count = 0;

        for (var offset = 3;
             offset < pixels.Length;
             offset += 4)
        {
            if (pixels[offset] > 8)
                count++;
        }

        return count;
    }

    private static int CountBorderVisible(
        byte[] pixels,
        int width,
        int height)
    {
        var borderX =
            Math.Max(1, width / 10);
        var borderY =
            Math.Max(1, height / 10);
        var count = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= borderX &&
                    x < width - borderX &&
                    y >= borderY &&
                    y < height - borderY)
                {
                    continue;
                }

                if (pixels[
                    (y * width + x) *
                    4 + 3] > 8)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void Blend(
        byte[] pixels,
        int pixelIndex,
        double sourceB,
        double sourceG,
        double sourceR,
        double sourceAlpha)
    {
        var offset = pixelIndex * 4;
        var destinationAlpha =
            pixels[offset + 3] /
            255.0;
        var outputAlpha =
            sourceAlpha +
            destinationAlpha *
            (1.0 - sourceAlpha);

        if (outputAlpha <= 0.000001)
            return;

        pixels[offset] =
            ToByte(
                (sourceB * sourceAlpha +
                 pixels[offset] *
                 destinationAlpha *
                 (1.0 - sourceAlpha)) /
                outputAlpha);
        pixels[offset + 1] =
            ToByte(
                (sourceG * sourceAlpha +
                 pixels[offset + 1] *
                 destinationAlpha *
                 (1.0 - sourceAlpha)) /
                outputAlpha);
        pixels[offset + 2] =
            ToByte(
                (sourceR * sourceAlpha +
                 pixels[offset + 2] *
                 destinationAlpha *
                 (1.0 - sourceAlpha)) /
                outputAlpha);
        pixels[offset + 3] =
            ToByte(outputAlpha * 255.0);
    }

    private static double Interpolate(
        double a,
        double b,
        double c,
        double aw,
        double bw,
        double cw,
        double w1,
        double w2,
        double w3,
        double reciprocalW) =>
        (w1 * a * aw +
         w2 * b * bw +
         w3 * c * cw) /
        reciprocalW;

    private static double Bilinear(
        double p00,
        double p10,
        double p01,
        double p11,
        double tx,
        double ty)
    {
        var top =
            p00 +
            (p10 - p00) * tx;
        var bottom =
            p01 +
            (p11 - p01) * tx;

        return top +
               (bottom - top) * ty;
    }

    private static byte ToByte(
        double value) =>
        (byte)Math.Clamp(
            value,
            0.0,
            255.0);

    private static void Normalize(
        ref double x,
        ref double y,
        ref double z)
    {
        var length =
            Math.Sqrt(
                x * x +
                y * y +
                z * z);

        if (length <= 0.000001)
        {
            x = 0;
            y = 0;
            z = -1;
            return;
        }

        x /= length;
        y /= length;
        z /= length;
    }

    private static void Normalize(
        ref float x,
        ref float y,
        ref float z)
    {
        var length =
            MathF.Sqrt(
                x * x +
                y * y +
                z * z);

        if (length <= 0.000001f)
        {
            x = 0;
            y = 0;
            z = -1;
            return;
        }

        x /= length;
        y /= length;
        z /= length;
    }
}
