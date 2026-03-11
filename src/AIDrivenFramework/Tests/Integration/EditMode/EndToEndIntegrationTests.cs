using AIDrivenFW.API;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System.Threading.Tasks;

public class EndToEndIntegrationTests
{
    GenAI testAI;

    [Test, Category("E2E")]
    public async Task EndToEnd_MultipleGenerations_WorkCorrectly_LlamaCppCLI()
    {
        testAI = new GenAI(new LlamaProcessExecutor());

        bool prepared = await AIDrivenInitializer.Initialize().AsTask();

        var result1 = await testAI.Generate("Hello").AsTask();
        var result2 = await testAI.Generate("How are you?").AsTask();

        Assert.IsFalse(string.IsNullOrEmpty(result1) || result1.Contains("✖"));
        Assert.IsFalse(string.IsNullOrEmpty(result2) || result2.Contains("✖"));
    }

    [Test, Category("E2E")]
    public async Task EndToEnd_MultipleGenerations_WorkCorrectly_LlamaCppHTTP()
    {
        testAI = new GenAI(new LlamaHTTPExecutor());

        bool prepared = await AIDrivenInitializer.Initialize().AsTask();

        var result1 = await testAI.Generate("Hello").AsTask();
        var result2 = await testAI.Generate("How are you?").AsTask();

        Assert.IsFalse(string.IsNullOrEmpty(result1) || result1.Contains("✖"));
        Assert.IsFalse(string.IsNullOrEmpty(result2) || result2.Contains("✖"));
    }

    [Test, Category("E2E")]
    public async Task EndToEnd_MultipleGenerations_WorkCorrectly_OllamaHTTP()
    {
        testAI = new GenAI(new OllamaHTTPExecutor());

        bool prepared = await AIDrivenInitializer.Initialize().AsTask();

        var result1 = await testAI.Generate("Hello").AsTask();
        var result2 = await testAI.Generate("How are you?").AsTask();

        Assert.IsFalse(string.IsNullOrEmpty(result1) || result1.Contains("✖"));
        Assert.IsFalse(string.IsNullOrEmpty(result2) || result2.Contains("✖"));
    }

    [TearDown]
    public void TearDown()
    {
        testAI?.KillProcess();
    }
}
