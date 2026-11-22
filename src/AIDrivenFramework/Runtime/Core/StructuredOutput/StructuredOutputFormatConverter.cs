using System;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// JSON/YAMLのJSON Schemaソースを、各LLMランタイムの構造化出力形式へ変換する公開窓口。
    /// </summary>
    public static class StructuredOutputFormatConverter
    {

        public static T ConvertResponse<T>(string response, Func<string, T> converter,
            Func<string, string> preprocess)
        {
            try
            {
                string json = preprocess == null ? response : preprocess(response);
                StructuredOutputJson.Parse(json);
                return converter(json);
            }
            catch (Exception ex) when (ex is not OperationCanceledException &&
                ex is not StructuredOutputResponseException)
            {
                throw new StructuredOutputResponseException("The structured response could not be converted.", ex);
            }
        }

        /// <summary>Converts a JSON response to a YAML document.</summary>
        public static string ToYaml(string json)
        {
            try
            {
                return StructuredOutputYaml.Serialize(StructuredOutputJson.Parse(json));
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
            {
                throw new StructuredOutputResponseException("The response is not valid JSON.", ex);
            }
        }

        /// <summary>
        /// JSONまたはYAMLのSchemaソースをllama.cpp用GBNFへ変換する。
        /// </summary>
        public static string ToLlamaCppGbnf(StructuredOutputOptions options)
        {
            StructuredOutputDefinition definition = NormalizeRequired(options);
            return ToLlamaCppGbnf(definition);
        }

        /// <summary>
        /// JSONまたはYAMLのSchemaソースをOllamaのformatフィールドへ埋め込めるJSONへ変換する。
        /// </summary>
        /// <remarks>
        /// 戻り値はJSON文字列ですが、Ollamaへの送信時は文字列値ではなくJSONオブジェクトとして埋め込みます。
        /// </remarks>
        public static string ToOllamaFormatJson(StructuredOutputOptions options)
        {
            StructuredOutputDefinition definition = NormalizeRequired(options);
            return ToOllamaFormatJson(definition);
        }

        internal static string ToLlamaCppGbnf(StructuredOutputDefinition definition)
        {
            return definition == null
                ? null
                : JsonSchemaToGbnfConverter.Convert(definition.JsonSchema);
        }

        internal static string ToOllamaFormatJson(StructuredOutputDefinition definition)
        {
            return definition?.JsonSchema;
        }

        private static StructuredOutputDefinition NormalizeRequired(StructuredOutputOptions options)
        {
            if (options == null)
            {
                throw new GenAIConfigurationException("Structured-output options are required for format conversion.");
            }

            return new StructuredOutputSchemaNormalizer().Normalize(options);
        }
    }
}
