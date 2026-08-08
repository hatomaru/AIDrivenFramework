using AIDrivenFW.Core;
using NUnit.Framework;
using System;

namespace AIDrivenFW.Tests.Unit
{
    public class ProcessArgumentParserTests
    {
        [Test]
        public void Parse_PreservesShellSyntaxAsLiteralArgumentData()
        {
            var arguments = ProcessArgumentParser.Parse("--prompt \"$(touch /tmp/pwned) `id`\"");

            CollectionAssert.AreEqual(
                new[] { "--prompt", "$(touch /tmp/pwned) `id`" },
                arguments);
        }

        [Test]
        public void Parse_PreservesQuotedPathsAndEmptyArguments()
        {
            var arguments = ProcessArgumentParser.Parse("--model '/models/my model.gguf' --prompt \"\"");

            CollectionAssert.AreEqual(
                new[] { "--model", "/models/my model.gguf", "--prompt", string.Empty },
                arguments);
        }

        [Test]
        public void Parse_WithUnterminatedQuote_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => ProcessArgumentParser.Parse("--prompt \"unfinished"));
        }
    }
}
