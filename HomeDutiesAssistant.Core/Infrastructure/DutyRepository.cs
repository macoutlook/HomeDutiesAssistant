using HomeDutiesAssistant.Configuration;
using HomeDutiesAssistant.Models;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;
using Task = System.Threading.Tasks.Task;

namespace HomeDutiesAssistant.Infrastructure;

// The RAG database: a thin wrapper over PostgreSQL + pgvector.
// Stores each fact's text together with its embedding, and answers the
// question "which stored facts are closest in meaning to this query vector?".
public sealed class DutyRepository : IDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly double _lexicalWeight;
    private readonly double _vectorWeight;
    private readonly int _rrfK;

    public DutyRepository(IOptions<DatabaseOptions> databaseOptions, IOptions<RagOptions> ragOptions)
    {
        var builder = new NpgsqlDataSourceBuilder(databaseOptions.Value.ConnectionString);
        builder.UseVector(); // teach Npgsql how to map pgvector's 'vector' type
        _dataSource = builder.Build();
        _lexicalWeight = ragOptions.Value.LexicalWeight;
        _vectorWeight = ragOptions.Value.VectorWeight;
        _rrfK = ragOptions.Value.RrfK;
    }

    // Stores the structured fields, the canonical `content`, and its embedding together.
    // `title` is a unique natural key.
    public async Task SaveAsync(Models.Duty duty, string content, float[] embedding,
        CancellationToken ct = default)
    {
        if (duty.Id <= 0)
        {
            await using var cmd = _dataSource.CreateCommand(DutySql.Insert);
            AddDutyParameters(cmd, duty, content, embedding);
            duty.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        }
        else
        {
            await using var cmd = _dataSource.CreateCommand(DutySql.Update);
            cmd.Parameters.AddWithValue(duty.Id);
            AddDutyParameters(cmd, duty, content, embedding);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    // All of a home's duties with their structured fields, for the management UI.
    public async Task<List<Duty>> ListDutiesAsync(long homeId, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(DutySql.List);
        cmd.Parameters.AddWithValue(homeId);
        var duties = new List<Duty>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            duties.Add(new Duty
            {
                Id = reader.GetInt64(0),
                HomeId = reader.GetInt64(1),
                Category = reader.GetString(2),
                Title = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Provider = reader.IsDBNull(4) ? null : reader.GetString(4),
                Amount = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                Currency = reader.IsDBNull(6) ? null : reader.GetString(6),
                DueDate = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7),
                Frequency = reader.IsDBNull(8) ? null : reader.GetString(8),
                Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
            });
        }
        return duties;
    }

    // Remove a duty by id within its home. Returns true if a row was deleted.
    public async Task<bool> DeleteAsync(long id, long homeId, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(DutySql.Delete);
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(homeId);
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
        long homeId, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(DutySql.Search);
        cmd.Parameters.AddWithValue(new Vector(queryEmbedding)); // $1
        cmd.Parameters.AddWithValue(queryText);                  // $2
        cmd.Parameters.AddWithValue(_lexicalWeight);             // $3
        cmd.Parameters.AddWithValue(_vectorWeight);              // $4
        cmd.Parameters.AddWithValue(_rrfK);                      // $5
        cmd.Parameters.AddWithValue(topK);                       // $6
        cmd.Parameters.AddWithValue(homeId);                     // $7

        var hits = new List<SearchHit>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            hits.Add(new SearchHit(reader.GetString(0), reader.GetDouble(1)));
        return hits;
    }

    public async Task<long> CountAsync(long homeId, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(DutySql.Count);
        cmd.Parameters.AddWithValue(homeId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public void Dispose() => _dataSource.Dispose();
    
    private static void AddDutyParameters(NpgsqlCommand cmd, Models.Duty duty, string content, float[] embedding)
    {
        cmd.Parameters.AddWithValue(duty.HomeId);
        cmd.Parameters.AddWithValue(duty.Category);
        cmd.Parameters.AddWithValue(duty.Title);
        cmd.Parameters.AddWithValue(Nullable(duty.Provider));
        cmd.Parameters.AddWithValue((object?)duty.Amount ?? DBNull.Value);
        cmd.Parameters.AddWithValue(Nullable(duty.Currency));
        cmd.Parameters.AddWithValue((object?)duty.DueDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue(Nullable(duty.Frequency));
        cmd.Parameters.AddWithValue(Nullable(duty.Notes));
        cmd.Parameters.AddWithValue(content);
        cmd.Parameters.AddWithValue(new Vector(embedding));
    }

    private static object Nullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}

public readonly record struct SearchHit(string Content, double Distance);