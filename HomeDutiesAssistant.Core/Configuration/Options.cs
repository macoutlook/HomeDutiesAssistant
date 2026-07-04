namespace HomeDutiesAssistant.Configuration;

// Strongly-typed options bound from appsettings.json sections via the
// Options pattern (services.Configure<T>(...)). Injected as IOptions<T>.

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "llama3.1:8b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    // nomic-embed-text was trained with task prefixes; documents and queries
    // must be embedded with these so they share a vector space. For an
    // embedding model that needs no prefixes, set both to "".
    public string EmbedDocumentPrefix { get; set; } = "search_document: ";
    public string EmbedQueryPrefix { get; set; } = "search_query: ";
    public string ChatUri { get; set; } = "/api/chat";
    public string EmbedUri { get; set; } = "/api/embed";
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = "";
}

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    // How many of the most-relevant facts to feed the model per question.
    public int TopK { get; set; } = 4;

    // Vector size produced by the embedding model. nomic-embed-text => 768.
    public int EmbeddingDimensions { get; set; } = 768;

    // Retrieval fuses two signals with Reciprocal Rank Fusion (RRF):
    //   score = LexicalWeight/(RrfK + lexicalRank) + VectorWeight/(RrfK + vectorRank)
    //   - lexical: trigram (pg_trgm) match on category/content — robust for
    //     Polish queries that share words with the facts.
    //   - vector: pgvector cosine distance — semantic recall.
    // nomic-embed-text is weak on Polish, so lexical is weighted higher. Raise
    // VectorWeight (relative to LexicalWeight) when using a strong multilingual
    // embedding model. RrfK dumps the influence of low-ranked results (60 is
    // the standard RRF default).
    public double LexicalWeight { get; set; } = 2.0;
    public double VectorWeight { get; set; } = 1.0;
    public int RrfK { get; set; } = 60;
}