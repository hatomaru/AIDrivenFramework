using AIDrivenFW.API;
using NUnit.Framework;
using System.Threading.Tasks;

public class InitializationIntegrationTests
{
    [Test, Category("Integration")]
    public async Task Initialize_WithValidModel_ReturnsTrue()
    {
        bool prepared = await AIDrivenInitializer.Initialize();

        Assert.IsTrue(prepared);
    }
}
