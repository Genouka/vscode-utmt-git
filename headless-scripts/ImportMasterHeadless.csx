using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UndertaleModLib.Models;
using UndertaleModLib.Compiler;
using UndertaleModLib.Util;
using ImageMagick;

EnsureDataLoaded();

string repoDir = Environment.GetEnvironmentVariable("UTMT_PROJECT_ROOT");
if (string.IsNullOrEmpty(repoDir))
{
    throw new Exception("UTMT_PROJECT_ROOT environment variable is not set.");
}
if (!Directory.Exists(repoDir))
{
    throw new Exception($"Project root directory does not exist: {repoDir}");
}

Console.WriteLine($"[UTMT-IMPORT] Starting import from: {repoDir}");

string rootFolder = repoDir;

string JoinWithinDirectory(string baseDir, string relativePath)
{
    var combined = Path.GetFullPath(Path.Combine(baseDir, relativePath));
    var baseFull = Path.GetFullPath(baseDir);
    if (!combined.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Path escapes the base directory.");
    }
    return combined;
}

string GetFolderCI(string root, string name)
{
    return Directory.GetDirectories(root)
        .FirstOrDefault(d => Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase));
}

/* SOUNDS */

string dirSounds = GetFolderCI(repoDir, "sounds");
if (!string.IsNullOrEmpty(dirSounds))
{
    Console.WriteLine($"[UTMT-IMPORT] Phase: Sounds");
    string[] soundFiles = Directory.GetFiles(dirSounds, "*.*", SearchOption.AllDirectories);

    foreach (string file in soundFiles)
    {
        try
        {
            string filename = Path.GetFileName(file);
            if (!(filename.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)))
                continue;

            string soundName = Path.GetFileNameWithoutExtension(file);
            bool isOGG = filename.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);

            float sndVolume = 1.0f;
            float sndPitch = 1.0f;
            uint sndEffects = 0;
            string sndAudioGroupName = null;

            string metaPath = Path.Combine(Path.GetDirectoryName(file), "metadata.json");
            if (File.Exists(metaPath))
            {
                JObject meta = JObject.Parse(File.ReadAllText(metaPath));
                if (meta["Volume"] != null) sndVolume = (float)meta["Volume"];
                if (meta["Pitch"] != null) sndPitch = (float)meta["Pitch"];
                if (meta["Effects"] != null) sndEffects = (uint)meta["Effects"];
                if (meta["AudioGroup"] != null) sndAudioGroupName = (string)meta["AudioGroup"];
            }

            UndertaleSound existingSound = Data.Sounds.FirstOrDefault(s => s.Name?.Content == soundName);
            UndertaleEmbeddedAudio soundData = new UndertaleEmbeddedAudio { Data = File.ReadAllBytes(file) };

            int targetGroupID = Data.GetBuiltinSoundGroupID();
            if (!string.IsNullOrEmpty(sndAudioGroupName))
            {
                var ag = Data.AudioGroups.FirstOrDefault(g => g.Name?.Content == sndAudioGroupName);
                if (ag != null) targetGroupID = Data.AudioGroups.IndexOf(ag);
            }
            else if (existingSound != null)
            {
                targetGroupID = existingSound.GroupID;
            }

            bool isExternalGroup = (targetGroupID != Data.GetBuiltinSoundGroupID());
            int audioID = -1;

            if (isExternalGroup)
            {
                UndertaleData audioGroupDat;
                string relativePath = $"audiogroup{targetGroupID}.dat";
                if (targetGroupID < Data.AudioGroups.Count && Data.AudioGroups[targetGroupID] is UndertaleAudioGroup { Path.Content: string customPath })
                    relativePath = customPath;

                string agPath = JoinWithinDirectory(Path.GetDirectoryName(FilePath), relativePath);
                using (FileStream fsRead = new(agPath, FileMode.Open, FileAccess.Read))
                    audioGroupDat = UndertaleIO.Read(fsRead);

                if (existingSound?.AudioFile != null)
                    audioGroupDat.EmbeddedAudio.Remove(existingSound.AudioFile);

                audioGroupDat.EmbeddedAudio.Add(soundData);
                audioID = audioGroupDat.EmbeddedAudio.Count - 1;

                using (FileStream fsWrite = new(agPath, FileMode.Create))
                    UndertaleIO.Write(fsWrite, audioGroupDat);
            }
            else
            {
                if (existingSound?.AudioFile != null)
                    Data.EmbeddedAudio.Remove(existingSound.AudioFile);

                Data.EmbeddedAudio.Add(soundData);
                audioID = Data.EmbeddedAudio.Count - 1;
            }

            UndertaleSound.AudioEntryFlags newFlags = isOGG
                ? (UndertaleSound.AudioEntryFlags.IsEmbedded | UndertaleSound.AudioEntryFlags.IsCompressed | UndertaleSound.AudioEntryFlags.Regular)
                : (UndertaleSound.AudioEntryFlags.IsEmbedded | UndertaleSound.AudioEntryFlags.Regular);

            if (existingSound == null)
            {
                UndertaleSound newSound = new UndertaleSound()
                {
                    Name = Data.Strings.MakeString(soundName),
                    Type = Data.Strings.MakeString(isOGG ? ".ogg" : ".wav"),
                    File = Data.Strings.MakeString(filename),
                    Flags = newFlags,
                    Effects = sndEffects,
                    Volume = sndVolume,
                    Pitch = sndPitch,
                    AudioID = audioID,
                    AudioFile = isExternalGroup ? null : soundData,
                    GroupID = targetGroupID,
                    AudioGroup = Data.AudioGroups[targetGroupID]
                };
                Data.Sounds.Add(newSound);
            }
            else
            {
                existingSound.AudioID = audioID;
                existingSound.AudioFile = isExternalGroup ? null : soundData;
                existingSound.File = Data.Strings.MakeString(filename);
                existingSound.Type = Data.Strings.MakeString(isOGG ? ".ogg" : ".wav");
                existingSound.Flags = newFlags;
                existingSound.Volume = sndVolume;
                existingSound.Pitch = sndPitch;
                existingSound.Effects = sndEffects;
                existingSound.GroupID = targetGroupID;
                existingSound.AudioGroup = Data.AudioGroups[targetGroupID];
            }
            Console.WriteLine($"[UTMT-IMPORT] Imported sound: {soundName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UTMT-IMPORT] Error importing sound file '{file}': {ex.Message}");
        }
    }
}

/* SPRITES, BG, FONTS */

Console.WriteLine($"[UTMT-IMPORT] Phase: Graphics");

Dictionary<string, JObject> spriteMetadataCache = new Dictionary<string, JObject>();
Dictionary<string, JObject> bgMetadataCache = new Dictionary<string, JObject>();
Dictionary<string, JObject> fontMetadataCache = new Dictionary<string, JObject>();

void LoadMetadataCache(string folderPath, Dictionary<string, JObject> cache)
{
    if (!Directory.Exists(folderPath)) return;
    foreach (string dir in Directory.GetDirectories(folderPath))
    {
        try
        {
            string metaPath = Path.Combine(dir, "metadata.json");
            if (File.Exists(metaPath))
            {
                try
                {
                    JObject meta = JObject.Parse(File.ReadAllText(metaPath));
                    string name = (string)meta["Name"];
                    if (!string.IsNullOrEmpty(name))
                        cache[name] = meta;
                }
                catch
                {
                    Console.WriteLine($"[UTMT-IMPORT] Warning: Failed to parse metadata.json in '{dir}'. Skipping metadata cache for this entry.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UTMT-IMPORT] Error loading metadata from '{dir}': {ex.Message}");
        }
    }
}

string dirSprites = GetFolderCI(repoDir, "sprites");
if (!string.IsNullOrEmpty(dirSprites)) LoadMetadataCache(dirSprites, spriteMetadataCache);

string dirBackgrounds = GetFolderCI(repoDir, "backgrounds");
if (!string.IsNullOrEmpty(dirBackgrounds)) LoadMetadataCache(dirBackgrounds, bgMetadataCache);

string dirFonts = GetFolderCI(repoDir, "fonts");
if (!string.IsNullOrEmpty(dirFonts)) LoadMetadataCache(dirFonts, fontMetadataCache);

List<TextureInfo> sourceTextures = new List<TextureInfo>();

void ScanGraphicsFolder(string folderPath, SpriteType type, Dictionary<string, JObject> metaCache)
{
    if (!Directory.Exists(folderPath)) return;

    foreach (string file in Directory.GetFiles(folderPath, "*.png", SearchOption.AllDirectories))
    {
        try
        {
            MagickImage img = new MagickImage(file);
            TextureInfo ti = new TextureInfo
            {
                Source = file,
                Width = (int)img.Width,
                Height = (int)img.Height,
                Image = img,
                SType = type,
                Name = Path.GetFileNameWithoutExtension(file)
            };

            string parentDir = Path.GetDirectoryName(file);
            string parentName = Path.GetFileName(parentDir);
            JObject parentMeta = null;
            metaCache?.TryGetValue(parentName, out parentMeta);

            if (type != SpriteType.Background)
            {
                int metaTargetX = -1, metaTargetY = -1;
                int metaBoundingWidth = -1, metaBoundingHeight = -1;
                int metaTargetWidth = -1, metaTargetHeight = -1;

                if (type == SpriteType.Sprite && parentMeta != null)
                {
                    JArray tpiArray = parentMeta["TexturePageItems"] as JArray;
                    if (tpiArray != null)
                    {
                        int lastUnderscore = ti.Name.LastIndexOf('_');
                        int frameIdx = -1;
                        if (lastUnderscore > 0 && int.TryParse(ti.Name.Substring(lastUnderscore + 1), out frameIdx))
                        {
                            if (frameIdx >= 0 && frameIdx < tpiArray.Count && tpiArray[frameIdx] != null && tpiArray[frameIdx].Type != JTokenType.Null)
                            {
                                JObject tpiObj = tpiArray[frameIdx] as JObject;
                                if (tpiObj != null)
                                {
                                    metaTargetX = (int?)tpiObj["TargetX"] ?? -1;
                                    metaTargetY = (int?)tpiObj["TargetY"] ?? -1;
                                    metaTargetWidth = (int?)tpiObj["TargetWidth"] ?? -1;
                                    metaTargetHeight = (int?)tpiObj["TargetHeight"] ?? -1;
                                    metaBoundingWidth = (int?)tpiObj["BoundingWidth"] ?? -1;
                                    metaBoundingHeight = (int?)tpiObj["BoundingHeight"] ?? -1;
                                }
                            }
                        }
                    }
                }
                else if (type == SpriteType.Font && parentMeta != null)
                {
                    JObject tpiObj = parentMeta["TexturePageItem"] as JObject;
                    if (tpiObj != null)
                    {
                        metaTargetX = (int?)tpiObj["TargetX"] ?? -1;
                        metaTargetY = (int?)tpiObj["TargetY"] ?? -1;
                        metaTargetWidth = (int?)tpiObj["TargetWidth"] ?? -1;
                        metaTargetHeight = (int?)tpiObj["TargetHeight"] ?? -1;
                        metaBoundingWidth = (int?)tpiObj["BoundingWidth"] ?? -1;
                        metaBoundingHeight = (int?)tpiObj["BoundingHeight"] ?? -1;
                    }
                }

                if (metaTargetX >= 0 && metaTargetY >= 0 && metaBoundingWidth > 0 && metaBoundingHeight > 0)
                {
                    ti.TargetX = metaTargetX;
                    ti.TargetY = metaTargetY;
                    ti.BoundingWidth = metaBoundingWidth;
                    ti.BoundingHeight = metaBoundingHeight;

                    if (metaTargetWidth > 0 && metaTargetHeight > 0)
                    {
                        img.Trim();
                        img.ResetPage();
                        if ((int)img.Width != metaTargetWidth || (int)img.Height != metaTargetHeight)
                        {
                            img.InterpolativeResize((uint)metaTargetWidth, (uint)metaTargetHeight, PixelInterpolateMethod.Bilinear);
                        }
                        ti.Width = metaTargetWidth;
                        ti.Height = metaTargetHeight;
                    }
                    else
                    {
                        img.BorderColor = MagickColors.Transparent;
                        img.BackgroundColor = MagickColors.Transparent;
                        img.Border(1);
                        IMagickGeometry bbox = img.BoundingBox;
                        if (bbox != null)
                        {
                            img.Trim();
                        }
                        else
                        {
                            img.Crop(1, 1);
                        }
                        img.ResetPage();
                        ti.Width = (int)img.Width;
                        ti.Height = (int)img.Height;
                    }
                }
                else
                {
                    img.BorderColor = MagickColors.Transparent;
                    img.BackgroundColor = MagickColors.Transparent;
                    img.Border(1);
                    IMagickGeometry bbox = img.BoundingBox;
                    if (bbox != null)
                    {
                        ti.TargetX = bbox.X - 1; ti.TargetY = bbox.Y - 1;
                        img.Trim();
                    }
                    else
                    {
                        ti.TargetX = 0; ti.TargetY = 0; img.Crop(1, 1);
                    }
                    img.ResetPage();
                    ti.Width = (int)img.Width; ti.Height = (int)img.Height;
                    ti.BoundingWidth = ti.TargetX + ti.Width;
                    ti.BoundingHeight = ti.TargetY + ti.Height;
                }
            }
            else
            {
                JObject tpiObj = parentMeta?["TexturePageItem"] as JObject;
                if (tpiObj != null)
                {
                    ti.TargetX = (int?)tpiObj["TargetX"] ?? 0;
                    ti.TargetY = (int?)tpiObj["TargetY"] ?? 0;
                    ti.BoundingWidth = (int?)tpiObj["BoundingWidth"] ?? ti.Width;
                    ti.BoundingHeight = (int?)tpiObj["BoundingHeight"] ?? ti.Height;
                }
                else
                {
                    ti.TargetX = 0;
                    ti.TargetY = 0;
                    ti.BoundingWidth = ti.Width;
                    ti.BoundingHeight = ti.Height;
                }
            }
            sourceTextures.Add(ti);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UTMT-IMPORT] Error processing image file '{file}': {ex.Message}");
        }
    }
}

if (!string.IsNullOrEmpty(dirSprites)) ScanGraphicsFolder(dirSprites, SpriteType.Sprite, spriteMetadataCache);
if (!string.IsNullOrEmpty(dirBackgrounds)) ScanGraphicsFolder(dirBackgrounds, SpriteType.Background, bgMetadataCache);
if (!string.IsNullOrEmpty(dirFonts)) ScanGraphicsFolder(dirFonts, SpriteType.Font, fontMetadataCache);

if (sourceTextures.Count > 0)
{
    Console.WriteLine($"[UTMT-IMPORT] Packing {sourceTextures.Count} textures...");
    Packer packer = new Packer { SourceTextures = sourceTextures, AtlasSize = 2048, Padding = 2 };
    packer.Process();

    int lastTextPage = Data.EmbeddedTextures.Count - 1;
    int lastTextPageItem = Data.TexturePageItems.Count - 1;

    foreach (Atlas atlas in packer.Atlasses)
    {
        try
        {
            UndertaleEmbeddedTexture embTex = new UndertaleEmbeddedTexture { Name = Data.Strings.MakeString($"Texture {++lastTextPage}") };

            using (MagickImage atlasImg = new MagickImage(MagickColors.Transparent, (uint)atlas.Width, (uint)atlas.Height))
            {
                foreach (Node n in atlas.Nodes)
                {
                    if (n.Texture == null) continue;
                    using (IMagickImage<byte> resized = TextureWorker.ResizeImage(n.Texture.Image, n.Bounds.Width, n.Bounds.Height))
                    {
                        atlasImg.Composite(resized, n.Bounds.X, n.Bounds.Y, CompositeOperator.Copy);
                    }
                }

                embTex.TextureData.Image = GMImage.FromMagickImage(atlasImg).ConvertToPng();
                Data.EmbeddedTextures.Add(embTex);

                if (Data.TextureGroupInfo != null && Data.TextureGroupInfo.Count > 0)
                {
                    Data.TextureGroupInfo[0].TexturePages.Add(new UndertaleResourceById<UndertaleEmbeddedTexture, UndertaleChunkTXTR>(embTex));
                }

                foreach (Node n in atlas.Nodes)
                {
                    if (n.Texture == null) continue;

                    UndertaleTexturePageItem tpi = new UndertaleTexturePageItem
                    {
                        Name = Data.Strings.MakeString($"PageItem {++lastTextPageItem}"),
                        SourceX = (ushort)n.Bounds.X,
                        SourceY = (ushort)n.Bounds.Y,
                        SourceWidth = (ushort)n.Bounds.Width,
                        SourceHeight = (ushort)n.Bounds.Height,
                        TargetX = (ushort)n.Texture.TargetX,
                        TargetY = (ushort)n.Texture.TargetY,
                        TargetWidth = (ushort)n.Bounds.Width,
                        TargetHeight = (ushort)n.Bounds.Height,
                        BoundingWidth = (ushort)n.Texture.BoundingWidth,
                        BoundingHeight = (ushort)n.Texture.BoundingHeight,
                        TexturePage = embTex
                    };
                    Data.TexturePageItems.Add(tpi);

                    if (n.Texture.SType == SpriteType.Background)
                    {
                        UndertaleBackground bg = Data.Backgrounds.ByName(n.Texture.Name);
                        if (bg == null) { bg = new UndertaleBackground { Name = Data.Strings.MakeString(n.Texture.Name) }; Data.Backgrounds.Add(bg); }
                        bg.Texture = tpi;
                    }
                    else if (n.Texture.SType == SpriteType.Font)
                    {
                        UndertaleFont fnt = Data.Fonts.ByName(n.Texture.Name);
                        if (fnt == null) { fnt = new UndertaleFont { Name = Data.Strings.MakeString(n.Texture.Name) }; Data.Fonts.Add(fnt); }
                        fnt.Texture = tpi;

                        string csvPath = Path.Combine(dirFonts, $"glyphs_{n.Texture.Name}.csv");
                        if (File.Exists(csvPath))
                        {
                            string[] lines = File.ReadAllLines(csvPath);
                            if (lines.Length >= 2)
                            {
                                string[] header = lines[0].Split(';');
                                fnt.DisplayName = Data.Strings.MakeString(header[0]);
                                fnt.EmSize = uint.Parse(header[1]);
                                fnt.Bold = bool.Parse(header[2]);
                                fnt.Italic = bool.Parse(header[3]);
                                fnt.RangeStart = ushort.Parse(lines[1]);

                                fnt.Glyphs.Clear();
                                for (int lineIdx = 2; lineIdx < lines.Length; lineIdx++)
                                {
                                    string[] gData = lines[lineIdx].Split(';');
                                    fnt.Glyphs.Add(new UndertaleFont.Glyph
                                    {
                                        Character = ushort.Parse(gData[0]),
                                        SourceX = ushort.Parse(gData[1]),
                                        SourceY = ushort.Parse(gData[2]),
                                        SourceWidth = ushort.Parse(gData[3]),
                                        SourceHeight = ushort.Parse(gData[4]),
                                        Shift = short.Parse(gData[5]),
                                        Offset = short.Parse(gData[6])
                                    });
                                }
                            }
                        }
                    }
                    else if (n.Texture.SType == SpriteType.Sprite)
                    {
                        int lastUnderscore = n.Texture.Name.LastIndexOf('_');
                        if (lastUnderscore > 0)
                        {
                            string sprName = n.Texture.Name.Substring(0, lastUnderscore);
                            if (int.TryParse(n.Texture.Name.Substring(lastUnderscore + 1), out int frame))
                            {
                                UndertaleSprite spr = Data.Sprites.ByName(sprName);
                                if (spr == null) { spr = new UndertaleSprite { Name = Data.Strings.MakeString(sprName) }; Data.Sprites.Add(spr); }

                                while (spr.Textures.Count <= frame)
                                {
                                    spr.Textures.Add(new UndertaleSprite.TextureEntry());
                                }

                                if (spr.Textures[frame] == null)
                                {
                                    spr.Textures.RemoveAt(frame);
                                    spr.Textures.Insert(frame, new UndertaleSprite.TextureEntry { Texture = tpi });
                                }
                                else
                                {
                                    spr.Textures[frame].Texture = tpi;
                                }

                                string sourceDir = Path.GetDirectoryName(n.Texture.Source);
                                string metaPath = Path.Combine(sourceDir, "metadata.json");

                                if (File.Exists(metaPath))
                                {
                                    JObject meta = JObject.Parse(File.ReadAllText(metaPath));
                                    spr.Width = (uint)meta["Width"];
                                    spr.Height = (uint)meta["Height"];
                                    spr.MarginLeft = (int)meta["MarginLeft"];
                                    spr.MarginRight = (int)meta["MarginRight"];
                                    spr.MarginBottom = (int)meta["MarginBottom"];
                                    spr.MarginTop = (int)meta["MarginTop"];
                                    spr.BBoxMode = (uint)meta["BBoxMode"];
                                    spr.SepMasks = (UndertaleSprite.SepMaskType)(int)meta["SepMasks"];
                                    spr.OriginX = (int)meta["OriginX"];
                                    spr.OriginY = (int)meta["OriginY"];
                                    spr.IsSpecialType = (bool)meta["IsSpecialType"];
                                    spr.SVersion = (uint)meta["SVersion"];
                                    spr.GMS2PlaybackSpeed = (float)meta["GMS2PlaybackSpeed"];
                                    spr.GMS2PlaybackSpeedType = (AnimSpeedType)(int)meta["GMS2PlaybackSpeedType"];
                                }
                                else
                                {
                                    spr.Width = Math.Max(spr.Width, tpi.BoundingWidth);
                                    spr.Height = Math.Max(spr.Height, tpi.BoundingHeight);
                                }
                                Console.WriteLine($"[UTMT-IMPORT] Imported sprite: {sprName} frame {frame}");
                            }
                        }
                    }

                    n.Texture.Image.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UTMT-IMPORT] Error processing atlas: {ex.Message}");
        }
    }
}

/* OBJECTS */
Console.WriteLine($"[UTMT-IMPORT] Phase: Objects");
string objectsDir = GetFolderCI(repoDir, "objects");
if (!string.IsNullOrEmpty(objectsDir))
{
    string[] jsonFiles = Directory.GetFiles(objectsDir, "*.json");
    List<JObject> parsedObjects = new List<JObject>();

    foreach (string file in jsonFiles)
        parsedObjects.Add(JObject.Parse(File.ReadAllText(file)));

    for (int i = 0; i < parsedObjects.Count; i++)
    {
        JObject jsonObj = parsedObjects[i];
        string objName = (string)jsonObj["Name"];
        UndertaleGameObject obj = Data.GameObjects.ByName(objName);
        if (obj == null)
        {
            obj = new UndertaleGameObject { Name = Data.Strings.MakeString(objName) };
            Data.GameObjects.Add(obj);
        }

        obj.Visible = (bool?)jsonObj["Visible"] ?? true;
        obj.Solid = (bool?)jsonObj["Solid"] ?? false;
        obj.Depth = (int?)jsonObj["Depth"] ?? 0;
        obj.Persistent = (bool?)jsonObj["Persistent"] ?? false;

        obj.UsesPhysics = (bool?)jsonObj["UsesPhysics"] ?? false;
        obj.IsSensor = (bool?)jsonObj["IsSensor"] ?? false;
        obj.CollisionShape = (CollisionShapeFlags?)((uint?)jsonObj["CollisionShape"]) ?? CollisionShapeFlags.Box;
        obj.Density = (float?)jsonObj["Density"] ?? 0.5f;
        obj.Restitution = (float?)jsonObj["Restitution"] ?? 0.1f;
        obj.Group = (uint?)jsonObj["Group"] ?? 0u;
        obj.LinearDamping = (float?)jsonObj["LinearDamping"] ?? 0.1f;
        obj.AngularDamping = (float?)jsonObj["AngularDamping"] ?? 0.1f;
        obj.Friction = (float?)jsonObj["Friction"] ?? 0.2f;
        obj.Awake = (bool?)jsonObj["Awake"] ?? true;
        obj.Kinematic = (bool?)jsonObj["Kinematic"] ?? false;
        Console.WriteLine($"[UTMT-IMPORT] Object shell: {objName}");
    }

    for (int i = 0; i < parsedObjects.Count; i++)
    {
        JObject jsonObj = parsedObjects[i];
        UndertaleGameObject obj = Data.GameObjects.ByName((string)jsonObj["Name"]);

        JToken sprToken = jsonObj["Sprite"];
        if (sprToken != null && sprToken.Type != JTokenType.Null)
            obj.Sprite = Data.Sprites.ByName(sprToken.ToString());
        else
            obj.Sprite = null;

        JToken maskToken = jsonObj["TextureMask"];
        if (maskToken != null && maskToken.Type != JTokenType.Null)
            obj.TextureMaskId = Data.Sprites.ByName(maskToken.ToString());
        else
            obj.TextureMaskId = null;

        JToken parentToken = jsonObj["Parent"];
        if (parentToken != null && parentToken.Type != JTokenType.Null)
            obj.ParentId = Data.GameObjects.ByName(parentToken.ToString());
        else
            obj.ParentId = null;
        Console.WriteLine($"[UTMT-IMPORT] Object pointers: {(string)jsonObj["Name"]}");
    }
}

/* ROOMS */
Console.WriteLine($"[UTMT-IMPORT] Phase: Rooms");
string roomsDir = GetFolderCI(repoDir, "rooms");
if (!string.IsNullOrEmpty(roomsDir))
{
    string[] roomDirs = Directory.GetDirectories(roomsDir);
    Console.WriteLine($"[UTMT-IMPORT] Found {roomDirs.Length} room directories");

    foreach (string roomDirPath in roomDirs)
    {
        try
        {
            string metaPath = Path.Combine(roomDirPath, "metadata.json");
            if (!File.Exists(metaPath)) continue;

            JObject roomJson = JObject.Parse(File.ReadAllText(metaPath));
            string roomName = (string)roomJson["Name"];
            if (string.IsNullOrEmpty(roomName)) continue;

            UndertaleRoom room = Data.Rooms.ByName(roomName);
            if (room == null)
            {
                room = new UndertaleRoom { Name = Data.Strings.MakeString(roomName) };
                Data.Rooms.Add(room);
            }

            room.Caption = (roomJson["Caption"] != null && roomJson["Caption"].Type != JTokenType.Null)
                ? Data.Strings.MakeString((string)roomJson["Caption"]) : null;
            room.Width = (uint?)roomJson["Width"] ?? 320;
            room.Height = (uint?)roomJson["Height"] ?? 240;
            room.Speed = (uint?)roomJson["Speed"] ?? 30;
            room.Persistent = (bool?)roomJson["Persistent"] ?? false;
            room.BackgroundColor = (uint?)roomJson["BackgroundColor"] ?? 0;
            room.DrawBackgroundColor = (bool?)roomJson["DrawBackgroundColor"] ?? true;

            string creationCodeName = (string)roomJson["CreationCodeId"];
            if (!string.IsNullOrEmpty(creationCodeName))
                room.CreationCodeId = Data.Code.ByName(creationCodeName);
            else
                room.CreationCodeId = null;

            room.Flags = (UndertaleRoom.RoomEntryFlags)((uint?)roomJson["Flags"] ?? 1);
            room.World = (bool?)roomJson["World"] ?? false;
            room.Top = (uint?)roomJson["Top"] ?? 0;
            room.Left = (uint?)roomJson["Left"] ?? 0;
            room.Right = (uint?)roomJson["Right"] ?? 1024;
            room.Bottom = (uint?)roomJson["Bottom"] ?? 768;
            room.GravityX = (float?)roomJson["GravityX"] ?? 0;
            room.GravityY = (float?)roomJson["GravityY"] ?? 10;
            room.MetersPerPixel = (float?)roomJson["MetersPerPixel"] ?? 0.1f;

            room.Backgrounds.Clear();
            JArray bgArray = roomJson["Backgrounds"] as JArray;
            if (bgArray != null)
            {
                foreach (JToken bgToken in bgArray)
                {
                    var bgEntry = new UndertaleRoom.Background();
                    bgEntry.Enabled = (bool?)bgToken["Enabled"] ?? false;
                    bgEntry.Foreground = (bool?)bgToken["Foreground"] ?? false;
                    string bgDefName = (string)bgToken["BackgroundDefinition"];
                    bgEntry.BackgroundDefinition = !string.IsNullOrEmpty(bgDefName) ? Data.Backgrounds.ByName(bgDefName) : null;
                    bgEntry.X = (int?)bgToken["X"] ?? 0;
                    bgEntry.Y = (int?)bgToken["Y"] ?? 0;
                    bgEntry.TiledHorizontally = (bool?)bgToken["TiledHorizontally"] ?? false;
                    bgEntry.TiledVertically = (bool?)bgToken["TiledVertically"] ?? false;
                    bgEntry.SpeedX = (int?)bgToken["SpeedX"] ?? 0;
                    bgEntry.SpeedY = (int?)bgToken["SpeedY"] ?? 0;
                    bgEntry.Stretch = (bool?)bgToken["Stretch"] ?? false;
                    bgEntry.ParentRoom = room;
                    room.Backgrounds.Add(bgEntry);
                }
            }

            room.Views.Clear();
            JArray viewArray = roomJson["Views"] as JArray;
            if (viewArray != null)
            {
                foreach (JToken viewToken in viewArray)
                {
                    var viewEntry = new UndertaleRoom.View();
                    viewEntry.Enabled = (bool?)viewToken["Enabled"] ?? false;
                    viewEntry.ViewX = (int?)viewToken["ViewX"] ?? 0;
                    viewEntry.ViewY = (int?)viewToken["ViewY"] ?? 0;
                    viewEntry.ViewWidth = (int?)viewToken["ViewWidth"] ?? 640;
                    viewEntry.ViewHeight = (int?)viewToken["ViewHeight"] ?? 480;
                    viewEntry.PortX = (int?)viewToken["PortX"] ?? 0;
                    viewEntry.PortY = (int?)viewToken["PortY"] ?? 0;
                    viewEntry.PortWidth = (int?)viewToken["PortWidth"] ?? 640;
                    viewEntry.PortHeight = (int?)viewToken["PortHeight"] ?? 480;
                    viewEntry.BorderX = (uint?)viewToken["BorderX"] ?? 32;
                    viewEntry.BorderY = (uint?)viewToken["BorderY"] ?? 32;
                    viewEntry.SpeedX = (int?)viewToken["SpeedX"] ?? -1;
                    viewEntry.SpeedY = (int?)viewToken["SpeedY"] ?? -1;
                    string viewObjName = (string)viewToken["ObjectId"];
                    viewEntry.ObjectId = !string.IsNullOrEmpty(viewObjName) ? Data.GameObjects.ByName(viewObjName) : null;
                    room.Views.Add(viewEntry);
                }
            }

            room.GameObjects.Clear();
            JArray objArray = roomJson["GameObjects"] as JArray;
            if (objArray != null)
            {
                foreach (JToken objToken in objArray)
                {
                    var gameObj = new UndertaleRoom.GameObject();
                    gameObj.X = (int?)objToken["X"] ?? 0;
                    gameObj.Y = (int?)objToken["Y"] ?? 0;
                    string objDefName = (string)objToken["ObjectDefinition"];
                    gameObj.ObjectDefinition = !string.IsNullOrEmpty(objDefName) ? Data.GameObjects.ByName(objDefName) : null;
                    gameObj.InstanceID = (uint?)objToken["InstanceID"] ?? 0;
                    string ccName = (string)objToken["CreationCode"];
                    gameObj.CreationCode = !string.IsNullOrEmpty(ccName) ? Data.Code.ByName(ccName) : null;
                    gameObj.ScaleX = (float?)objToken["ScaleX"] ?? 1;
                    gameObj.ScaleY = (float?)objToken["ScaleY"] ?? 1;
                    gameObj.Color = (uint?)objToken["Color"] ?? 0xFFFFFFFF;
                    gameObj.Rotation = (float?)objToken["Rotation"] ?? 0;
                    string preCreateName = (string)objToken["PreCreateCode"];
                    gameObj.PreCreateCode = !string.IsNullOrEmpty(preCreateName) ? Data.Code.ByName(preCreateName) : null;
                    gameObj.ImageSpeed = (float?)objToken["ImageSpeed"] ?? 0;
                    gameObj.ImageIndex = (int?)objToken["ImageIndex"] ?? 0;
                    room.GameObjects.Add(gameObj);
                }
            }

            room.Tiles.Clear();
            JArray tileArray = roomJson["Tiles"] as JArray;
            if (tileArray != null)
            {
                foreach (JToken tileToken in tileArray)
                {
                    var tile = new UndertaleRoom.Tile();
                    tile.X = (int?)tileToken["X"] ?? 0;
                    tile.Y = (int?)tileToken["Y"] ?? 0;
                    tile.spriteMode = (bool?)tileToken["SpriteMode"] ?? false;
                    if (tile.spriteMode)
                    {
                        string sprDefName = (string)tileToken["SpriteDefinition"];
                        tile.SpriteDefinition = !string.IsNullOrEmpty(sprDefName) ? Data.Sprites.ByName(sprDefName) : null;
                    }
                    else
                    {
                        string bgDefName2 = (string)tileToken["BackgroundDefinition"];
                        tile.BackgroundDefinition = !string.IsNullOrEmpty(bgDefName2) ? Data.Backgrounds.ByName(bgDefName2) : null;
                    }
                    tile.SourceX = (int?)tileToken["SourceX"] ?? 0;
                    tile.SourceY = (int?)tileToken["SourceY"] ?? 0;
                    tile.Width = (uint?)tileToken["Width"] ?? 0;
                    tile.Height = (uint?)tileToken["Height"] ?? 0;
                    tile.TileDepth = (int?)tileToken["TileDepth"] ?? 0;
                    tile.InstanceID = (uint?)tileToken["InstanceID"] ?? 0;
                    tile.ScaleX = (float?)tileToken["ScaleX"] ?? 1;
                    tile.ScaleY = (float?)tileToken["ScaleY"] ?? 1;
                    tile.Color = (uint?)tileToken["Color"] ?? 0xFFFFFFFF;
                    room.Tiles.Add(tile);
                }
            }

            room.Layers.Clear();
            JArray layerArray = roomJson["Layers"] as JArray;
            if (layerArray != null)
            {
                foreach (JToken layerToken in layerArray)
                {
                    var layer = new UndertaleRoom.Layer();
                    layer.ParentRoom = room;
                    layer.LayerName = Data.Strings.MakeString((string)layerToken["LayerName"]);
                    layer.LayerId = (uint?)layerToken["LayerId"] ?? 0;
                    layer.LayerType = (UndertaleRoom.LayerType)((int?)layerToken["LayerType"] ?? 0);
                    layer.LayerDepth = (int?)layerToken["LayerDepth"] ?? 0;
                    layer.XOffset = (float?)layerToken["XOffset"] ?? 0;
                    layer.YOffset = (float?)layerToken["YOffset"] ?? 0;
                    layer.HSpeed = (float?)layerToken["HSpeed"] ?? 0;
                    layer.VSpeed = (float?)layerToken["VSpeed"] ?? 0;
                    layer.IsVisible = (bool?)layerToken["IsVisible"] ?? true;
                    layer.EffectEnabled = (bool?)layerToken["EffectEnabled"] ?? false;
                    layer.EffectType = (layerToken["EffectType"] != null && layerToken["EffectType"].Type != JTokenType.Null)
                        ? Data.Strings.MakeString((string)layerToken["EffectType"]) : null;

                    layer.EffectProperties.Clear();
                    JArray effectPropsArr = layerToken["EffectProperties"] as JArray;
                    if (effectPropsArr != null)
                    {
                        foreach (JToken propToken in effectPropsArr)
                        {
                            var prop = new UndertaleRoom.EffectProperty();
                            prop.Name = Data.Strings.MakeString((string)propToken["Name"]);
                            prop.Value = Data.Strings.MakeString((string)propToken["Value"]);
                            layer.EffectProperties.Add(prop);
                        }
                    }

                    JToken instDataToken = layerToken["InstancesData"];
                    if (instDataToken != null)
                    {
                        var instData = new UndertaleRoom.Layer.LayerInstancesData();
                        JArray instIdsArr = instDataToken["InstanceIds"] as JArray;
                        if (instIdsArr != null)
                        {
                            var instanceIds = new List<uint>();
                            foreach (var idToken in instIdsArr)
                                instanceIds.Add((uint)idToken);

                            instData.InstanceIds = instanceIds.ToArray();
                            instData.Instances.Clear();
                            foreach (uint instId in instanceIds)
                            {
                                var roomObj = room.GameObjects.FirstOrDefault(g => g.InstanceID == instId);
                                if (roomObj != null)
                                    instData.Instances.Add(roomObj);
                            }
                        }
                        layer.Data = instData;
                    }

                    JToken tilesDataToken = layerToken["TilesData"];
                    if (tilesDataToken != null)
                    {
                        var tilesData = new UndertaleRoom.Layer.LayerTilesData();
                        tilesData.ParentLayer = layer;
                        string tilesBgName = (string)tilesDataToken["Background"];
                        tilesData.Background = !string.IsNullOrEmpty(tilesBgName) ? Data.Backgrounds.ByName(tilesBgName) : null;
                        tilesData.TilesX = (uint?)tilesDataToken["TilesX"] ?? 0;
                        tilesData.TilesY = (uint?)tilesDataToken["TilesY"] ?? 0;

                        JArray tileDataArr = tilesDataToken["TileData"] as JArray;
                        if (tileDataArr != null)
                        {
                            uint[][] tileData = new uint[tilesData.TilesY][];
                            for (int y = 0; y < tilesData.TilesY; y++)
                            {
                                tileData[y] = new uint[tilesData.TilesX];
                                if (y < tileDataArr.Count)
                                {
                                    JArray rowArr = tileDataArr[y] as JArray;
                                    if (rowArr != null)
                                    {
                                        for (int x = 0; x < Math.Min(tilesData.TilesX, (uint)rowArr.Count); x++)
                                            tileData[y][x] = (uint)rowArr[x];
                                    }
                                }
                            }
                            tilesData.TileData = tileData;
                        }
                        layer.Data = tilesData;
                    }

                    JToken bgDataToken = layerToken["BackgroundData"];
                    if (bgDataToken != null)
                    {
                        var bgData = new UndertaleRoom.Layer.LayerBackgroundData();
                        bgData.ParentLayer = layer;
                        bgData.Visible = (bool?)bgDataToken["Visible"] ?? true;
                        bgData.Foreground = (bool?)bgDataToken["Foreground"] ?? false;
                        string bgSpriteName = (string)bgDataToken["Sprite"];
                        bgData.Sprite = !string.IsNullOrEmpty(bgSpriteName) ? Data.Sprites.ByName(bgSpriteName) : null;
                        bgData.TiledHorizontally = (bool?)bgDataToken["TiledHorizontally"] ?? false;
                        bgData.TiledVertically = (bool?)bgDataToken["TiledVertically"] ?? false;
                        bgData.Stretch = (bool?)bgDataToken["Stretch"] ?? false;
                        bgData.Color = (uint?)bgDataToken["Color"] ?? 0xFF000000;
                        bgData.FirstFrame = (float?)bgDataToken["FirstFrame"] ?? 0;
                        bgData.AnimationSpeed = (float?)bgDataToken["AnimationSpeed"] ?? 0;
                        bgData.AnimationSpeedType = (AnimSpeedType)((int?)bgDataToken["AnimationSpeedType"] ?? 0);
                        layer.Data = bgData;
                    }

                    JToken assetsDataToken = layerToken["AssetsData"];
                    if (assetsDataToken != null)
                    {
                        var assetsData = new UndertaleRoom.Layer.LayerAssetsData();

                        assetsData.LegacyTiles = new UndertalePointerList<UndertaleRoom.Tile>();
                        JArray legacyTilesArr = assetsDataToken["LegacyTiles"] as JArray;
                        if (legacyTilesArr != null)
                        {
                            foreach (JToken ltToken in legacyTilesArr)
                            {
                                var lt = new UndertaleRoom.Tile();
                                lt.X = (int?)ltToken["X"] ?? 0;
                                lt.Y = (int?)ltToken["Y"] ?? 0;
                                lt.spriteMode = (bool?)ltToken["SpriteMode"] ?? false;
                                if (lt.spriteMode)
                                {
                                    string ltSprName = (string)ltToken["SpriteDefinition"];
                                    lt.SpriteDefinition = !string.IsNullOrEmpty(ltSprName) ? Data.Sprites.ByName(ltSprName) : null;
                                }
                                else
                                {
                                    string ltBgName = (string)ltToken["BackgroundDefinition"];
                                    lt.BackgroundDefinition = !string.IsNullOrEmpty(ltBgName) ? Data.Backgrounds.ByName(ltBgName) : null;
                                }
                                lt.SourceX = (int?)ltToken["SourceX"] ?? 0;
                                lt.SourceY = (int?)ltToken["SourceY"] ?? 0;
                                lt.Width = (uint?)ltToken["Width"] ?? 0;
                                lt.Height = (uint?)ltToken["Height"] ?? 0;
                                lt.TileDepth = (int?)ltToken["TileDepth"] ?? 0;
                                lt.InstanceID = (uint?)ltToken["InstanceID"] ?? 0;
                                lt.ScaleX = (float?)ltToken["ScaleX"] ?? 1;
                                lt.ScaleY = (float?)ltToken["ScaleY"] ?? 1;
                                lt.Color = (uint?)ltToken["Color"] ?? 0xFFFFFFFF;
                                assetsData.LegacyTiles.Add(lt);
                            }
                        }

                        assetsData.Sprites = new UndertalePointerList<UndertaleRoom.SpriteInstance>();
                        JArray sprInstArr = assetsDataToken["Sprites"] as JArray;
                        if (sprInstArr != null)
                        {
                            foreach (JToken siToken in sprInstArr)
                            {
                                var si = new UndertaleRoom.SpriteInstance();
                                si.Name = Data.Strings.MakeString((string)siToken["Name"]);
                                string siSprName = (string)siToken["Sprite"];
                                si.Sprite = !string.IsNullOrEmpty(siSprName) ? Data.Sprites.ByName(siSprName) : null;
                                si.X = (int?)siToken["X"] ?? 0;
                                si.Y = (int?)siToken["Y"] ?? 0;
                                si.ScaleX = (float?)siToken["ScaleX"] ?? 1;
                                si.ScaleY = (float?)siToken["ScaleY"] ?? 1;
                                si.Color = (uint?)siToken["Color"] ?? 0xFFFFFFFF;
                                si.AnimationSpeed = (float?)siToken["AnimationSpeed"] ?? 0;
                                si.AnimationSpeedType = (AnimSpeedType)((int?)siToken["AnimationSpeedType"] ?? 0);
                                si.FrameIndex = (float?)siToken["FrameIndex"] ?? 0;
                                si.Rotation = (float?)siToken["Rotation"] ?? 0;
                                assetsData.Sprites.Add(si);
                            }
                        }

                        assetsData.Sequences = new UndertalePointerList<UndertaleRoom.SequenceInstance>();
                        JArray seqInstArr = assetsDataToken["Sequences"] as JArray;
                        if (seqInstArr != null)
                        {
                            foreach (JToken seqiToken in seqInstArr)
                            {
                                var seqi = new UndertaleRoom.SequenceInstance();
                                seqi.Name = Data.Strings.MakeString((string)seqiToken["Name"]);
                                string seqResName = (string)seqiToken["Sequence"];
                                seqi.Sequence = !string.IsNullOrEmpty(seqResName) ? Data.Sequences.ByName(seqResName) : null;
                                seqi.X = (int?)seqiToken["X"] ?? 0;
                                seqi.Y = (int?)seqiToken["Y"] ?? 0;
                                seqi.ScaleX = (float?)seqiToken["ScaleX"] ?? 1;
                                seqi.ScaleY = (float?)seqiToken["ScaleY"] ?? 1;
                                seqi.Color = (uint?)seqiToken["Color"] ?? 0xFFFFFFFF;
                                seqi.AnimationSpeed = (float?)seqiToken["AnimationSpeed"] ?? 0;
                                seqi.AnimationSpeedType = (AnimSpeedType)((int?)seqiToken["AnimationSpeedType"] ?? 0);
                                seqi.FrameIndex = (float?)seqiToken["FrameIndex"] ?? 0;
                                seqi.Rotation = (float?)seqiToken["Rotation"] ?? 0;
                                assetsData.Sequences.Add(seqi);
                            }
                        }

                        layer.Data = assetsData;
                    }

                    JToken effectDataToken = layerToken["EffectData"];
                    if (effectDataToken != null && layer.LayerType == UndertaleRoom.LayerType.Effect)
                    {
                        var effectData = new UndertaleRoom.Layer.LayerEffectData();
                        effectData.EffectType = (effectDataToken["EffectType"] != null && effectDataToken["EffectType"].Type != JTokenType.Null)
                            ? Data.Strings.MakeString((string)effectDataToken["EffectType"]) : null;
                        layer.Data = effectData;
                    }

                    room.Layers.Add(layer);
                }
            }

            room.Sequences.Clear();
            JArray seqArray = roomJson["Sequences"] as JArray;
            if (seqArray != null)
            {
                foreach (JToken seqToken in seqArray)
                {
                    string seqName = (string)seqToken;
                    if (!string.IsNullOrEmpty(seqName))
                    {
                        var seq = Data.Sequences.ByName(seqName);
                        if (seq != null)
                        {
                            var seqRef = new UndertaleResourceById<UndertaleSequence, UndertaleChunkSEQN>();
                            seqRef.Resource = seq;
                            room.Sequences.Add(seqRef);
                        }
                    }
                }
            }

            Console.WriteLine($"[UTMT-IMPORT] Imported room: {roomName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UTMT-IMPORT] Error importing room from '{roomDirPath}': {ex.Message}");
        }
    }
}

/* CODE */
Console.WriteLine($"[UTMT-IMPORT] Phase: Code");
string codeDir = GetFolderCI(repoDir, "code");
if (!string.IsNullOrEmpty(codeDir))
{
    string[] gmlFiles = Directory.GetFiles(codeDir, "*.gml");
    Console.WriteLine($"[UTMT-IMPORT] Found {gmlFiles.Length} GML files");

    CodeImportGroup importGroup = new CodeImportGroup(Data) { AutoCreateAssets = true };
    for (int i = 0; i < gmlFiles.Length; i++)
    {
        string code = File.ReadAllText(gmlFiles[i]);
        string codeName = Path.GetFileNameWithoutExtension(gmlFiles[i]);
        importGroup.QueueReplace(codeName, code);
        Console.WriteLine($"[UTMT-IMPORT] Queued: {codeName}");
    }

    Console.WriteLine($"[UTMT-IMPORT] Compiling and linking GML code...");
    importGroup.Import();
}

Console.WriteLine("[UTMT-IMPORT] Import complete! All assets integrated successfully.");
ScriptMessage("Import complete! All assets integrated successfully.");

/* INTERNALS */
public enum SpriteType { Sprite, Background, Font }
public class TextureInfo { public string Source; public string Name; public int Width; public int Height; public int TargetX; public int TargetY; public int BoundingWidth; public int BoundingHeight; public SpriteType SType; public MagickImage Image; }
public class Rect { public int X; public int Y; public int Width; public int Height; public int Right { get { return X + Width; } } public int Down { get { return Y + Height; } } public int Area { get { return Width * Height; } } }
public class Split : Rect
{
    public bool Invalidated = false;
    public Split(int x, int y, int w, int h) { X = x; Y = y; Width = w; Height = h; }
    public bool containsRect(Rect r) { return (r.X >= X) && (r.Y >= Y) && (Right >= r.Right) && (Down >= r.Down); }
    public bool overlapsRect(Rect r) { return (((r.X >= X) && (r.X <= Right)) || ((X >= r.X) && (X <= r.Right))) && (((r.Y >= Y) && (r.Y <= Down)) || ((Y >= r.Y) && (Y <= r.Down))); }
    public bool fits(int w, int h) { return (Width >= w) && (Height >= h); }
    public IEnumerable<Split> splitNode(Rect r)
    {
        if (!overlapsRect(r) || Invalidated) return new List<Split>();
        Invalidated = true;
        return new List<Split> {
            new Split(X, Y, Width, r.Y - Y), new Split(X, Y, r.X - X, Height),
            new Split(X, r.Down, Width, Down - r.Down), new Split(r.Right, Y, Right - r.Right, Height),
        }.Where(item => item.Area > 0);
    }
}
public class Node { public Rect Bounds; public TextureInfo Texture; }
public class Atlas { public int Width; public int Height; public List<Node> Nodes = new List<Node>(); }
public class Packer
{
    public List<TextureInfo> SourceTextures; public int Padding; public int AtlasSize; public List<Atlas> Atlasses = new List<Atlas>();
    public void Process()
    {
        List<TextureInfo> textures = SourceTextures.OrderByDescending(t => t.Width * t.Height).ToList();
        while (textures.Count > 0)
        {
            Atlas atlas = new Atlas { Width = AtlasSize, Height = AtlasSize };
            List<Split> splits = new List<Split> { new Split(0, 0, AtlasSize, AtlasSize) };
            List<TextureInfo> leftovers = new List<TextureInfo>();

            foreach (var tex in textures)
            {
                int pW = tex.Width + (Padding * 2); int pH = tex.Height + (Padding * 2);
                Split bestFit = splits.Where(s => s.fits(pW, pH)).OrderBy(s => Math.Max(s.Width - pW, s.Height - pH)).FirstOrDefault();

                if (bestFit == null) { leftovers.Add(tex); continue; }

                Rect rect = new Rect { X = bestFit.X, Y = bestFit.Y, Width = pW, Height = pH };
                var newSplits = splits.SelectMany(s => s.splitNode(rect)).ToList();
                splits = splits.Where(s => !s.Invalidated).Concat(newSplits).ToList();

                foreach (var s1 in splits) foreach (var s2 in splits) if (s1 != s2 && s1.containsRect(s2)) s2.Invalidated = true;
                splits.RemoveAll(s => s.Invalidated);

                atlas.Nodes.Add(new Node { Bounds = new Rect { X = bestFit.X + Padding, Y = bestFit.Y + Padding, Width = tex.Width, Height = tex.Height }, Texture = tex });
            }
            if (atlas.Nodes.Count > 0) Atlasses.Add(atlas);
            textures = leftovers;
            if (atlas.Nodes.Count == 0 && textures.Count > 0) break;
        }
    }
}
