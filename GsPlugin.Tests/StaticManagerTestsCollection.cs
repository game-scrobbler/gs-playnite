using Xunit;

namespace GsPlugin.Tests {
    /// <summary>
    /// Tests in this collection mutate static singleton state and must not run
    /// in parallel with other test collections.
    /// </summary>
    [CollectionDefinition("StaticManagerTests", DisableParallelization = true)]
    public class StaticManagerTestsCollection {
    }
}
