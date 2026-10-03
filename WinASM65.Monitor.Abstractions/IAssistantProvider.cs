using System.Collections.Generic;
using System.Threading;

namespace WinASM65.Monitor.Abstractions
{
    /// <summary>
    /// A model to ask, and a streaming answer back.
    ///
    /// Three things, because that is what the panel needs: a catalogue to pick from,
    /// a budget to assemble context within, and a completion that arrives in pieces.
    ///
    /// Nothing here writes anything. A provider produces a proposed change as text
    /// and the host turns it into a diff against current file content; applying it is
    /// a separate, confirmed act. Generated code is untrusted input, and a provider
    /// with a file-system capability is a provider that can skip the confirmation.
    ///
    /// Local first: the default is a model on the user's own machine, because code
    /// and cartridge contents should not leave it unless that was a deliberate choice.
    /// This contract does not enforce that, and the doc comment is where the reasoning
    /// is kept.
    /// </summary>
    public interface IAssistantProvider
    {
        /// <summary>Stable identifier for the provider, such as <c>ollama</c>.</summary>
        string ProviderId { get; }

        /// <summary>Name for the model picker.</summary>
        string DisplayName { get; }

        /// <summary>The models this provider offers. Empty when it offers none, which is a fact to display rather than hide.</summary>
        IReadOnlyList<AssistantModel> Models { get; }

        /// <summary>
        /// The context ceiling the host must assemble within, in tokens.
        ///
        /// Deliberately not a property of a model alone: what the provider will
        /// actually accept can be lower than what a model advertises, and it is the
        /// smaller number the host has to respect. The panel shows this budget next to
        /// what was sent, because a request that was silently truncated is worse than
        /// one that was refused.
        /// </summary>
        int ContextBudget { get; }

        /// <summary>
        /// Runs a completion and yields it as it arrives, so a long answer can be read
        /// while it is still being written.
        ///
        /// The context is assembled by the host, from the file region, the relevant
        /// instruction documentation, the attached system's capability flags and the
        /// target profile. That assembly is the part that decides whether the answer is
        /// right, and it is host-side on purpose so it stays inspectable.
        /// </summary>
        IAsyncEnumerable<AssistantChunk> CompleteAsync(AssistantRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// One model in a provider's catalogue.
    /// </summary>
    public sealed class AssistantModel
    {
        /// <summary>Creates a catalogue entry.</summary>
        /// <param name="id">Identifier sent back in a request.</param>
        /// <param name="displayName">Name for the picker.</param>
        /// <param name="contextTokens">Context the model advertises, which may exceed the provider's own budget.</param>
        public AssistantModel(string id, string displayName, int contextTokens)
        {
            Id = id;
            DisplayName = displayName;
            ContextTokens = contextTokens;
        }

        /// <summary>Identifier sent back in a request.</summary>
        public string Id { get; private set; }

        /// <summary>Name for the picker.</summary>
        public string DisplayName { get; private set; }

        /// <summary>Context the model advertises, which may exceed the provider's own budget.</summary>
        public int ContextTokens { get; private set; }
    }

    /// <summary>
    /// One request: which model, what to write, and what the host decided to show it.
    /// </summary>
    public sealed class AssistantRequest
    {
        /// <summary>Creates a request.</summary>
        /// <param name="modelId">Model from <see cref="IAssistantProvider.Models"/>.</param>
        /// <param name="prompt">The instruction.</param>
        /// <param name="context">Assembled context, each entry one labelled piece, in the order it should be sent.</param>
        public AssistantRequest(string modelId, string prompt, IReadOnlyList<string> context)
        {
            ModelId = modelId;
            Prompt = prompt;
            Context = context;
        }

        /// <summary>Model from <see cref="IAssistantProvider.Models"/>.</summary>
        public string ModelId { get; private set; }

        /// <summary>The instruction.</summary>
        public string Prompt { get; private set; }

        /// <summary>Assembled context, in the order it should be sent.</summary>
        public IReadOnlyList<string> Context { get; private set; }
    }

    /// <summary>
    /// A piece of a streamed answer, and whether it is the last one.
    ///
    /// <see cref="IsComplete"/> is explicit because the provider knows when it is
    /// done and the host cannot infer it from an empty chunk.
    /// </summary>
    public sealed class AssistantChunk
    {
        /// <summary>Creates a chunk.</summary>
        /// <param name="text">The text of this piece, empty only when the answer is complete with nothing more to say.</param>
        /// <param name="isComplete">True on the final chunk of the answer.</param>
        public AssistantChunk(string text, bool isComplete)
        {
            Text = text;
            IsComplete = isComplete;
        }

        /// <summary>The text of this piece.</summary>
        public string Text { get; private set; }

        /// <summary>True on the final chunk of the answer.</summary>
        public bool IsComplete { get; private set; }
    }
}