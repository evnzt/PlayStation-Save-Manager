using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace PlayStationSaveManager.Services;

public static class BuiltInSaveIcons
{
    private static readonly Lazy<ObjIconModel> SystemModel = new(() =>
        ObjIconModel.Load(
            Asset("SystemConfiguration", "System Configuration.obj"),
            Asset("SystemConfiguration", "System.png")));

    private static readonly Lazy<ObjIconModel> CorruptedModel = new(() =>
        ObjIconModel.Load(
            Asset("CorruptedSave", "Corrupted Save.obj"),
            Asset("CorruptedSave", "VertexColorBake.png")));

    private static string Asset(string folder, string file) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "FallbackIcons", folder, file);

    public static BitmapSource RenderSystemConfiguration(int width, int height, double rotationY = -0.35) =>
        SystemModel.Value.Render(width, height, rotationY);

    public static BitmapSource RenderCorruptedSave(int width, int height, double rotationY = -0.35) =>
        CorruptedModel.Value.Render(width, height, rotationY);

    public static ObjIconModel GetSystemModel() => SystemModel.Value;
    public static ObjIconModel GetCorruptedModel() => CorruptedModel.Value;

    public static bool IsSystemConfiguration(string directoryId, string gameTitle) =>
        directoryId.Equals("BEDATA-SYSTEM", StringComparison.OrdinalIgnoreCase) ||
        directoryId.EndsWith("-SYSTEM", StringComparison.OrdinalIgnoreCase) ||
        gameTitle.Contains("System Configuration", StringComparison.OrdinalIgnoreCase);
}
