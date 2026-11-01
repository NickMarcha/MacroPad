#:project ../../src/MacroPad.Core/MacroPad.Core.csproj
// Writes the demo settings the README screenshots use: generic rooms and audio served by fake_services.py.
// Usage: dotnet run demo-settings.cs -- <output settings.json>
using MacroPad.Core;

var s = new AppSettings
{
    HomeAssistantUrl = "http://localhost:8123/", Token = "demo",
    BragiUrl = "http://localhost:8765/", BragiKey = "demo",
    ActiveProfile = "Default",
};
ushort Code(int slot) => Triggers.Code(1, slot);
Target Area(string id, string name) => new(TargetKind.Area, id, name);
var office = Area("office", "Office");
s.Default.SelectedTarget = office;
s.SetAction(Code(1), new(PadCommand.Select, office));
s.SetAction(Code(2), new(PadCommand.Select, Area("living_room", "Living Room")));
s.SetAction(Code(3), new(PadCommand.Select, new Target(TargetKind.Audio, "headset/desk-headset/playback", "Desk Headset · Speakers")));
s.SetAction(Code(Protocol.KnobSlot(0, 0)), new(PadCommand.Down, Target.Selected, 5));
s.SetAction(Code(Protocol.KnobSlot(0, 1)), new(PadCommand.Toggle, Target.Selected));
s.SetAction(Code(Protocol.KnobSlot(0, 2)), new(PadCommand.Up, Target.Selected, 5));
var music = new Profile { Name = "Music" };
music.Actions[Triggers.Key(Code(1))] = new(PadCommand.Select, new Target(TargetKind.Audio, "peer/studio/incoming", "studio to headset"));
s.Profiles.Add(music);
s.Save(args[0]);
Console.WriteLine($"demo settings written to {args[0]}");
