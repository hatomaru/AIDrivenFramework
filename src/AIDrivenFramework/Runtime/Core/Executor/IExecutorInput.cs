using System;

namespace AIDrivenFW.Core
{
    /// <summary>
    /// Caller-defined validation and serialization. Throw an argument/configuration exception
    /// for invalid data. Both methods run once before generation; retries reuse the snapshot.
    /// Serialization must be synchronous and bounded. No backend schema is imposed by Core.
    /// </summary>
    public interface IExecutorInput
    {
        void Validate();
        string Serialize();
    }

    /// <summary>An already serialized payload, forwarded without parsing or wrapping.</summary>
    public sealed class SerializedExecutorInput : IExecutorInput
    {
        public string Payload { get; }

        public SerializedExecutorInput(string payload)
        {
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public void Validate() { }
        public string Serialize() => Payload;
    }
}
