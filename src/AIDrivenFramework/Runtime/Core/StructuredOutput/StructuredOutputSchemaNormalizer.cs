using System;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// JSON/YAMLで記述されたJSON SchemaをExecutor共通のJSONへ正規化する。
    /// </summary>
    public sealed class StructuredOutputSchemaNormalizer
    {
        public StructuredOutputDefinition Normalize(StructuredOutputOptions options)
        {
            if (options == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(options.schema))
            {
                throw new GenAIConfigurationException("A structured-output schema is required.");
            }

            try
            {
                object schema = options.sourceFormat switch
                {
                    SchemaSourceFormat.Json => StructuredOutputJson.Parse(options.schema),
                    SchemaSourceFormat.Yaml => StructuredOutputYaml.Parse(options.schema),
                    _ => throw new ArgumentOutOfRangeException(nameof(options.sourceFormat), options.sourceFormat, "Unsupported schema source format.")
                };

                if (schema is not System.Collections.Generic.IDictionary<string, object>)
                {
                    throw new FormatException("The JSON Schema root must be an object.");
                }

                return new StructuredOutputDefinition(StructuredOutputJson.Serialize(schema));
            }
            catch (GenAIConfigurationException)
            {
                throw;
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
            {
                throw new GenAIConfigurationException($"The structured-output schema is invalid: {ex.Message}", ex);
            }
        }
    }
}
