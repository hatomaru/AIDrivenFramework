using AIDrivenFW.Core;
using NUnit.Framework;
using System.Threading.Tasks;

namespace AIDrivenFW.Tests.Unit
{
    public class StructuredOutputTests
    {
        private const string JsonSchema =
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"score\":{\"type\":\"integer\"}},\"required\":[\"name\"]}";

        [Test]
        public void Normalize_JsonSchema_ReturnsCanonicalJson()
        {
            var normalizer = new StructuredOutputSchemaNormalizer();

            StructuredOutputDefinition result = normalizer.Normalize(StructuredOutputOptions.FromJson(JsonSchema));

            Assert.AreEqual(JsonSchema, result.JsonSchema);
        }

        [Test]
        public void Normalize_YamlSchema_ConvertsNestedMappingsAndSequencesToJson()
        {
            const string yaml = @"
type: object
properties:
  name:
    type: string
  score:
    type: integer
required: [name]
";
            var normalizer = new StructuredOutputSchemaNormalizer();

            StructuredOutputDefinition result = normalizer.Normalize(StructuredOutputOptions.FromYaml(yaml));

            Assert.AreEqual(JsonSchema, result.JsonSchema);
        }

        [Test]
        public void Normalize_InvalidYaml_ThrowsConfigurationException()
        {
            var normalizer = new StructuredOutputSchemaNormalizer();

            Assert.Throws<GenAIConfigurationException>(() =>
                normalizer.Normalize(StructuredOutputOptions.FromYaml("type: object\n  invalid: indentation")));
        }

        [Test]
        public async Task GenerateAsync_StructuredOutput_IsNormalizedBeforeExecutorStarts()
        {
            const string yaml = "type: object\nproperties:\n  answer:\n    type: string\nrequired: [answer]";
            var executor = new FakeAIExecutor("fake", "{\"answer\":\"ok\"}");
            var core = new GenAICore(executor);

            string result = await core.GenerateAsync(
                "answer",
                structuredOutput: StructuredOutputOptions.FromYaml(yaml)).AsTask();

            Assert.AreEqual("{\"answer\":\"ok\"}", result);
            Assert.AreEqual(1, executor.ConfigureStructuredOutputCallCount);
            Assert.AreEqual(
                "{\"type\":\"object\",\"properties\":{\"answer\":{\"type\":\"string\"}},\"required\":[\"answer\"]}",
                executor.LastStructuredOutput.JsonSchema);
            Assert.AreEqual(1, executor.StartProcessCallCount, "A process-bound grammar change must restart the executor.");
        }

        [Test]
        public void JsonSchemaToGbnf_ObjectSchema_ProducesRootAndPropertyRules()
        {
            string grammar = JsonSchemaToGbnfConverter.Convert(JsonSchema);

            StringAssert.StartsWith("root ::= schema", grammar);
            StringAssert.Contains("json-string", grammar);
            StringAssert.Contains("json-integer", grammar);
            StringAssert.Contains("\\\"name\\\"", grammar);
        }

        [Test]
        public void OllamaPayload_StructuredOutput_EmbedsSchemaAsFormatObject()
        {
            var definition = new StructuredOutputSchemaNormalizer().Normalize(StructuredOutputOptions.FromJson(JsonSchema));

            string payload = OllamaHTTPExecutor.BuildRequestJson("model", "prompt", "system", false, definition);

            StringAssert.Contains("\"format\":{" + "\"type\":\"object\"", payload);
            Assert.IsFalse(payload.Contains("\"format\":\"{"), "Ollama format must be a JSON object, not a quoted schema string.");
        }

        [Test]
        public void LlamaPayload_StructuredOutput_SendsConvertedGrammar()
        {
            string grammar = JsonSchemaToGbnfConverter.Convert(JsonSchema);
            var messages = new[] { new Message { role = "user", content = "prompt" } };

            string payload = LlamaHTTPExecutor.BuildRequestJson(messages, false, grammar);

            StringAssert.Contains("\"grammar\":", payload);
            StringAssert.Contains("root ::= schema", payload);
        }
    }
}
