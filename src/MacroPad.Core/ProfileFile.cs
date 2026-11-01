using System.Text.Json;
using System.Text.Json.Serialization;

namespace MacroPad.Core;

/// <summary>
/// Profiles saved to a file, to back them up or move them to another computer. Only the profiles go in: what each
/// action key does and the knob's selected target. No connection settings, tokens or keys, so a file is safe to share.
/// Targets name Home Assistant areas and lights and Bragi controls by id, which only mean something where the same
/// ones exist.
/// </summary>
public static class ProfileFile
{
    public const string Extension = ".macropad.json";
    const string FormatName = "macropad-profiles";
    const int CurrentVersion = 1;

    public sealed class Document
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = CurrentVersion;
        public List<Profile> Profiles { get; set; } = [];
    }

    public static string Write(IEnumerable<Profile> profiles) =>
        JsonSerializer.Serialize(new Document { Profiles = profiles.ToList() }, ProfileFileJson.Default.Document);

    /// <summary>Reads a file written by <see cref="Write"/>. Throws <see cref="FormatException"/> with a readable message otherwise.</summary>
    public static List<Profile> Read(string json)
    {
        Document? doc;
        try
        {
            doc = JsonSerializer.Deserialize(json, ProfileFileJson.Default.Document);
        }
        catch (JsonException e)
        {
            throw new FormatException("That isn't a MacroPad profile file.", e);
        }
        if (doc is null || doc.Format != FormatName)
            throw new FormatException("That isn't a MacroPad profile file.");
        if (doc.Version > CurrentVersion)
            throw new FormatException("That file comes from a newer MacroPad. Update MacroPad to read it.");
        if (doc.Profiles.Count == 0)
            throw new FormatException("That file has no profiles in it.");
        return doc.Profiles;
    }

    /// <summary>
    /// Adds imported profiles to the settings, renaming any whose name is taken ("Music (2)"). Returns the added ones,
    /// in order. An imported "Default" becomes an ordinary profile; the settings' own Default stays first.
    /// </summary>
    public static List<Profile> AddTo(AppSettings settings, IEnumerable<Profile> imported)
    {
        var added = new List<Profile>();
        foreach (var profile in imported)
        {
            var name = string.IsNullOrWhiteSpace(profile.Name) ? "Imported" : profile.Name.Trim();
            var unique = name;
            for (var n = 2; settings.Find(unique) is not null || unique.Equals("next", StringComparison.OrdinalIgnoreCase); n++)
                unique = $"{name} ({n})";
            profile.Name = unique;
            settings.Profiles.Add(profile);
            added.Add(profile);
        }
        return added;
    }
}

[JsonSerializable(typeof(ProfileFile.Document))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class ProfileFileJson : JsonSerializerContext;
