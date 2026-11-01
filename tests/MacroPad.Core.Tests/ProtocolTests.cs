using MacroPad.Core;

namespace MacroPad.Core.Tests;

public class ProtocolTests
{
    // Reports captured from a 514C:8851 pad (3 keys, 1 knob) answering 03 FA 12 00 <layer>.
    static byte[] Report(string hex)
    {
        var bytes = Convert.FromHexString(hex.Replace(" ", ""));
        var r = new byte[64];
        bytes.CopyTo(r, 0);
        return r;
    }

    [Fact]
    public void Parses_single_key()
    {
        var (layer, slot, b) = Protocol.ParseSlot(Report("03 fa 03 01 01 00 00 00 00 00 01 00 73"));
        Assert.Equal((1, 3), (layer, slot));
        Assert.Equal(new KeyboardBinding([new KeyChord(Modifiers.None, 0x73)]), b);
        Assert.Equal("F24", Usages.Format(b));
    }

    [Fact]
    public void Parses_mouse_click()
    {
        var (_, _, b) = Protocol.ParseSlot(Report("03 fa 01 01 03 00 00 00 00 00 04 00 01 00 00 00"));
        Assert.Equal(new MouseBinding(Modifiers.None, MouseButtons.Left), b);
    }

    [Fact]
    public void Parses_ctrl_wheel()
    {
        var (_, _, b) = Protocol.ParseSlot(Report("03 fa 02 01 03 00 00 00 00 00 04 01 00 00 00 01"));
        Assert.Equal(new MouseBinding(Modifiers.LeftCtrl, MouseButtons.None, Wheel: 1), b);
        Assert.Equal("Ctrl+Wheel Up 1", Usages.Format(b));
    }

    [Fact]
    public void Parses_wheel_down()
    {
        var (_, _, b) = Protocol.ParseSlot(Report("03 fa 10 01 03 00 00 00 00 00 04 00 00 00 00 ff"));
        Assert.Equal(new MouseBinding(Modifiers.None, MouseButtons.None, Wheel: -1), b);
    }

    [Fact]
    public void Write_matches_packet_verified_on_hardware()
    {
        // 03 FE <slot> <layer> 01 00 00 00 00 00 <count> <mod> <code>: wrote 'B' to layer 3 key 1 and read it back.
        var p = Protocol.WriteBinding(3, 1, new KeyboardBinding([new KeyChord(Modifiers.None, 0x05)]));
        Assert.Equal(65, p.Length);
        Assert.Equal(Convert.FromHexString("03FE010301000000000001000500"), p[..14]);
        Assert.All(p[14..], x => Assert.Equal(0, x));
    }

    [Fact]
    public void Write_then_parse_round_trips()
    {
        Binding[] cases =
        [
            new KeyboardBinding([new KeyChord(Modifiers.LeftCtrl | Modifiers.LeftShift, 0x04), new KeyChord(Modifiers.None, 0x28)], 50),
            new KeyboardBinding([new KeyChord(Modifiers.LeftShift, 0)]),
            new MediaBinding(0x00E9),
            new MouseBinding(Modifiers.LeftAlt, MouseButtons.Right, 5, -3, 0),
            new EmptyBinding(),
        ];
        foreach (var b in cases)
        {
            var p = Protocol.WriteBinding(2, 17, b);
            // The device answers with the same layout, under the read command.
            p[1] = 0xFA;
            var (layer, slot, parsed) = Protocol.ParseSlot(p[..64]);
            Assert.Equal((2, 17), (layer, slot));
            Assert.Equal(b, parsed);
        }
    }

    [Fact]
    public void Led_packet()
    {
        var p = Protocol.WriteLed(1, LedMode.Backlight, LedColor.Blue);
        Assert.Equal(Convert.FromHexString("03FEB00108000000000001006" + "1"), p[..13]);
    }

    [Fact]
    public void Knob_slots_follow_buttons()
    {
        Assert.Equal(16, Protocol.KnobSlot(0, 0));
        Assert.Equal(18, Protocol.KnobSlot(0, 2));
        Assert.Equal(1, Protocol.ButtonSlot(0));
    }
}
