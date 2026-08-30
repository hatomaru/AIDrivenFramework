using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// JSON Schemaをllama.cppのgrammarパラメーターで利用できるGBNFへ変換する。
    /// </summary>
    public static class JsonSchemaToGbnfConverter
    {
        public static string Convert(string jsonSchema)
        {
            if (string.IsNullOrWhiteSpace(jsonSchema))
            {
                throw new GenAIConfigurationException("A JSON Schema is required for GBNF conversion.");
            }

            try
            {
                object parsed = StructuredOutputJson.Parse(jsonSchema);
                if (parsed is not Dictionary<string, object> rootSchema)
                {
                    throw new FormatException("The JSON Schema root must be an object.");
                }
                return new Converter(rootSchema).Build();
            }
            catch (GenAIConfigurationException)
            {
                throw;
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
            {
                throw new GenAIConfigurationException($"The JSON Schema could not be converted to GBNF: {ex.Message}", ex);
            }
        }

        private sealed class Converter
        {
            private const int MaxOptionalProperties = 12;
            private readonly Dictionary<string, object> rootSchema;
            private readonly Dictionary<string, string> rules = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, string> referenceRules = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly HashSet<string> reservedNames = new HashSet<string>(StringComparer.Ordinal);
            private int anonymousRuleIndex;

            internal Converter(Dictionary<string, object> rootSchema)
            {
                this.rootSchema = rootSchema;
            }

            internal string Build()
            {
                AddCommonRules();
                string schemaRule = BuildSchemaRule(rootSchema, "schema");

                var builder = new StringBuilder();
                builder.Append("root ::= ").Append(schemaRule).AppendLine();
                foreach (KeyValuePair<string, string> rule in rules)
                {
                    builder.Append(rule.Key).Append(" ::= ").Append(rule.Value).AppendLine();
                }
                return builder.ToString().TrimEnd();
            }

            private string BuildSchemaRule(Dictionary<string, object> schema, string preferredName)
            {
                if (TryGetString(schema, "$ref", out string reference))
                {
                    return BuildReference(reference);
                }

                if (schema.TryGetValue("const", out object constant))
                {
                    return AddRule(preferredName, Literal(StructuredOutputJson.Serialize(constant)) + " ws");
                }

                if (TryGetList(schema, "enum", out List<object> enumValues))
                {
                    if (enumValues.Count == 0) throw new FormatException("An enum must contain at least one value.");
                    string expression = string.Join(" | ", enumValues.Select(value => Literal(StructuredOutputJson.Serialize(value)) + " ws"));
                    return AddRule(preferredName, expression);
                }

                if (TryGetList(schema, "oneOf", out List<object> oneOf)) return BuildUnion(oneOf, preferredName);
                if (TryGetList(schema, "anyOf", out List<object> anyOf)) return BuildUnion(anyOf, preferredName);

                if (schema.TryGetValue("type", out object typeValue) && typeValue is List<object> typeList)
                {
                    var alternatives = new List<object>();
                    foreach (object item in typeList)
                    {
                        if (item is not string typeName) throw new FormatException("Schema type arrays may only contain strings.");
                        alternatives.Add(new Dictionary<string, object> { ["type"] = typeName });
                    }
                    return BuildUnion(alternatives, preferredName);
                }

                string type = typeValue as string;
                if (string.IsNullOrEmpty(type))
                {
                    if (schema.ContainsKey("properties") || schema.ContainsKey("required")) type = "object";
                    else if (schema.ContainsKey("items") || schema.ContainsKey("prefixItems")) type = "array";
                }

                return type switch
                {
                    "object" => BuildObject(schema, preferredName),
                    "array" => BuildArray(schema, preferredName),
                    "string" => "json-string",
                    "integer" => "json-integer",
                    "number" => "json-number",
                    "boolean" => "json-boolean",
                    "null" => "json-null",
                    null => "json-value",
                    _ => throw new FormatException($"Unsupported JSON Schema type '{type}'.")
                };
            }

            private string BuildUnion(List<object> schemas, string preferredName)
            {
                if (schemas.Count == 0) throw new FormatException("A schema union must contain at least one entry.");
                var alternatives = new List<string>();
                for (int i = 0; i < schemas.Count; i++)
                {
                    if (schemas[i] is not Dictionary<string, object> child) throw new FormatException("Schema union entries must be objects.");
                    alternatives.Add(BuildSchemaRule(child, preferredName + "-option-" + (i + 1)));
                }
                return AddRule(preferredName, string.Join(" | ", alternatives));
            }

            private string BuildObject(Dictionary<string, object> schema, string preferredName)
            {
                var properties = new Dictionary<string, object>(StringComparer.Ordinal);
                if (schema.TryGetValue("properties", out object rawProperties))
                {
                    if (rawProperties is not Dictionary<string, object> propertyMap) throw new FormatException("'properties' must be an object.");
                    properties = propertyMap;
                }

                var required = new HashSet<string>(StringComparer.Ordinal);
                if (TryGetList(schema, "required", out List<object> requiredValues))
                {
                    foreach (object value in requiredValues)
                    {
                        if (value is not string name) throw new FormatException("'required' entries must be strings.");
                        required.Add(name);
                    }
                }

                foreach (string requiredName in required)
                {
                    if (!properties.ContainsKey(requiredName)) throw new FormatException($"Required property '{requiredName}' is not declared in 'properties'.");
                }

                if (properties.Count == 0)
                {
                    return AddRule(preferredName, Literal("{") + " ws " + Literal("}") + " ws");
                }

                var entries = new List<PropertyEntry>();
                foreach (KeyValuePair<string, object> property in properties)
                {
                    if (property.Value is not Dictionary<string, object> childSchema) throw new FormatException($"Schema for property '{property.Key}' must be an object.");
                    string childRule = BuildSchemaRule(childSchema, preferredName + "-" + property.Key);
                    string expression = Literal(StructuredOutputJson.Quote(property.Key)) + " ws " + Literal(":") + " ws " + childRule;
                    entries.Add(new PropertyEntry(expression, required.Contains(property.Key)));
                }

                int optionalCount = entries.Count(entry => !entry.Required);
                if (optionalCount > MaxOptionalProperties)
                {
                    throw new FormatException($"Objects with more than {MaxOptionalProperties} optional properties are not supported by the GBNF converter.");
                }

                var variants = new List<string>();
                BuildObjectVariants(entries, 0, new List<string>(), variants);
                string body = string.Join(" | ", variants.Select(variant => Literal("{") + " ws" + (variant.Length == 0 ? string.Empty : " " + variant) + " " + Literal("}") + " ws"));
                return AddRule(preferredName, body);
            }

            private static void BuildObjectVariants(List<PropertyEntry> entries, int index, List<string> selected, List<string> variants)
            {
                if (index == entries.Count)
                {
                    variants.Add(string.Join(" " + Literal(",") + " ws ", selected));
                    return;
                }

                PropertyEntry entry = entries[index];
                if (!entry.Required) BuildObjectVariants(entries, index + 1, selected, variants);
                selected.Add(entry.Expression);
                BuildObjectVariants(entries, index + 1, selected, variants);
                selected.RemoveAt(selected.Count - 1);
            }

            private string BuildArray(Dictionary<string, object> schema, string preferredName)
            {
                object itemSchema = null;
                if (schema.TryGetValue("items", out object items)) itemSchema = items;

                if (itemSchema == null)
                {
                    return AddRule(preferredName, Literal("[") + " ws (json-value (" + Literal(",") + " ws json-value)*)? " + Literal("]") + " ws");
                }

                if (itemSchema is not Dictionary<string, object> childSchema) throw new FormatException("'items' must be a schema object.");
                string itemRule = BuildSchemaRule(childSchema, preferredName + "-item");
                return AddRule(preferredName, Literal("[") + " ws (" + itemRule + " (" + Literal(",") + " ws " + itemRule + ")*)? " + Literal("]") + " ws");
            }

            private string BuildReference(string reference)
            {
                if (referenceRules.TryGetValue(reference, out string existing)) return existing;
                const string definitionsPrefix = "#/definitions/";
                const string defsPrefix = "#/$defs/";
                string name;
                string containerName;
                if (reference.StartsWith(definitionsPrefix, StringComparison.Ordinal))
                {
                    name = reference.Substring(definitionsPrefix.Length);
                    containerName = "definitions";
                }
                else if (reference.StartsWith(defsPrefix, StringComparison.Ordinal))
                {
                    name = reference.Substring(defsPrefix.Length);
                    containerName = "$defs";
                }
                else
                {
                    throw new FormatException($"Only local $ref values under #/$defs or #/definitions are supported: '{reference}'.");
                }

                if (!rootSchema.TryGetValue(containerName, out object rawDefinitions) || rawDefinitions is not Dictionary<string, object> definitions ||
                    !definitions.TryGetValue(name, out object rawDefinition) || rawDefinition is not Dictionary<string, object> definition)
                {
                    throw new FormatException($"Referenced schema '{reference}' was not found.");
                }

                string ruleName = UniqueName("ref-" + name);
                referenceRules.Add(reference, ruleName);
                rules.Add(ruleName, "json-value"); // recursion-safe placeholder
                string resolvedRule = BuildSchemaRule(definition, ruleName + "-value");
                rules[ruleName] = resolvedRule;
                return ruleName;
            }

            private void AddCommonRules()
            {
                AddNamedRule("ws", "[ \\t\\n\\r]*");
                AddNamedRule("json-string", Literal("\"") + " ([^\"\\\\] | " + Literal("\\") + " ([\"\\\\/bfnrt] | " + Literal("u") + " [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F]))* " + Literal("\"") + " ws");
                AddNamedRule("json-integer", Literal("-") + "? (" + Literal("0") + " | [1-9] [0-9]*) ws");
                AddNamedRule("json-number", Literal("-") + "? (" + Literal("0") + " | [1-9] [0-9]*) (" + Literal(".") + " [0-9]+)? ([eE] [-+]? [0-9]+)? ws");
                AddNamedRule("json-boolean", "(" + Literal("true") + " | " + Literal("false") + ") ws");
                AddNamedRule("json-null", Literal("null") + " ws");
                AddNamedRule("json-array", Literal("[") + " ws (json-value (" + Literal(",") + " ws json-value)*)? " + Literal("]") + " ws");
                AddNamedRule("json-member", "json-string " + Literal(":") + " ws json-value");
                AddNamedRule("json-object", Literal("{") + " ws (json-member (" + Literal(",") + " ws json-member)*)? " + Literal("}") + " ws");
                AddNamedRule("json-value", "json-object | json-array | json-string | json-number | json-boolean | json-null");
            }

            private string AddRule(string preferredName, string expression)
            {
                string name = UniqueName(preferredName);
                rules.Add(name, expression);
                return name;
            }

            private void AddNamedRule(string name, string expression)
            {
                reservedNames.Add(name);
                rules.Add(name, expression);
            }

            private string UniqueName(string preferredName)
            {
                string name = Regex.Replace(preferredName ?? string.Empty, "[^a-zA-Z0-9-]", "-").Trim('-').ToLowerInvariant();
                if (string.IsNullOrEmpty(name)) name = "rule-" + (++anonymousRuleIndex);
                string candidate = name;
                int suffix = 2;
                while (reservedNames.Contains(candidate) || rules.ContainsKey(candidate)) candidate = name + "-" + suffix++;
                reservedNames.Add(candidate);
                return candidate;
            }

            private static string Literal(string text)
            {
                var builder = new StringBuilder("\"");
                foreach (char character in text)
                {
                    switch (character)
                    {
                        case '\\': builder.Append("\\\\"); break;
                        case '"': builder.Append("\\\""); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        default: builder.Append(character); break;
                    }
                }
                return builder.Append('"').ToString();
            }

            private static bool TryGetString(Dictionary<string, object> source, string key, out string value)
            {
                if (source.TryGetValue(key, out object raw) && raw is string text)
                {
                    value = text;
                    return true;
                }
                value = null;
                return false;
            }

            private static bool TryGetList(Dictionary<string, object> source, string key, out List<object> value)
            {
                if (source.TryGetValue(key, out object raw) && raw is List<object> list)
                {
                    value = list;
                    return true;
                }
                value = null;
                return false;
            }

            private readonly struct PropertyEntry
            {
                internal PropertyEntry(string expression, bool required)
                {
                    Expression = expression;
                    Required = required;
                }
                internal string Expression { get; }
                internal bool Required { get; }
            }
        }
    }
}
