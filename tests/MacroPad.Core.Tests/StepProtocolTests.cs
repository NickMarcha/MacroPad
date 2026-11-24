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
    public void Model_lays_out_keys_as_on_the_pad()
    {
        var model = PadModel.For(0x514C, 0x8850);
        Assert.Equal(PacketFormat.Steps, model.Format);
        // Top row typed d h l, bottom row a e i.
        Assert.Equal([4, 8, 12, 3, 7, 11, 2, 6, 10, 1, 5, 9], model.KeySlots(12));
        Assert.Equal([1, 2, 3], PadModel.For(0x514C, 0x8851).KeySlots(3));
        Assert.Equal(PacketFormat.Chords, PadModel.For(0x1189, 0x8850).Format);
    }

    [Fact]
    public void Slot_count_covers_every_knob()
    {
        Assert.Equal(18, Protocol.SlotCount(1));
        Assert.Equal(21, Protocol.SlotCount(2));
    }
}
