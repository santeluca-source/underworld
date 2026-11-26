using NUnit.Framework;
using Newtonsoft.Json;

namespace Underworld.Tests
{
    /// <summary>Controlla che la catena dei test (Unity Test Framework + Newtonsoft) funzioni.</summary>
    public class SmokeTests
    {
        class Sample { public long Cash; public string Name; }

        [Test]
        public void Newtonsoft_RoundTrip_KeepsLongValues()
        {
            var json = JsonConvert.SerializeObject(new Sample { Cash = 9_000_000_000L, Name = "test" });
            var back = JsonConvert.DeserializeObject<Sample>(json);
            Assert.AreEqual(9_000_000_000L, back.Cash);
            Assert.AreEqual("test", back.Name);
        }
    }
}
