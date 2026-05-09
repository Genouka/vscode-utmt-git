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

string outputDir = Path.Combine(projectRoot, "objects");
Directory.CreateDirectory(outputDir);

Console.WriteLine($"[UTMT-EXPORT-OBJECTS] Exporting all objects to: {outputDir}");
Console.WriteLine($"[UTMT-EXPORT-OBJECTS] Total objects: {Data.GameObjects.Count}");

int exported = 0;
foreach (var obj in Data.GameObjects)
{
    try
    {
        if (obj == null || obj.Name == null) continue;

        var eventsList = new List<object>();
        for (int i = 0; i < obj.Events.Count; i++)
        {
            var subEvents = obj.Events[i];
            if (subEvents != null && subEvents.Count > 0)
            {
                foreach (var ev in subEvents)
                {
                    string codeName = null;

                    var action = ev.Actions.FirstOrDefault();
                    if (action != null && action.CodeId != null)
                    {
                        codeName = action.CodeId.Name?.Content;
                    }

                    eventsList.Add(new
                    {
                        EventType = ((EventType)i).ToString(),
                        EventSubtype = ev.EventSubtype,
                        CodeName = codeName
                    });
                }
            }
        }

        var jsonObject = new
        {
            Name = obj.Name?.Content,
            Sprite = obj.Sprite?.Name?.Content,
            Visible = obj.Visible,
            Solid = obj.Solid,
            Depth = obj.Depth,
            Persistent = obj.Persistent,
            Parent = obj.ParentId?.Name?.Content,
            TextureMask = obj.TextureMaskId?.Name?.Content,
            UsesPhysics = obj.UsesPhysics,
            IsSensor = obj.IsSensor,
            CollisionShape = (int)obj.CollisionShape,
            Density = obj.Density,
            Restitution = obj.Restitution,
            Group = obj.Group,
            LinearDamping = obj.LinearDamping,
            AngularDamping = obj.AngularDamping,
            Friction = obj.Friction,
            Awake = obj.Awake,
            Kinematic = obj.Kinematic,
            Events = eventsList
        };

        string jsonOutput = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
        string filePath = Path.Combine(outputDir, $"{obj.Name.Content}.json");
        File.WriteAllText(filePath, jsonOutput);

        exported++;
        Console.WriteLine($"[UTMT-EXPORT-OBJECTS] Exported: {obj.Name.Content}");
    }
    catch (Exception e)
    {
        Console.WriteLine($"[UTMT-EXPORT-OBJECTS] Error exporting object: {obj?.Name?.Content}. Exception: {e.Message}");
    }
}

Console.WriteLine($"[UTMT-EXPORT-OBJECTS] Complete! Exported {exported} objects.");
