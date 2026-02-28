using AIDrivenFW.API;
using AIDrivenFW.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System.Threading.Tasks;

public class IAIExecutorTests
{

    [Test, Category("Core")]
    public async Task Generate_WhenCalled_ReturnsExpectedResult()
    {
        // Arrange
        var fake = new FakeExecutor
        {
            ExtractValue = "OK"
        };
        var genAI = new GenAI(fake);

        // Act
        var response = await genAI.Generate("Test input").AsTask();

        // Assert
        Assert.AreEqual("OK", response);
    }

    [Test, Category("Core")]
    public async Task Generate_WhenProcessDead_RestartsProcess()
    {
        // Arrange
        var fake = new FakeExecutor
        {
            ProcessAlive = true,
            ExtractValue = "OK"
        };

        var genAI = new GenAI(fake);

        // 1回目で正常生成
        await genAI.Generate("Test input").AsTask();

        // プロセス死亡をシミュレート
        fake.ProcessAlive = false;

        // Act
        await genAI.Generate("Test input").AsTask();

        // Assert
        Assert.IsTrue(fake.StartCalled);
    }
}
