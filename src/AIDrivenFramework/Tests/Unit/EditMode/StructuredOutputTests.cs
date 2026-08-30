using AIDrivenFW.Core;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIDrivenFW.Tests.Unit
{
    public class StructuredOutputTests
    {
        private const string JsonSchema =
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"score\":{\"type\":\"integer\"}},\"required\":[\"name\"]}";

        private const string YamlSchema = @"
type: object
properties:
  name:
    type: string
  score:
    type: integer
required: [name]
";

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
            var normalizer = new StructuredOutputSchemaNormalizer();

            StructuredOutputDefinition result = normalizer.Normalize(StructuredOutputOptions.FromYaml(YamlSchema));

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
        public void JsonSource_ToLlamaCppFormat_ConvertsToGbnf()
        {
            string grammar = StructuredOutputFormatConverter.ToLlamaCppGbnf(
                StructuredOutputOptions.FromJson(JsonSchema));

            StringAssert.StartsWith("root ::= schema", grammar);
            StringAssert.Contains("json-string", grammar);
            StringAssert.Contains("json-integer", grammar);
            StringAssert.Contains("\\\"name\\\"", grammar);
            Assert.IsFalse(grammar.Contains(JsonSchema), "llama.cpp must receive GBNF, not the original JSON Schema.");
        }

        [Test]
        public void JsonSource_ToOllamaFormat_ConvertsToJsonObjectSource()
        {
            string formatJson = StructuredOutputFormatConverter.ToOllamaFormatJson(
                StructuredOutputOptions.FromJson(JsonSchema));

            var format = (Dictionary<string, object>)StructuredOutputJson.Parse(formatJson);
            Assert.AreEqual("object", format["type"]);
            Assert.IsInstanceOf<Dictionary<string, object>>(format["properties"]);
            Assert.AreEqual(JsonSchema, formatJson);
        }

        [Test]
        public void JsonAndYamlSources_ProduceEquivalentProviderFormats()
        {
            var json = StructuredOutputOptions.FromJson(JsonSchema);
            var yaml = StructuredOutputOptions.FromYaml(YamlSchema);

            Assert.AreEqual(
                StructuredOutputFormatConverter.ToLlamaCppGbnf(json),
                StructuredOutputFormatConverter.ToLlamaCppGbnf(yaml));
            Assert.AreEqual(
                StructuredOutputFormatConverter.ToOllamaFormatJson(json),
                StructuredOutputFormatConverter.ToOllamaFormatJson(yaml));
        }

        [Test]
        public void InvalidJsonSource_ProviderConversionThrowsConfigurationException()
        {
            var invalid = StructuredOutputOptions.FromJson("{\"type\":\"object\"");

            Assert.Throws<GenAIConfigurationException>(() =>
                StructuredOutputFormatConverter.ToLlamaCppGbnf(invalid));
            Assert.Throws<GenAIConfigurationException>(() =>
                StructuredOutputFormatConverter.ToOllamaFormatJson(invalid));
        }

        [Test]
        public void OllamaPayload_StructuredOutput_EmbedsSchemaAsFormatObject()
        {
            string formatJson = StructuredOutputFormatConverter.ToOllamaFormatJson(
                StructuredOutputOptions.FromJson(JsonSchema));

            string payload = OllamaHTTPExecutor.BuildRequestJson("model", "prompt", "system", false, formatJson);
            var payloadObject = (Dictionary<string, object>)StructuredOutputJson.Parse(payload);

            StringAssert.Contains("\"format\":{" + "\"type\":\"object\"", payload);
            Assert.IsFalse(payload.Contains("\"format\":\"{"), "Ollama format must be a JSON object, not a quoted schema string.");
            Assert.IsInstanceOf<Dictionary<string, object>>(payloadObject["format"]);
        }

        [Test]
        public void LlamaPayload_StructuredOutput_SendsConvertedGrammar()
        {
            string grammar = StructuredOutputFormatConverter.ToLlamaCppGbnf(
                StructuredOutputOptions.FromJson(JsonSchema));
            var messages = new[] { new Message { role = "user", content = "prompt" } };

            string payload = LlamaHTTPExecutor.BuildRequestJson(messages, false, grammar);
            var payloadObject = (Dictionary<string, object>)StructuredOutputJson.Parse(payload);

            StringAssert.Contains("\"grammar\":", payload);
            StringAssert.Contains("root ::= schema", payload);
            Assert.AreEqual(grammar, payloadObject["grammar"]);
        }

        [Test]
        public void LlamaCliArguments_JsonSource_AppendsConvertedGrammarAsSingleArgument()
        {
            string grammar = StructuredOutputFormatConverter.ToLlamaCppGbnf(
                StructuredOutputOptions.FromJson(JsonSchema));

            string arguments = LlamaCliExecutor.AppendGrammarArgument("-m model.gguf", grammar);
            IReadOnlyList<string> parsed = ProcessArgumentParser.Parse(arguments);
            int grammarOption = -1;
            for (int i = 0; i < parsed.Count; i++)
            {
                if (parsed[i] == "--grammar")
                {
                    grammarOption = i;
                    break;
                }
            }

            Assert.GreaterOrEqual(grammarOption, 0);
            Assert.Less(grammarOption + 1, parsed.Count);
            Assert.AreEqual(grammar, parsed[grammarOption + 1]);
        }
    }
}
