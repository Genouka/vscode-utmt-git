using System;
using System.IO;
using Newtonsoft.Json;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

EnsureDataLoaded();

string projectRoot = Environment.GetEnvironmentVariable("UTMT_PROJECT_ROOT");
if (string.IsNullOrEmpty(projectRoot))
{
    throw new Exception("UTMT_PROJECT_ROOT environment variable is not set.");
}

string outputDir = Path.Combine(projectRoot, "backgrounds");
Directory.CreateDirectory(outputDir);

Console.WriteLine($"[UTMT-EXPORT-BACKGROUNDS] Exporting all backgrounds to: {outputDir}");
Console.WriteLine($"[UTMT-EXPORT-BACKGROUNDS] Total backgrounds: {Data.Backgrounds.Count}");

TextureWorker worker = new TextureWorker();

try
{
    int exported = 0;
    foreach (var bg in Data.Backgrounds)
    {
        try
        {
            if (bg == null || bg.Name == null) continue;

            string bgName = bg.Name.Content;
            string bgDir = Path.Combine(outputDir, bgName);
            Directory.CreateDirectory(bgDir);

            object texturePageItem = null;
            if (bg.Texture != null)
            {
                texturePageItem = new
                {
                    TargetX = bg.Texture.TargetX,
                    TargetY = bg.Texture.TargetY,
                    TargetWidth = bg.Texture.TargetWidth,
                    TargetHeight = bg.Texture.TargetHeight,
                    BoundingWidth = bg.Texture.BoundingWidth,
                    BoundingHeight = bg.Texture.BoundingHeight
                };
            }

            var metadata = new
            {
                Name = bgName,
                Transparent = bg.Transparent,
                Smooth = bg.Smooth,
                Preload = bg.Preload,
                GMS2TilesetVersion = bg.GMS2TilesetVersion,
                GMS2TileWidth = bg.GMS2TileWidth,
                GMS2TileHeight = bg.GMS2TileHeight,
                GMS2OutputBorderX = bg.GMS2OutputBorderX,
                GMS2OutputBorderY = bg.GMS2OutputBorderY,
                GMS2TileColumns = bg.GMS2TileColumns,
                GMS2ItemsPerTileCount = bg.GMS2ItemsPerTileCount,
                GMS2TileCount = bg.GMS2TileCount,
                GMS2FrameLength = bg.GMS2FrameLength,
                TexturePageItem = texturePageItem
            };

            string jsonOutput = JsonConvert.SerializeObject(metadata, Formatting.Indented);
            File.WriteAllText(Path.Combine(bgDir, "metadata.json"), jsonOutput);

            if (bg.Texture != null)
            {
                string fileName = $"{bgName}_0.png";
                worker.ExportAsPNG(bg.Texture, Path.Combine(bgDir, fileName), null, true);
            }

            exported++;
            Console.WriteLine($"[UTMT-EXPORT-BACKGROUNDS] Exported: {bgName}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[UTMT-EXPORT-BACKGROUNDS] Error exporting background: {e.Message}");
        }
    }

    Console.WriteLine($"[UTMT-EXPORT-BACKGROUNDS] Complete! Exported {exported} backgrounds.");
}
finally
{
    worker.Dispose();
}
