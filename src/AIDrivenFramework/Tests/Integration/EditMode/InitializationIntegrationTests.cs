using AIDrivenFW.API;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System.Threading.Tasks;

public class InitializationIntegrationTests
{
    [Test, Category("Integration")]
    public async Task Initialize_WithValidModel_ReturnsTrue()
    {
        bool prepared = await AIDrivenInitializer.Initialize().AsTask();

        Assert.IsTrue(prepared);
    }
}
