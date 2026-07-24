using System.Runtime.Serialization;
using Dc.SimHarness.Engine;
using Xunit;
namespace Engine.Tests;
public class NameProbeTests
{
    [Fact]
    public void DirectNameRoundTrip()
    {
        GameBootstrap.EnsureInitialized();
        var loc = typeof(UnityEngine.Object).Assembly.Location;
        var card = (Card)FormatterServices.GetUninitializedObject(typeof(Card));
        card.name = "Zzz";
        System.Console.WriteLine($"CoreModule loaded from: {loc}");
        System.Console.WriteLine($"card.name after set = '{card.name}'");
        Assert.Equal("Zzz", card.name);
    }
}
