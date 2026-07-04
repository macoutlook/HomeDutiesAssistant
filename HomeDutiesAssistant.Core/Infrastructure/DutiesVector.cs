using HomeDutiesAssistant.Configuration;
using HomeDutiesAssistant.Models;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;

namespace HomeDutiesAssistant.Infrastructure;

// The RAG database: a thin wrapper over PostgreSQL + pgvector.
// Stores each fact's text together with its embedding, and answers the
// question "which stored facts are closest in meaning to this query vector?".
public sealed class DutiesVector : IDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly int _dimensions;
    private readonly double _lexicalWeight;
    private readonly double _vectorWeight;
    private readonly int _rrfK;

    public DutiesVector(IOptions<DatabaseOptions> database, IOptions<RagOptions> rag)
    {
        var builder = new NpgsqlDataSourceBuilder(database.Value.ConnectionString);
        builder.UseVector(); // teach Npgsql how to map pgvector's 'vector' type
        _dataSource = builder.Build();
        _dimensions = rag.Value.EmbeddingDimensions;
        _lexicalWeight = rag.Value.LexicalWeight;
        _vectorWeight = rag.Value.VectorWeight;
        _rrfK = rag.Value.RrfK;
    }

    // Ensure the extension and table exist. Safe to call every startup.
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        await using (var ext = new NpgsqlCommand(DutiesSql.CreateVectorExtension, conn))
            await ext.ExecuteNonQueryAsync(ct);

        // pg_trgm powers the lexical half of hybrid retrieval (word_similarity).
        await using (var trgm = new NpgsqlCommand(DutiesSql.CreateTrigramExtension, conn))
            await trgm.ExecuteNonQueryAsync(ct);

        // The 'vector' type only exists after the extension is created, so we
        // reload Npgsql's type cache now that it is present.
        await conn.ReloadTypesAsync(ct);

        var createTable = string.Format(DutiesSql.CreateTable, _dimensions);
        await using (var cmd = new NpgsqlCommand(createTable, conn))
            await cmd.ExecuteNonQueryAsync(ct);
    }

    // Create a duty (Id == 0) or update an existing one (Id > 0). Stores the
    // structured fields, the canonical `content`, and its embedding together.
    // `title` is a unique natural key: creating/seeding a duty whose title
    // already exists upserts that row rather than duplicating it. Editing keys
    // off the surrogate id, so a title can be renamed. The (possibly newly
    // generated) id is written back onto the passed Duty.
    public async Task UpsertAsync(Duty duty, string content, float[] embedding,
        CancellationToken ct = default)
    {
        if (duty.Id <= 0)
        {
            // CREATE/seed — dedupe on the title key; let the identity column
            // assign the id for genuinely new rows and return it.
            await using var cmd = _dataSource.CreateCommand(DutiesSql.Insert);
            AddDutyParameters(cmd, duty, content, embedding);
            duty.Id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        }
        else
        {
            // EDIT — update the known row by id ($1); fields follow ($2..$11).
            // A title rename that collides with another row raises a unique
            // violation, which the caller surfaces to the user.
            await using var cmd = _dataSource.CreateCommand(DutiesSql.Update);
            cmd.Parameters.AddWithValue(duty.Id);
            AddDutyParameters(cmd, duty, content, embedding);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    // Adds the duty's column values (category .. embedding) to the command in a
    // fixed order, so they bind to the same positions in the INSERT/UPDATE SQL.
    private static void AddDutyParameters(NpgsqlCommand cmd, Duty duty, string content, float[] embedding)
    {
        cmd.Parameters.AddWithValue(duty.Category);
        cmd.Parameters.AddWithValue(duty.Title);
        cmd.Parameters.AddWithValue(Nullable(duty.Provider));
        cmd.Parameters.AddWithValue((object?)duty.Amount ?? DBNull.Value);
        cmd.Parameters.AddWithValue(Nullable(duty.Currency));
        cmd.Parameters.AddWithValue(Nullable(duty.DueDate));
        cmd.Parameters.AddWithValue(Nullable(duty.Frequency));
        cmd.Parameters.AddWithValue(Nullable(duty.Notes));
        cmd.Parameters.AddWithValue(content);
        cmd.Parameters.AddWithValue(new Vector(embedding));
    }

    // Map empty/whitespace strings to SQL NULL so blank optional fields don't
    // round-trip as "" (kept for the optional fields; title is required).
    private static object Nullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    // All duties with their structured fields, for the management UI.
    public async Task<List<Duty>> ListDutiesAsync(CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(DutiesSql.List);
        var duties = new List<Duty>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            duties.Add(new Duty
            {
                Id = reader.GetInt64(0),
                Category = reader.GetString(1),
                Title = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Provider = reader.IsDBNull(3) ? null : reader.GetString(3),
                Amount = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                Currency = reader.IsDBNull(5) ? null : reader.GetString(5),
                DueDate = reader.IsDBNull(6) ? null : reader.GetString(6),
                Frequency = reader.IsDBNull(7) ? null : reader.GetString(7),
                Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
            });
        }
        return duties;
    }

    // Remove a duty by id. Returns true if a row was deleted.
    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(DutiesSql.Delete);
        cmd.Parameters.AddWithValue(id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // The retrieval step — hybrid search via Reciprocal Rank Fusion (RRF).
    // Each fact is ranked twice: by trigram text match (lexical) and by pgvector
    // cosine distance (semantic). We fuse the two ranks
    //   score = lexW/(k + lexRank) + vecW/(k + vecRank)
    // and return the topK by score. Ranking on position rather than raw scores
    // lets us combine two signals that live on different scales. The lexical
    // score weights the category 0.6 / content 0.4, since the category word is
    // the strongest topic cue. word_similarity(query, target) tolerates the
    // extra words in a natural question (e.g. "ile płacę za prąd").
    public async Task<List<SearchHit>> SearchAsync(float[] queryEmbedding, string queryText, int topK,
        CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(DutiesSql.Search);
        cmd.Parameters.AddWithValue(new Vector(queryEmbedding)); // $1
        cmd.Parameters.AddWithValue(queryText);                  // $2
        cmd.Parameters.AddWithValue(_lexicalWeight);             // $3
        cmd.Parameters.AddWithValue(_vectorWeight);              // $4
        cmd.Parameters.AddWithValue(_rrfK);                      // $5
        cmd.Parameters.AddWithValue(topK);                       // $6

        var hits = new List<SearchHit>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            hits.Add(new SearchHit(reader.GetString(0), reader.GetDouble(1)));
        return hits;
    }

    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(DutiesSql.Count);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public void Dispose() => _dataSource.Dispose();
}

public readonly record struct SearchHit(string Content, double Distance);