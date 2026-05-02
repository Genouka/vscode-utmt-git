using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

EnsureDataLoaded();

string projectRoot = Environment.GetEnvironmentVariable("UTMT_PROJECT_ROOT");
if (string.IsNullOrEmpty(projectRoot))
{
    throw new Exception("UTMT_PROJECT_ROOT environment variable is not set.");
}

string outputDir = Path.Combine(projectRoot, "sprites");
Directory.CreateDirectory(outputDir);

Console.WriteLine($"[UTMT-EXPORT-SPRITES] Exporting all sprites to: {outputDir}");
Console.WriteLine($"[UTMT-EXPORT-SPRITES] Total sprites: {Data.Sprites.Count}");

TextureWorker worker = new TextureWorker();

try
{
    int exported = 0;
    foreach (var spr in Data.Sprites)
    {
        if (spr == null || spr.Name == null) continue;

        string spriteName = spr.Name.Content;
        string spriteDir = Path.Combine(outputDir, spriteName);
        Directory.CreateDirectory(spriteDir);

        var metadata = new
        {
            Name = spriteName,
            Width = spr.Width,
            Height = spr.Height,
            MarginLeft = spr.MarginLeft,
            MarginRight = spr.MarginRight,
            MarginBottom = spr.MarginBottom,
            MarginTop = spr.MarginTop,
            BBoxMode = spr.BBoxMode,
            SepMasks = (int)spr.SepMasks,
            OriginX = spr.OriginX,
            OriginY = spr.OriginY,
            IsSpecialType = spr.IsSpecialType,
            SVersion = spr.SVersion,
            GMS2PlaybackSpeed = spr.GMS2PlaybackSpeed,
            GMS2PlaybackSpeedType = (int)spr.GMS2PlaybackSpeedType
        };

        string jsonOutput = JsonConvert.SerializeObject(metadata, Formatting.Indented);
        File.WriteAllText(Path.Combine(spriteDir, "metadata.json"), jsonOutput);

        for (int i = 0; i < spr.Textures.Count; i++)
        {
            if (spr.Textures[i]?.Texture != null)
            {
                string fileName = $"{spriteName}_{i}.png";
                worker.ExportAsPNG(spr.Textures[i].Texture, Path.Combine(spriteDir, fileName), null, false);
            }
        }

        exported++;
        Console.WriteLine($"[UTMT-EXPORT-SPRITES] Exported: {spriteName} ({spr.Textures.Count} frames)");
    }

    Console.WriteLine($"[UTMT-EXPORT-SPRITES] Complete! Exported {exported} sprites.");
}
finally
{
    worker.Dispose();
}
