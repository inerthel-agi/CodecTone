using AudioConverter.Core.Remix;
using AudioConverter.Infrastructure.Storage;

namespace AudioConverter.Infrastructure.Tests;

[TestClass]
public sealed class UserPresetStoreTests
{
    [TestMethod]
    public void SaveAndLoad_RoundTripsPresetsAndToleratesCorruption()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new UserPresetStore(root);
            Assert.AreEqual(0, store.Load().Count);

            store.Save([new UserRemixPreset("Night drive", [new UserRemixEffect(RemixEffectKind.Reverb, true, 0.3, 2.5, 0.7), new UserRemixEffect(RemixEffectKind.Bass, false, 6, 90, 0)])]);
            var loaded = store.Load().Single();
            Assert.AreEqual("Night drive", loaded.Name);
            CollectionAssert.AreEqual(new[] { RemixEffectKind.Reverb, RemixEffectKind.Bass }, loaded.Effects.Select(effect => effect.Kind).ToArray());
            Assert.AreEqual(2.5, loaded.Effects[0].Second);
            Assert.IsFalse(loaded.Effects[1].Enabled);

            File.WriteAllText(Path.Combine(root, "remix-presets.json"), "{ not json");
            Assert.AreEqual(0, store.Load().Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void ValidateName_TrimsAndRejectsEmptyOrLongNames()
    {
        Assert.AreEqual("Lo-fi tape", UserRemixPreset.ValidateName("  Lo-fi tape ").Name);
        Assert.IsNotNull(UserRemixPreset.ValidateName("   ").Error);
        Assert.IsNotNull(UserRemixPreset.ValidateName(new string('x', 41)).Error);
    }
}
