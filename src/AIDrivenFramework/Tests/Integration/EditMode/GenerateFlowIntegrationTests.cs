using AIDrivenFW.API;
using NUnit.Framework;
using System.Threading.Tasks;

public class GenerateFlowIntegrationTests
{
    GenAI testAI;

    [Test, Category("Integration")]
    public async Task Generate_ReturnsNonEmptyText()
    {
        testAI = new GenAI();

        var result = await testAI.Generate("Hello");

        Assert.IsFalse(string.IsNullOrEmpty(result) || result.Contains("✖"));
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        testAI?.KillProcess();
    }
}
