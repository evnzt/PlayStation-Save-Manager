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

        // Icons whose animation timeline contradicts the declared header
        // length (Blade II is one example) are held on a stable pose. Some
        // of those models are also authored as thin, single-sided geometry,
        // so the normal-based thin-card cull can make pieces disappear while
        // the preview rotates edge-on. Disable only that cull for the same
        // narrowly detected malformed-animation case; all valid icons keep
        // the existing renderer behavior.
        var renderMalformedAnimationDoubleSided =
            HasContradictoryAnimationTimeline(model);

        // Keep geometry-classification heuristics stable for the entire icon.
        // Re-evaluating them from the current morph pose can make folding
        // animations suddenly become a "thin card" only at one point in
        // their cycle (the SmackDown! ladder is a reference case), causing
        // foreground details to disappear until the model opens again.
        // Classify from the authored base shape instead; genuinely flat
        // icons retain the safeguard, while animated objects cannot switch
        // renderer modes mid-animation.
        var classificationShape = model.Shapes[0]
            .Select(value => (double)value)
            .ToArray();

        var thinCardDepthBias =
            GetThinCardDepthBias(classificationShape);

        // Large, flat background panels in some animated icons can carry
        // unreliable per-vertex normals. Keep this decision stable for the
        // same reason as the thin-card classification above.
        var largePlanarCullArea =
            GetLargePlanarCullArea(classificationShape);

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

        var renderThinClosedPanelDoubleSided =
            thinCardDepthBias > 0.0 &&
            IsThinClosedPanel(
                model,
                classificationShape);

        var selected = Rasterize(
            model,
            projected,
            width,
            height,
            useTextureMask: useMasked,
            useNeutralLighting:
                model.Shapes.Length == 1 &&
                thinCardDepthBias > 0.0,
            thinCardDepthBias: thinCardDepthBias,
            disableThinCardBackfaceCull:
                renderMalformedAnimationDoubleSided ||
                renderThinClosedPanelDoubleSided,
            largePlanarCullArea: largePlanarCullArea);

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
                    thinCardDepthBias: 0.0,
                    disableThinCardBackfaceCull: false,
                    largePlanarCullArea: 0.0);

            var masked =
                Rasterize(
                    model,
                    canonicalProjected,
                    Math.Max(96, width),
                    Math.Max(96, height),
                    useTextureMask: true,
                    useNeutralLighting: false,
                    thinCardDepthBias: 0.0,
                    disableThinCardBackfaceCull: false,
                    largePlanarCullArea: 0.0);

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

    private static bool IsThinClosedPanel(
        Ps2IconModel model,
        double[] positions)
    {
        // Detect a shallow CLOSED 3D panel: two substantial opposed broad
        // faces plus real side-wall geometry. This is deliberately narrower
        // than the general thin-card test so round/irregular icons are not
        // rerouted. NFS Most Wanted's plaque-style icon is the reference
        // geometry, but no game/title/serial is consulted.
        if (model.Shapes.Length != 1 ||
            model.VertexCount < 24 ||
            positions.Length < model.VertexCount * 3)
        {
            return false;
        }

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        double minZ = double.MaxValue, maxZ = double.MinValue;

        for (var offset = 0;
             offset + 2 < positions.Length;
             offset += 3)
        {
            var x = positions[offset] / 4096.0;
            var y = positions[offset + 1] / 4096.0;
            var z = positions[offset + 2] / 4096.0;

            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z);
        }

        var spanX = Math.Max(0.0001, maxX - minX);
        var spanY = Math.Max(0.0001, maxY - minY);
        var spanZ = Math.Max(0.0, maxZ - minZ);
        var faceSpan = Math.Max(spanX, spanY);

        // It must be shallow, but still have measurable thickness.
        if (spanZ <= faceSpan * 0.005 ||
            spanZ > faceSpan * 0.075)
        {
            return false;
        }

        var positiveBroad = 0;
        var negativeBroad = 0;
        var sideWall = 0;
        var usable = 0;

        for (var triangle = 0;
             triangle + 2 < model.VertexCount;
             triangle += 3)
        {
            var a = triangle * 3;
            var b = (triangle + 1) * 3;
            var c = (triangle + 2) * 3;

            var ax = positions[a] / 4096.0;
            var ay = positions[a + 1] / 4096.0;
            var az = positions[a + 2] / 4096.0;
            var bx = positions[b] / 4096.0;
            var by = positions[b + 1] / 4096.0;
            var bz = positions[b + 2] / 4096.0;
            var cx = positions[c] / 4096.0;
            var cy = positions[c + 1] / 4096.0;
            var cz = positions[c + 2] / 4096.0;

            var e1x = bx - ax;
            var e1y = by - ay;
            var e1z = bz - az;
            var e2x = cx - ax;
            var e2y = cy - ay;
            var e2z = cz - az;

            var nx = e1y * e2z - e1z * e2y;
            var ny = e1z * e2x - e1x * e2z;
            var nz = e1x * e2y - e1y * e2x;
            var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);

            if (length <= 0.000001)
                continue;

            nz /= length;
            usable++;

            if (nz >= 0.80)
                positiveBroad++;
            else if (nz <= -0.80)
                negativeBroad++;
            else if (Math.Abs(nz) <= 0.35)
                sideWall++;
        }

        if (usable < 8)
            return false;

        // Require meaningful geometry on BOTH broad faces and around the
        // perimeter. Also require the opposed faces to be reasonably balanced.
        var minimumBroad = Math.Max(2, usable / 10);
        var minimumSide = Math.Max(2, usable / 8);
        var broadRatio =
            Math.Min(positiveBroad, negativeBroad) /
            (double)Math.Max(1, Math.Max(positiveBroad, negativeBroad));

        return positiveBroad >= minimumBroad &&
               negativeBroad >= minimumBroad &&
               sideWall >= minimumSide &&
               broadRatio >= 0.45;
    }

    private static double GetLargePlanarCullArea(
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

        // Only engage for genuinely thin card-like models.  The threshold
        // selects broad backdrop panels, not the smaller foreground pieces.
        if (spanZ > faceSpan * 0.10)
            return 0.0;

        return spanX * spanY * 0.12;
    }

    private static bool HasContradictoryAnimationTimeline(
        Ps2IconModel model)
    {
        if (model.Shapes.Length <= 1 ||
            model.FrameLength <= 0)
        {
            return false;
        }

        var keys = model.Frames
            .SelectMany(frame => frame.Keys)
            .Where(key =>
                float.IsFinite(key.Time) &&
                float.IsFinite(key.Value))
            .ToArray();

        if (keys.Length == 0)
            return false;

        var minimumKeyTime = keys.Min(key => key.Time);
        var maximumKeyTime = keys.Max(key => key.Time);
        var declaredLength = Math.Max(1.0, model.FrameLength);

        return maximumKeyTime - minimumKeyTime >
               declaredLength * 1.5;
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

        // Some PS2 icons contain degenerate animation tables where every
        // key is stamped at the same instant. Cycling evenly through every
        // stored shape makes those icons thrash wildly (Sled Storm is one
        // example). In that case the key values describe the intended held
        // shape, so render that shape instead of inventing intermediate
        // animation that is not present in the file.
        var animationKeys = model.Frames
            .SelectMany(frame => frame.Keys)
            .Where(key =>
                float.IsFinite(key.Time) &&
                float.IsFinite(key.Value))
            .ToArray();

        if (animationKeys.Length > 0)
        {
            var firstTime = animationKeys[0].Time;
            var oneInstant = animationKeys.All(
                key => Math.Abs(key.Time - firstTime) < 0.0001f);

            if (oneInstant)
            {
                var heldShape = model.Frames
                    .Where(frame =>
                        frame.ShapeId >= 0 &&
                        frame.ShapeId < shapeCount)
                    .Select(frame => new
                    {
                        frame.ShapeId,
                        Weight = frame.Keys
                            .Where(key => float.IsFinite(key.Value))
                            .Select(key => (double)key.Value)
                            .DefaultIfEmpty(0.0)
                            .Max()
                    })
                    .OrderByDescending(item => item.Weight)
                    .FirstOrDefault();

                if (heldShape is not null && heldShape.Weight > 0.0)
                {
                    return model.Shapes[heldShape.ShapeId]
                        .Select(value => (double)value)
                        .ToArray();
                }
            }

            // A few icons contain a keyframe timeline that is much longer
            // than the animation length declared in the header. Treating
            // those shapes as a normal evenly timed loop makes the model
            // thrash rapidly. Preserve the pose selected at the beginning
            // of the real key timeline instead. This fallback is limited to
            // contradictory files and does not alter valid animations.
            var minimumKeyTime = animationKeys.Min(key => key.Time);
            var maximumKeyTime = animationKeys.Max(key => key.Time);
            var declaredLength = Math.Max(1.0, model.FrameLength);

            if (maximumKeyTime - minimumKeyTime >
                declaredLength * 1.5)
            {
                var initialShape = model.Frames
                    .Where(frame =>
                        frame.ShapeId >= 0 &&
                        frame.ShapeId < shapeCount)
                    .Select(frame => new
                    {
                        frame.ShapeId,
                        Weight = frame.Keys
                            .Where(key =>
                                float.IsFinite(key.Time) &&
                                float.IsFinite(key.Value) &&
                                Math.Abs(key.Time - minimumKeyTime) < 0.0001f)
                            .Select(key => (double)key.Value)
                            .DefaultIfEmpty(0.0)
                            .Max()
                    })
                    .OrderByDescending(item => item.Weight)
                    .FirstOrDefault();

                if (initialShape is not null && initialShape.Weight > 0.0)
                {
                    return model.Shapes[initialShape.ShapeId]
                        .Select(value => (double)value)
                        .ToArray();
                }
            }
        }

        // Valid native rigid-spin animations (Jackie Chan Adventures is
        // the reference case) are handled only after v10.32's malformed
        // timeline safeguards above have had first refusal. This keeps Sled
        // Storm on its original safe path while allowing genuine authored
        // rigid rotation to remain seamless.
        if (model.TryGetNativeRigidYRotationAngles(out var rigidAngles) &&
            rigidAngles.Count == shapeCount)
        {
            return BuildRigidYRotationVertices(
                model,
                rigidAngles,
                elapsedSeconds);
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

    private static double[] BuildRigidYRotationVertices(
        Ps2IconModel model,
        IReadOnlyList<double> angles,
        double elapsedSeconds)
    {
        var shapeCount = model.Shapes.Length;
        var rawFrame =
            elapsedSeconds *
            60.0 *
            model.AnimationSpeed +
            model.PlayOffset;

        var framesPerShape =
            model.FrameLength /
            (double)shapeCount;
        if (framesPerShape <= 0.0)
            framesPerShape = 1.0;

        // Keep rigid-spin animations unwrapped across ICN cycle boundaries.
        // The previous implementation held the final authored pose for the
        // last shape interval to avoid a 177° -> 0° reverse morph. That hold
        // created a visible pause once per revolution. Instead, infer the
        // normal angular step from the authored poses and use it for the
        // final interval, then carry that accumulated rotation into the next
        // cycle. Rotation is periodic, so the mesh remains visually identical
        // while the motion stays continuous.
        var deltas = new List<double>();
        for (var index = 1; index < angles.Count; index++)
        {
            var delta = angles[index] - angles[index - 1];
            if (double.IsFinite(delta) && Math.Abs(delta) > 0.000001)
                deltas.Add(delta);
        }

        var inferredStep = deltas.Count > 0
            ? deltas.OrderBy(value => value).ElementAt(deltas.Count / 2)
            : 0.0;

        var cycleAdvance =
            angles[shapeCount - 1] +
            inferredStep -
            angles[0];

        var cyclePosition = rawFrame / model.FrameLength;
        var cycleIndex = Math.Floor(cyclePosition);
        var frame = rawFrame - cycleIndex * model.FrameLength;
        if (frame < 0.0)
        {
            frame += model.FrameLength;
            cycleIndex -= 1.0;
        }

        var shapePosition = frame / framesPerShape;
        var current = Math.Clamp(
            (int)Math.Floor(shapePosition),
            0,
            shapeCount - 1);
        var tween = shapePosition - Math.Floor(shapePosition);
        tween = tween * tween * (3.0 - 2.0 * tween);

        var currentAngle = angles[current];
        var nextAngle = current >= shapeCount - 1
            ? angles[0] + cycleAdvance
            : angles[current + 1];

        var angle =
            currentAngle +
            (nextAngle - currentAngle) *
            tween +
            cycleIndex * cycleAdvance;

        var source = model.Shapes[0];
        var output = new double[model.VertexCount * 3];

        double centerX = 0.0, centerY = 0.0, centerZ = 0.0;
        for (var vertex = 0; vertex < model.VertexCount; vertex++)
        {
            var p = vertex * 3;
            centerX += source[p];
            centerY += source[p + 1];
            centerZ += source[p + 2];
        }
        centerX /= model.VertexCount;
        centerY /= model.VertexCount;
        centerZ /= model.VertexCount;

        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        for (var vertex = 0; vertex < model.VertexCount; vertex++)
        {
            var p = vertex * 3;
            var x = source[p] - centerX;
            var y = source[p + 1] - centerY;
            var z = source[p + 2] - centerZ;

            output[p] = centerX + x * cos + z * sin;
            output[p + 1] = centerY + y;
            output[p + 2] = centerZ - x * sin + z * cos;
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
        double thinCardDepthBias,
        bool disableThinCardBackfaceCull,
        double largePlanarCullArea)
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
                thinCardDepthBias,
                disableThinCardBackfaceCull,
                largePlanarCullArea);
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
        double thinCardDepthBias,
        bool disableThinCardBackfaceCull,
        double largePlanarCullArea)
    {
        var averageNormalZ =
            (a.Source.NormalZ +
             b.Source.NormalZ +
             c.Source.NormalZ) /
            3.0;

        var cullNormalZ = averageNormalZ;

        if (largePlanarCullArea > 0.0)
        {
            var edge1X = b.Source.X - a.Source.X;
            var edge1Y = b.Source.Y - a.Source.Y;
            var edge1Z = b.Source.Z - a.Source.Z;
            var edge2X = c.Source.X - a.Source.X;
            var edge2Y = c.Source.Y - a.Source.Y;
            var edge2Z = c.Source.Z - a.Source.Z;

            var faceX = edge1Y * edge2Z - edge1Z * edge2Y;
            var faceY = edge1Z * edge2X - edge1X * edge2Z;
            var faceZ = edge1X * edge2Y - edge1Y * edge2X;
            var faceLength = Math.Sqrt(
                faceX * faceX +
                faceY * faceY +
                faceZ * faceZ);
            var faceArea = faceLength * 0.5;

            if (faceLength > 0.000001 &&
                faceArea >= largePlanarCullArea)
            {
                cullNormalZ = faceZ / faceLength;
            }
        }

        if (!disableThinCardBackfaceCull &&
            thinCardDepthBias > 0.0 &&
            cullNormalZ >= -0.015)
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
            // Flat, single-shape icons are effectively textured cards.
            // Per-vertex normals can disagree across their triangle split,
            // producing a visible dark seam through otherwise continuous
            // artwork (for example the CodeBreaker/Pelican icon). Preserve
            // the source texture without synthetic lighting on this path.
            return (1.0, 1.0, 1.0);
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
