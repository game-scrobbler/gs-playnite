using GsPlugin.Services;
using Xunit;

namespace GsPlugin.Tests {
    public class GsAllowedPluginsTests {
        [Theory]
        [InlineData("GOG OSS")]
        [InlineData("Legendary")]
        [InlineData("Epic Games")]
        [InlineData("Epic Games Store")]
        [InlineData("Amazon Games")]
        [InlineData("Steam Library")]
        public void IsRecognizedSourceName_KnownStores_ReturnsTrue(string sourceName) {
            Assert.True(GsAllowedPlugins.IsRecognizedSourceName(sourceName));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Custom Shelf")]
        [InlineData("Manual")]
        [InlineData("Glitch")]
        [InlineData("Original")]
        [InlineData("Steampunk")]
        public void IsRecognizedSourceName_UnknownSources_ReturnsFalse(string sourceName) {
            Assert.False(GsAllowedPlugins.IsRecognizedSourceName(sourceName));
        }
    }
}
