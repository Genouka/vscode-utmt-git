using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

EnsureDataLoaded();

string projectRoot = Environment.GetEnvironmentVariable("UTMT_PROJECT_ROOT");
if (string.IsNullOrEmpty(projectRoot))
{
    throw new Exception("UTMT_PROJECT_ROOT environment variable is not set.");
}

string outputDir = Path.Combine(projectRoot, "fonts");
Directory.CreateDirectory(outputDir);

Console.WriteLine($"[UTMT-EXPORT-FONTS] Exporting all fonts to: {outputDir}");
Console.WriteLine($"[UTMT-EXPORT-FONTS] Total fonts: {Data.Fonts.Count}");

TextureWorker worker = new TextureWorker();

try
{
    int exported = 0;
    foreach (var fnt in Data.Fonts)
    {
        if (fnt == null || fnt.Name == null) continue;

        string fontName = fnt.Name.Content;
        string fontDir = Path.Combine(outputDir, fontName);
        Directory.CreateDirectory(fontDir);

        object texturePageItem = null;
        if (fnt.Texture != null)
        {
            texturePageItem = new
            {
                TargetX = fnt.Texture.TargetX,
                TargetY = fnt.Texture.TargetY,
                TargetWidth = fnt.Texture.TargetWidth,
                TargetHeight = fnt.Texture.TargetHeight,
                BoundingWidth = fnt.Texture.BoundingWidth,
                BoundingHeight = fnt.Texture.BoundingHeight
            };
        }

        var metadata = new
        {
            Name = fontName,
            DisplayName = fnt.DisplayName?.Content,
            EmSize = fnt.EmSize,
            Bold = fnt.Bold,
            Italic = fnt.Italic,
            RangeStart = fnt.RangeStart,
            RangeEnd = fnt.RangeEnd,
            Charset = fnt.Charset,
            AntiAliasing = fnt.AntiAliasing,
            ScaleX = fnt.ScaleX,
            ScaleY = fnt.ScaleY,
            AscenderOffset = fnt.AscenderOffset,
            Ascender = fnt.Ascender,
            SDFSpread = fnt.SDFSpread,
            LineHeight = fnt.LineHeight,
            TexturePageItem = texturePageItem
        };

        string jsonOutput = JsonConvert.SerializeObject(metadata, Formatting.Indented);
        File.WriteAllText(Path.Combine(fontDir, "metadata.json"), jsonOutput);

        if (fnt.Texture != null)
        {
            string fileName = $"{fontName}_0.png";
            worker.ExportAsPNG(fnt.Texture, Path.Combine(fontDir, fileName), null, true);
        }

        if (fnt.Glyphs != null && fnt.Glyphs.Count > 0)
        {
            var csvLines = new List<string>();
            csvLines.Add($"{fnt.DisplayName?.Content ?? fontName};{fnt.EmSize};{fnt.Bold};{fnt.Italic}");
            csvLines.Add(fnt.RangeStart.ToString());

            foreach (var glyph in fnt.Glyphs)
            {
                csvLines.Add($"{glyph.Character};{glyph.SourceX};{glyph.SourceY};{glyph.SourceWidth};{glyph.SourceHeight};{glyph.Shift};{glyph.Offset}");
            }

            File.WriteAllText(Path.Combine(fontDir, $"glyphs_{fontName}.csv"), string.Join("\n", csvLines));
        }

        exported++;
        Console.WriteLine($"[UTMT-EXPORT-FONTS] Exported: {fontName} ({fnt.Glyphs?.Count ?? 0} glyphs)");
    }

    Console.WriteLine($"[UTMT-EXPORT-FONTS] Complete! Exported {exported} fonts.");
}
finally
{
    worker.Dispose();
}
