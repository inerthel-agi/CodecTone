using AudioConverter.Core.Models;
using AudioConverter.Infrastructure.Storage;

namespace AudioConverter.Infrastructure.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    [TestMethod]
    public async Task Preferences_RoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonSettingsStore(root);
            await store.SaveAsync(new AppSettings("Glass", null, new Dictionary<string, string> { ["Convert.Format"] = "Flac" }, "fr"));
            var loaded = await store.LoadAsync();
            Assert.AreEqual("Glass", loaded.Theme);
            Assert.AreEqual("fr", loaded.Language);
            Assert.AreEqual("Flac", loaded.Preferences!["Convert.Format"]);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task SaveAndLoad_RoundTripsSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonSettingsStore(root);
            await store.SaveAsync(new AppSettings("White", "C:\\Audio"));

            Assert.AreEqual(new AppSettings("White", "C:\\Audio"), await store.LoadAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task Load_ReturnsDefaultsForInvalidJson()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), "not json");
        try
        {
            Assert.AreEqual(new AppSettings(), await new JsonSettingsStore(root).LoadAsync());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SaveAsync_SerializesConcurrentWrites()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonSettingsStore(root);
            var writes = Enumerable.Range(1, 40)
                .Select(index => store.SaveAsync(new AppSettings(index % 2 == 0 ? "Oled" : "White", $"C:\\Audio\\{index}")));

            await Task.WhenAll(writes);

            var loaded = await store.LoadAsync();
            Assert.IsTrue(loaded.LastOutputDirectory?.StartsWith("C:\\Audio\\", StringComparison.Ordinal) == true);
            Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp").Length);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
