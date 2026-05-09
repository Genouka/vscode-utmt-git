using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UndertaleModLib.Models;

EnsureDataLoaded();

string projectRoot = Environment.GetEnvironmentVariable("UTMT_PROJECT_ROOT");
if (string.IsNullOrEmpty(projectRoot))
{
    throw new Exception("UTMT_PROJECT_ROOT environment variable is not set.");
}

string outputDir = Path.Combine(projectRoot, "rooms");
Directory.CreateDirectory(outputDir);

Console.WriteLine($"[UTMT-EXPORT-ROOMS] Exporting all rooms to: {outputDir}");
Console.WriteLine($"[UTMT-EXPORT-ROOMS] Total rooms: {Data.Rooms.Count}");

int exported = 0;
foreach (var room in Data.Rooms)
{
    try
    {
        if (room == null || room.Name == null) continue;

        string roomName = room.Name.Content;
        string roomDir = Path.Combine(outputDir, roomName);
        Directory.CreateDirectory(roomDir);

        var backgroundsList = new List<object>();
        foreach (var bg in room.Backgrounds)
        {
            if (bg == null) continue;
            backgroundsList.Add(new
            {
                Enabled = bg.Enabled,
                Foreground = bg.Foreground,
                BackgroundDefinition = bg.BackgroundDefinition?.Name?.Content,
                X = bg.X,
                Y = bg.Y,
                TiledHorizontally = bg.TiledHorizontally,
                TiledVertically = bg.TiledVertically,
                SpeedX = bg.SpeedX,
                SpeedY = bg.SpeedY,
                Stretch = bg.Stretch
            });
        }

        var viewsList = new List<object>();
        foreach (var view in room.Views)
        {
            if (view == null) continue;
            viewsList.Add(new
            {
                Enabled = view.Enabled,
                ViewX = view.ViewX,
                ViewY = view.ViewY,
                ViewWidth = view.ViewWidth,
                ViewHeight = view.ViewHeight,
                PortX = view.PortX,
                PortY = view.PortY,
                PortWidth = view.PortWidth,
                PortHeight = view.PortHeight,
                BorderX = view.BorderX,
                BorderY = view.BorderY,
                SpeedX = view.SpeedX,
                SpeedY = view.SpeedY,
                ObjectId = view.ObjectId?.Name?.Content
            });
        }

        var gameObjectsList = new List<object>();
        foreach (var obj in room.GameObjects)
        {
            if (obj == null) continue;
            gameObjectsList.Add(new
            {
                X = obj.X,
                Y = obj.Y,
                ObjectDefinition = obj.ObjectDefinition?.Name?.Content,
                InstanceID = obj.InstanceID,
                CreationCode = obj.CreationCode?.Name?.Content,
                ScaleX = obj.ScaleX,
                ScaleY = obj.ScaleY,
                Color = obj.Color,
                Rotation = obj.Rotation,
                PreCreateCode = obj.PreCreateCode?.Name?.Content,
                ImageSpeed = obj.ImageSpeed,
                ImageIndex = obj.ImageIndex
            });
        }

        var tilesList = new List<object>();
        foreach (var tile in room.Tiles)
        {
            if (tile == null) continue;
            tilesList.Add(new
            {
                X = tile.X,
                Y = tile.Y,
                SpriteMode = tile.spriteMode,
                BackgroundDefinition = tile.spriteMode ? null : tile.BackgroundDefinition?.Name?.Content,
                SpriteDefinition = tile.spriteMode ? tile.SpriteDefinition?.Name?.Content : null,
                SourceX = tile.SourceX,
                SourceY = tile.SourceY,
                Width = tile.Width,
                Height = tile.Height,
                TileDepth = tile.TileDepth,
                InstanceID = tile.InstanceID,
                ScaleX = tile.ScaleX,
                ScaleY = tile.ScaleY,
                Color = tile.Color
            });
        }

        var layersList = new List<object>();
        foreach (var layer in room.Layers)
        {
            if (layer == null) continue;
            layersList.Add(SerializeLayer(layer));
        }

        var sequencesList = new List<string>();
        foreach (var seqRef in room.Sequences)
        {
            if (seqRef?.Resource != null)
                sequencesList.Add(seqRef.Resource.Name?.Content);
        }

        var jsonObject = new
        {
            Name = roomName,
            Caption = room.Caption?.Content,
            Width = room.Width,
            Height = room.Height,
            Speed = room.Speed,
            Persistent = room.Persistent,
            BackgroundColor = room.BackgroundColor,
            DrawBackgroundColor = room.DrawBackgroundColor,
            CreationCodeId = room.CreationCodeId?.Name?.Content,
            Flags = (uint)room.Flags,
            World = room.World,
            Top = room.Top,
            Left = room.Left,
            Right = room.Right,
            Bottom = room.Bottom,
            GravityX = room.GravityX,
            GravityY = room.GravityY,
            MetersPerPixel = room.MetersPerPixel,
            Backgrounds = backgroundsList,
            Views = viewsList,
            GameObjects = gameObjectsList,
            Tiles = tilesList,
            Layers = layersList,
            Sequences = sequencesList
        };

        string jsonOutput = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
        File.WriteAllText(Path.Combine(roomDir, "metadata.json"), jsonOutput);

        exported++;
        Console.WriteLine($"[UTMT-EXPORT-ROOMS] Exported: {roomName} ({room.GameObjects.Count} objects, {room.Tiles.Count} tiles, {room.Layers.Count} layers)");
    }
    catch (Exception e)
    {
        Console.WriteLine($"[UTMT-EXPORT-ROOMS] Error exporting room: {room?.Name?.Content ?? "null"}. Exception: {e.Message}");
    }
}

Console.WriteLine($"[UTMT-EXPORT-ROOMS] Complete! Exported {exported} rooms.");

object SerializeLayer(UndertaleRoom.Layer layer)
{
    var layerObj = new Dictionary<string, object>
    {
        { "LayerName", layer.LayerName?.Content },
        { "LayerId", layer.LayerId },
        { "LayerType", (int)layer.LayerType },
        { "LayerDepth", layer.LayerDepth },
        { "XOffset", layer.XOffset },
        { "YOffset", layer.YOffset },
        { "HSpeed", layer.HSpeed },
        { "VSpeed", layer.VSpeed },
        { "IsVisible", layer.IsVisible },
        { "EffectEnabled", layer.EffectEnabled },
        { "EffectType", layer.EffectType?.Content }
    };

    var effectProps = new List<object>();
    if (layer.EffectProperties != null)
    {
        foreach (var prop in layer.EffectProperties)
        {
            if (prop == null) continue;
            effectProps.Add(new
            {
                Name = prop.Name?.Content,
                Value = prop.Value?.Content
            });
        }
    }
    layerObj["EffectProperties"] = effectProps;

    if (layer.InstancesData != null)
    {
        var instanceIds = new List<uint>();
        foreach (var inst in layer.InstancesData.Instances)
        {
            if (inst != null)
                instanceIds.Add(inst.InstanceID);
        }
        layerObj["InstancesData"] = new { InstanceIds = instanceIds };
    }

    if (layer.TilesData != null)
    {
        var tileDataJagged = new List<List<uint>>();
        if (layer.TilesData.TileData != null)
        {
            foreach (var row in layer.TilesData.TileData)
            {
                if (row != null)
                    tileDataJagged.Add(row.ToList());
                else
                    tileDataJagged.Add(new List<uint>());
            }
        }
        layerObj["TilesData"] = new
        {
            Background = layer.TilesData.Background?.Name?.Content,
            TilesX = layer.TilesData.TilesX,
            TilesY = layer.TilesData.TilesY,
            TileData = tileDataJagged
        };
    }

    if (layer.BackgroundData != null)
    {
        layerObj["BackgroundData"] = new
        {
            Visible = layer.BackgroundData.Visible,
            Foreground = layer.BackgroundData.Foreground,
            Sprite = layer.BackgroundData.Sprite?.Name?.Content,
            TiledHorizontally = layer.BackgroundData.TiledHorizontally,
            TiledVertically = layer.BackgroundData.TiledVertically,
            Stretch = layer.BackgroundData.Stretch,
            Color = layer.BackgroundData.Color,
            FirstFrame = layer.BackgroundData.FirstFrame,
            AnimationSpeed = layer.BackgroundData.AnimationSpeed,
            AnimationSpeedType = (int)layer.BackgroundData.AnimationSpeedType
        };
    }

    if (layer.AssetsData != null)
    {
        var legacyTiles = new List<object>();
        if (layer.AssetsData.LegacyTiles != null)
        {
            foreach (var tile in layer.AssetsData.LegacyTiles)
            {
                if (tile == null) continue;
                legacyTiles.Add(new
                {
                    X = tile.X,
                    Y = tile.Y,
                    SpriteMode = tile.spriteMode,
                    BackgroundDefinition = tile.spriteMode ? null : tile.BackgroundDefinition?.Name?.Content,
                    SpriteDefinition = tile.spriteMode ? tile.SpriteDefinition?.Name?.Content : null,
                    SourceX = tile.SourceX,
                    SourceY = tile.SourceY,
                    Width = tile.Width,
                    Height = tile.Height,
                    TileDepth = tile.TileDepth,
                    InstanceID = tile.InstanceID,
                    ScaleX = tile.ScaleX,
                    ScaleY = tile.ScaleY,
                    Color = tile.Color
                });
            }
        }

        var sprites = new List<object>();
        if (layer.AssetsData.Sprites != null)
        {
            foreach (var spr in layer.AssetsData.Sprites)
            {
                if (spr == null) continue;
                sprites.Add(new
                {
                    Name = spr.Name?.Content,
                    Sprite = spr.Sprite?.Name?.Content,
                    X = spr.X,
                    Y = spr.Y,
                    ScaleX = spr.ScaleX,
                    ScaleY = spr.ScaleY,
                    Color = spr.Color,
                    AnimationSpeed = spr.AnimationSpeed,
                    AnimationSpeedType = (int)spr.AnimationSpeedType,
                    FrameIndex = spr.FrameIndex,
                    Rotation = spr.Rotation
                });
            }
        }

        var sequences = new List<object>();
        if (layer.AssetsData.Sequences != null)
        {
            foreach (var seq in layer.AssetsData.Sequences)
            {
                if (seq == null) continue;
                sequences.Add(new
                {
                    Name = seq.Name?.Content,
                    Sequence = seq.Sequence?.Name?.Content,
                    X = seq.X,
                    Y = seq.Y,
                    ScaleX = seq.ScaleX,
                    ScaleY = seq.ScaleY,
                    Color = seq.Color,
                    AnimationSpeed = seq.AnimationSpeed,
                    AnimationSpeedType = (int)seq.AnimationSpeedType,
                    FrameIndex = seq.FrameIndex,
                    Rotation = seq.Rotation
                });
            }
        }

        layerObj["AssetsData"] = new
        {
            LegacyTiles = legacyTiles,
            Sprites = sprites,
            Sequences = sequences
        };
    }

    if (layer.EffectData != null)
    {
        layerObj["EffectData"] = new
        {
            EffectType = layer.EffectData.EffectType?.Content,
        };
    }

    return layerObj;
}
