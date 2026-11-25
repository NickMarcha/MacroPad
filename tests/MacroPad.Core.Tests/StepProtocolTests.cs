using MacroPad.Core;

namespace MacroPad.Core.Tests;

public class StepProtocolTests
{
    // Reports captured from a 514C:8850 pad (12 keys, 2 knobs).
    static byte[] Report(string hex)
    {
        var r = new byte[64];
        Convert.FromHexString(hex.Replace(" ", "")).CopyTo(r, 0);
        return r;
    }

    [Fact]
    public void Parses_factory_default()
    {
        var (layer, slot, b) = StepProtocol.ParseSlot(Report("03 fa 01 03 01 01 01 00 00 04"));
        Assert.Equal((3, 1), (layer, slot));
        Assert.Equal(new KeyboardBinding([new KeyChord(Modifiers.None, 0x04)]), b);
    }

    [Fact]
    public void Write_matches_packets_verified_on_hardware()
    {
        // Written to layer 3 key 1, read back in the same layout, then restored.
        var chord = StepProtocol.WriteBinding(3, 1, new KeyboardBinding([new KeyChord(Modifiers.LeftCtrl | Modifiers.LeftShift, 0x73)]));
        Assert.Equal(Convert.FromHexString("03FD0103010003" + "0000F1" + "0000F2" + "000073"), chord[..16]);
        Assert.All(chord[16..], x => Assert.Equal(0, x));

        var media = StepProtocol.WriteBinding(3, 1, new MediaBinding(0x00E9));
        Assert.Equal(Convert.FromHexString("03FD0103020002" + "0000E9" + "000000"), media[..13]);
    }

    [Fact]
    public void Ignores_bytes_past_the_step_count()
    {
        // After writing Ctrl+Shift+F24 and then Volume Up, the pad still had the old 73 in the third step.
        var (_, _, b) = StepProtocol.ParseSlot(Report("03 fa 01 03 02 00 02 00 00 e9 00 00 00 00 00 73"));
        Assert.Equal(new MediaBinding(0x00E9), b);
    }

    [Fact]
    public void Write_then_parse_round_trips()
    {
        Binding[] cases =
        [
            new KeyboardBinding([new KeyChord(Modifiers.LeftCtrl | Modifiers.LeftShift, 0x04), new KeyChord(Modifiers.None, 0x28)]),
            new KeyboardBinding([new KeyChord(Modifiers.RightAlt, 0)]),
            new MediaBinding(0x0F21),
            new MouseBinding(Modifiers.LeftAlt, MouseButtons.Right, 5, -3, 0),
            new MouseBinding(Modifiers.None, MouseButtons.None, Wheel: -1),
            new EmptyBinding(),
        ];
        foreach (var b in cases)
        {
            var p = StepProtocol.WriteBinding(2, 19, b);
            p[1] = 0xFA;
            var (layer, slot, parsed) = StepProtocol.ParseSlot(p[..64]);
            Assert.Equal((2, 19), (layer, slot));
            Assert.Equal(b, parsed);
        }
    }

    [Fact]
    public void Refuses_what_the_pad_cannot_hold()
    {
        Assert.Throws<ArgumentException>(() => StepProtocol.WriteBinding(1, 1, new KeyboardBinding([new KeyChord(0, 4)], 50)));
        Assert.Throws<ArgumentException>(() => StepProtocol.WriteBinding(1, 1, new MouseBinding(Modifiers.LeftCtrl | Modifiers.LeftShift, MouseButtons.Left)));
    }

    [Fact]
    public void Model_picks_the_format_by_vendor_and_product()
    {
        Assert.Equal(PacketFormat.Steps, PadModel.For(0x514C, 0x8850).Format);
        Assert.Equal(PacketFormat.Chords, PadModel.For(0x514C, 0x8851).Format);
        Assert.Equal(PacketFormat.Chords, PadModel.For(0x1189, 0x8850).Format);
    }

    [Fact]
    public void Layout_is_the_vendors_and_turns()
    {
        var layout = PadLayout.For(12, 2);
        // Keys 1–4 along the top, knobs on the right.
        Assert.Equal([1, 2, 3, 4], layout.Keys.Where(k => k.Y == 0).OrderBy(k => k.X).Select(k => k.Slot));
        Assert.All(layout.Knobs, n => Assert.True(n.X > layout.Keys.Max(k => k.X) + 1));

        // Upright, as it sits with the knobs at the top (three turns clockwise = one anticlockwise): the bottom row is
        // 1 5 9, which is how the pad typed a e i.
        var upright = layout.Rotated(3);
        Assert.Equal([1, 5, 9], upright.Keys.Where(k => k.Y == upright.Keys.Max(m => m.Y)).OrderBy(k => k.X).Select(k => k.Slot));
        Assert.Equal([4, 8, 12], upright.Keys.Where(k => k.Y == upright.Keys.Min(m => m.Y)).OrderBy(k => k.X).Select(k => k.Slot));
        Assert.All(upright.Knobs, n => Assert.True(n.Y + PadLayout.KnobSize <= upright.Keys.Min(k => k.Y)));
        Assert.Equal(layout.Height, upright.Width, 6);
        Assert.Equal(layout.Width, upright.Height, 6);

        Assert.Equal(layout.Keys, layout.Rotated(4).Keys);
    }

    [Fact]
    public void Lights_match_the_packets_used_on_hardware()
    {
        // Layer 1 (0 in the packet), mode 1, the factory colours: what the light test sent.
        var p = StepProtocol.WriteLights(1, KeyLights.Factory with { Mode = KeyLightMode.On });
        Assert.Equal(Convert.FromHexString("03FEB00001" + "FF0000FF8030FFFF30"), p[..14]);
        Assert.Equal(Convert.FromHexString("FFC864"), p[50..53]);
        Assert.Equal(Convert.FromHexString("03FAB002"), StepProtocol.ReadLights(3)[..4]);

        // The pad's answer to a read: 03 FA <mode> and the colours.
        var reply = new byte[64];
        Convert.FromHexString("03FA00").CopyTo(reply, 0);
        p[5..53].CopyTo(reply, 3);
        Assert.Equal(KeyLights.Factory, StepProtocol.ParseLights(reply));
    }

    [Fact]
    public void Slot_count_covers_every_knob()
    {
        Assert.Equal(18, Protocol.SlotCount(1));
        Assert.Equal(21, Protocol.SlotCount(2));
    }
}
