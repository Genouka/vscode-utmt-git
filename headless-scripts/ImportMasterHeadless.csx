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
}

/* SPRITES, BG, FONTS */

Console.WriteLine($"[UTMT-IMPORT] Phase: Graphics");

List<TextureInfo> sourceTextures = new List<TextureInfo>();
void ScanGraphicsFolder(string folderPath, SpriteType type)
{
    if (!Directory.Exists(folderPath)) return;

    foreach (string file in Directory.GetFiles(folderPath, "*.png", SearchOption.AllDirectories))
    {
        MagickImage img = new MagickImage(file);
        TextureInfo ti = new TextureInfo {
            Source = file, Width = (int)img.Width, Height = (int)img.Height,
            Image = img, SType = type, Name = Path.GetFileNameWithoutExtension(file)
        };

        if (type != SpriteType.Background)
        {
            img.BorderColor = MagickColors.Transparent;
            img.BackgroundColor = MagickColors.Transparent;
            img.Border(1);
            IMagickGeometry bbox = img.BoundingBox;
            if (bbox != null) {
                ti.TargetX = bbox.X - 1; ti.TargetY = bbox.Y - 1;
                img.Trim();
            } else {
                ti.TargetX = 0; ti.TargetY = 0; img.Crop(1, 1);
            }
            img.ResetPage();
            ti.Width = (int)img.Width; ti.Height = (int)img.Height;
        }
        sourceTextures.Add(ti);
    }
}

string dirSprites = GetFolderCI(repoDir, "sprites");
if (!string.IsNullOrEmpty(dirSprites)) ScanGraphicsFolder(dirSprites, SpriteType.Sprite);

string dirBackgrounds = GetFolderCI(repoDir, "backgrounds");
if (!string.IsNullOrEmpty(dirBackgrounds)) ScanGraphicsFolder(dirBackgrounds, SpriteType.Background);

string dirFonts = GetFolderCI(repoDir, "fonts");
if (!string.IsNullOrEmpty(dirFonts)) ScanGraphicsFolder(dirFonts, SpriteType.Font);

if (sourceTextures.Count > 0)
{
    Console.WriteLine($"[UTMT-IMPORT] Packing {sourceTextures.Count} textures...");
    Packer packer = new Packer { SourceTextures = sourceTextures, AtlasSize = 2048, Padding = 2 };
    packer.Process();

    int lastTextPage = Data.EmbeddedTextures.Count - 1;
    int lastTextPageItem = Data.TexturePageItems.Count - 1;

    foreach (Atlas atlas in packer.Atlasses)
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
                    SourceX = (ushort)n.Bounds.X, SourceY = (ushort)n.Bounds.Y,
                    SourceWidth = (ushort)n.Bounds.Width, SourceHeight = (ushort)n.Bounds.Height,
                    TargetX = (ushort)n.Texture.TargetX, TargetY = (ushort)n.Texture.TargetY,
                    TargetWidth = (ushort)n.Bounds.Width, TargetHeight = (ushort)n.Bounds.Height,
                    BoundingWidth = (ushort)(n.Texture.TargetX + n.Bounds.Width),
                    BoundingHeight = (ushort)(n.Texture.TargetY + n.Bounds.Height),
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
                                fnt.Glyphs.Add(new UndertaleFont.Glyph {
                                    Character = ushort.Parse(gData[0]),
                                    SourceX = ushort.Parse(gData[1]), SourceY = ushort.Parse(gData[2]),
                                    SourceWidth = ushort.Parse(gData[3]), SourceHeight = ushort.Parse(gData[4]),
                                    Shift = short.Parse(gData[5]), Offset = short.Parse(gData[6])
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
public class TextureInfo { public string Source; public string Name; public int Width; public int Height; public int TargetX; public int TargetY; public SpriteType SType; public MagickImage Image; }
public class Rect { public int X; public int Y; public int Width; public int Height; public int Right { get { return X + Width; } } public int Down { get { return Y + Height; } } public int Area { get { return Width * Height; } } }
public class Split : Rect {
    public bool Invalidated = false;
    public Split(int x, int y, int w, int h) { X = x; Y = y; Width = w; Height = h; }
    public bool containsRect(Rect r) { return (r.X >= X) && (r.Y >= Y) && (Right >= r.Right) && (Down >= r.Down); }
    public bool overlapsRect(Rect r) { return (((r.X >= X) && (r.X <= Right)) || ((X >= r.X) && (X <= r.Right))) && (((r.Y >= Y) && (r.Y <= Down)) || ((Y >= r.Y) && (Y <= r.Down))); }
    public bool fits(int w, int h) { return (Width >= w) && (Height >= h); }
    public IEnumerable<Split> splitNode(Rect r) {
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
public class Packer {
    public List<TextureInfo> SourceTextures; public int Padding; public int AtlasSize; public List<Atlas> Atlasses = new List<Atlas>();
    public void Process() {
        List<TextureInfo> textures = SourceTextures.OrderByDescending(t => t.Width * t.Height).ToList();
        while (textures.Count > 0) {
            Atlas atlas = new Atlas { Width = AtlasSize, Height = AtlasSize };
            List<Split> splits = new List<Split> { new Split(0, 0, AtlasSize, AtlasSize) };
            List<TextureInfo> leftovers = new List<TextureInfo>();

            foreach (var tex in textures) {
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
