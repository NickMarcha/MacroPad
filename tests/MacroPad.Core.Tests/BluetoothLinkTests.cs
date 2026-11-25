using MacroPad.Core;

namespace MacroPad.Core.Tests;

public class BluetoothLinkTests
{
    const string Address = "a1b2c3d4e5f6";

    static AppSettings With(params string[] keys)
    {
        var s = new AppSettings();
        foreach (var k in keys) s.PadFor(k);
        return s;
    }

    [Theory]
    [InlineData("serial:514C:8850:0123456789ABCDEF", "514C:8850")]
    [InlineData(@"path:\?\hid#vid_514c&pid_8851&mi_00#7&1", "514C:8851")]
    [InlineData("bluetooth:a1b2c3d4e5f6", null)]
    public void Model_comes_from_the_key(string key, string? model) => Assert.Equal(model, new PadRecord { Key = key }.Model);

    [Fact]
    public void Links_to_the_only_pad_of_a_Bluetooth_model_and_remembers_it()
    {
        var s = With("serial:514C:8851:A", "serial:514C:8850:B");
        var links = Pads.LinkBluetooth(s, [Address], out var changed);
        Assert.True(changed);
        Assert.Equal("serial:514C:8850:B", links[Address].Key);
        Assert.Equal(Address, links[Address].Bluetooth);

        Pads.LinkBluetooth(s, [Address], out changed);
        Assert.False(changed);
        Assert.Equal(2, s.Pads.Count);
    }

    [Fact]
    public void Gets_a_pad_of_its_own_when_it_is_unclear_which_pad_it_is()
    {
        var s = With("serial:514C:8850:A", "serial:514C:8850:B");
        var links = Pads.LinkBluetooth(s, [Address], out _);
        Assert.Equal($"bluetooth:{Address}", links[Address].Key);
        Assert.Equal(3, s.Pads.Count);

        var none = With("serial:514C:8851:A");
        Assert.Equal($"bluetooth:{Address}", Pads.LinkBluetooth(none, [Address], out _)[Address].Key);
    }
}
