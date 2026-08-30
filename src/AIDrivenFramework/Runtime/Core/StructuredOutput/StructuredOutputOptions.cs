using System;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// JSON Schemaを記述したソースの形式。
    /// </summary>
    public enum SchemaSourceFormat
    {
        Json,
        Yaml
    }

    /// <summary>
    /// AIの出力をJSON Schemaに従わせるための生成オプション。
    /// </summary>
    [Serializable]
    public sealed class StructuredOutputOptions
    {
        /// <summary>JSON SchemaをJSONまたはYAMLで記述した文字列。</summary>
        public string schema;

        /// <summary><see cref="schema"/>の記述形式。</summary>
        public SchemaSourceFormat sourceFormat = SchemaSourceFormat.Json;

        public StructuredOutputOptions(string schema, SchemaSourceFormat sourceFormat = SchemaSourceFormat.Json)
        {
            this.schema = schema;
            this.sourceFormat = sourceFormat;
        }

        public static StructuredOutputOptions FromJson(string jsonSchema)
        {
            return new StructuredOutputOptions(jsonSchema, SchemaSourceFormat.Json);
        }

        public static StructuredOutputOptions FromYaml(string yamlSchema)
        {
            return new StructuredOutputOptions(yamlSchema, SchemaSourceFormat.Yaml);
        }
    }

    /// <summary>
    /// Coreで検証・正規化された、Executor共通の構造化出力定義。
    /// </summary>
    public sealed class StructuredOutputDefinition
    {
        internal StructuredOutputDefinition(string jsonSchema)
        {
            JsonSchema = jsonSchema;
        }

        public string JsonSchema { get; }
    }

    /// <summary>
    /// リクエスト単位の構造化出力に対応するExecutorが実装する追加契約。
    /// </summary>
    /// <remarks>
    /// 既存の独自<see cref="IAIExecutor"/>実装との互換性を維持するため、基本Executor契約とは分離しています。
    /// </remarks>
    public interface IStructuredOutputExecutor
    {
        /// <summary>
        /// 次の生成に使う定義を設定する。通常生成へ戻す場合は<see langword="null"/>が渡される。
        /// </summary>
        /// <returns>設定変更を反映するためにプロセス再起動が必要な場合は<see langword="true"/>。</returns>
        bool ConfigureStructuredOutput(StructuredOutputDefinition definition);
    }
}
