using System;
using System.IO;
using Newtonsoft.Json;
using UndertaleModLib;
using UndertaleModLib.Models;

EnsureDataLoaded();

string projectRoot = Environment.GetEnvironmentVariable("UTMT_PROJECT_ROOT");
if (string.IsNullOrEmpty(projectRoot))
{
    throw new Exception("UTMT_PROJECT_ROOT environment variable is not set.");
}

string outputDir = Path.Combine(projectRoot, "sounds");
Directory.CreateDirectory(outputDir);

Console.WriteLine($"[UTMT-EXPORT-SOUNDS] Exporting all sounds to: {outputDir}");
Console.WriteLine($"[UTMT-EXPORT-SOUNDS] Total sounds: {Data.Sounds.Count}");

int exported = 0;
foreach (var snd in Data.Sounds)
{
    if (snd == null || snd.Name == null) continue;

    string soundName = snd.Name.Content;
    string soundDir = Path.Combine(outputDir, soundName);
    Directory.CreateDirectory(soundDir);

    var metadata = new
    {
        Name = soundName,
        Flags = (uint)snd.Flags,
        Type = snd.Type?.Content,
        File = snd.File?.Content,
        Effects = snd.Effects,
        Volume = snd.Volume,
        Pitch = snd.Pitch,
        AudioGroup = snd.AudioGroup?.Name?.Content ?? "audiogroup_default"
    };

    string jsonOutput = JsonConvert.SerializeObject(metadata, Formatting.Indented);
    File.WriteAllText(Path.Combine(soundDir, "metadata.json"), jsonOutput);

    byte[] audioData = null;

    if (snd.AudioFile != null)
    {
        audioData = snd.AudioFile.Data;
    }
    else if (snd.GroupID > Data.GetBuiltinSoundGroupID())
    {
        string relativeAudioGroupPath = snd.AudioGroup?.Path?.Content ?? $"audiogroup{snd.GroupID}.dat";
        string groupFilePath = Path.Combine(Path.GetDirectoryName(FilePath), relativeAudioGroupPath);

        if (File.Exists(groupFilePath))
        {
            try
            {
                using (var stream = new FileStream(groupFilePath, FileMode.Open, FileAccess.Read))
                {
                    UndertaleData agData = UndertaleIO.Read(stream);
                    if (agData != null && snd.AudioID >= 0 && snd.AudioID < agData.EmbeddedAudio.Count)
                    {
                        audioData = agData.EmbeddedAudio[snd.AudioID].Data;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[UTMT-EXPORT-SOUNDS] Warning: Failed to extract audio from {relativeAudioGroupPath}: {e.Message}");
            }
        }
    }

    if (audioData != null)
    {
        bool isCompressed = snd.Flags.HasFlag(UndertaleSound.AudioEntryFlags.IsCompressed);
        bool isEmbedded = snd.Flags.HasFlag(UndertaleSound.AudioEntryFlags.IsEmbedded);

        string audioExt = ".ogg";
        if (isEmbedded && !isCompressed) audioExt = ".wav";

        string audioFilePath = Path.Combine(soundDir, $"{soundName}{audioExt}");
        File.WriteAllBytes(audioFilePath, audioData);
    }
    else if (!snd.Flags.HasFlag(UndertaleSound.AudioEntryFlags.IsEmbedded))
    {
        string externalFilename = snd.File?.Content;
        if (!string.IsNullOrEmpty(externalFilename))
        {
            if (!externalFilename.Contains('.')) externalFilename += ".ogg";

            string sourcePath = Path.Combine(Path.GetDirectoryName(FilePath), externalFilename);
            if (File.Exists(sourcePath))
            {
                string destPath = Path.Combine(soundDir, externalFilename);
                File.Copy(sourcePath, destPath, true);
            }
        }
    }

    exported++;
    Console.WriteLine($"[UTMT-EXPORT-SOUNDS] Exported: {soundName}");
}

Console.WriteLine($"[UTMT-EXPORT-SOUNDS] Complete! Exported {exported} sounds.");
