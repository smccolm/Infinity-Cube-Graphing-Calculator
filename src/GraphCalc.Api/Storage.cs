using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GraphCalc.Shared;
using Microsoft.Data.Sqlite;

namespace GraphCalc.Api;

public sealed class GraphStorage
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public GraphStorage(string dbPath)
    {
        _dbPath = dbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
    }

    public string DbPath => _dbPath;

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
CREATE TABLE IF NOT EXISTS function_definition (
    id TEXT PRIMARY KEY,
    label TEXT NOT NULL,
    expression TEXT NOT NULL,
    color_hex TEXT NOT NULL,
    is_visible INTEGER NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS result_set (
    id TEXT PRIMARY KEY,
    function_id TEXT NOT NULL,
    label TEXT NOT NULL,
    expression TEXT NOT NULL,
    expression_hash TEXT NOT NULL,
    domain_key TEXT NOT NULL,
    grid_size INTEGER NOT NULL,
    result_json TEXT NOT NULL,
    created_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_result_set_cache
ON result_set(expression_hash, domain_key, grid_size);
""";
        command.ExecuteNonQuery();
    }

    public GraphFunctionDto UpsertFunction(UpsertFunctionRequest request)
    {
        string id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id.Trim();
        var dto = new GraphFunctionDto
        {
            Id = id,
            Label = CleanLabel(request.Label),
            Expression = request.Expression.Trim(),
            ColorHex = CleanColor(request.ColorHex),
            IsVisible = request.IsVisible,
            UpdatedUtc = DateTime.UtcNow.ToString("O")
        };

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO function_definition (id, label, expression, color_hex, is_visible, updated_utc)
VALUES ($id, $label, $expression, $color_hex, $is_visible, $updated_utc)
ON CONFLICT(id) DO UPDATE SET
    label = excluded.label,
    expression = excluded.expression,
    color_hex = excluded.color_hex,
    is_visible = excluded.is_visible,
    updated_utc = excluded.updated_utc;
""";
        command.Parameters.AddWithValue("$id", dto.Id);
        command.Parameters.AddWithValue("$label", dto.Label);
        command.Parameters.AddWithValue("$expression", dto.Expression);
        command.Parameters.AddWithValue("$color_hex", dto.ColorHex);
        command.Parameters.AddWithValue("$is_visible", dto.IsVisible ? 1 : 0);
        command.Parameters.AddWithValue("$updated_utc", dto.UpdatedUtc);
        command.ExecuteNonQuery();
        return dto;
    }

    public List<GraphFunctionDto> GetFunctions()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT id, label, expression, color_hex, is_visible, updated_utc
FROM function_definition
ORDER BY label COLLATE NOCASE;
""";
        using var reader = command.ExecuteReader();
        var list = new List<GraphFunctionDto>();
        while (reader.Read())
        {
            list.Add(new GraphFunctionDto
            {
                Id = reader.GetString(0),
                Label = reader.GetString(1),
                Expression = reader.GetString(2),
                ColorHex = reader.GetString(3),
                IsVisible = reader.GetInt32(4) == 1,
                UpdatedUtc = reader.GetString(5)
            });
        }
        return list;
    }

    public CalculationResultDto? TryGetCachedResult(CalculationRequest request)
    {
        string hash = ExpressionHash(request.Expression);
        string domain = DomainKey(request);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT result_json
FROM result_set
WHERE expression_hash = $hash
  AND domain_key = $domain
  AND grid_size = $grid_size
ORDER BY created_utc DESC
LIMIT 1;
""";
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$domain", domain);
        command.Parameters.AddWithValue("$grid_size", request.AxisSampleCount);
        string? json = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(json)) return null;

        var dto = JsonSerializer.Deserialize<CalculationResultDto>(json, _jsonOptions);
        if (dto != null) dto.FromCache = true;
        return dto;
    }

    public void SaveResult(CalculationResultDto result, CalculationRequest request)
    {
        string json = JsonSerializer.Serialize(result, _jsonOptions);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO result_set (
    id, function_id, label, expression, expression_hash, domain_key, grid_size, result_json, created_utc
)
VALUES (
    $id, $function_id, $label, $expression, $expression_hash, $domain_key, $grid_size, $result_json, $created_utc
);
""";
        command.Parameters.AddWithValue("$id", result.ResultId);
        command.Parameters.AddWithValue("$function_id", result.FunctionId);
        command.Parameters.AddWithValue("$label", result.Label);
        command.Parameters.AddWithValue("$expression", result.Expression);
        command.Parameters.AddWithValue("$expression_hash", result.ExpressionHash);
        command.Parameters.AddWithValue("$domain_key", DomainKey(request));
        command.Parameters.AddWithValue("$grid_size", result.AxisSampleCount);
        command.Parameters.AddWithValue("$result_json", json);
        command.Parameters.AddWithValue("$created_utc", result.CreatedUtc);
        command.ExecuteNonQuery();
    }

    public CalculationResultDto? GetResult(string resultId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT result_json
FROM result_set
WHERE id = $id;
""";
        command.Parameters.AddWithValue("$id", resultId);
        string? json = command.ExecuteScalar() as string;
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<CalculationResultDto>(json, _jsonOptions);
    }

    public static string ExpressionHash(string expression)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(expression.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes);
    }

    private static string DomainKey(CalculationRequest request)
    {
        return string.Join(";",
            $"sampler={request.SamplerName}",
            $"lod={request.LodName}",
            $"axisSamples={request.AxisSampleCount}",
            $"thetaEdgeInset={request.EdgeInset:0.##########}",
            "samplerVersion=R9-boundary-weighted-tan-sampler-atan-cache-render-fix",
            "displayBounds=X,Y,Z[-pi/2,pi/2]",
            "x=tan(thetaX)",
            "y=tan(thetaY)",
            "z=atan(f(x,y))");
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static string CleanLabel(string label)
    {
        string clean = string.IsNullOrWhiteSpace(label) ? "F" : label.Trim();
        return clean.Length <= 24 ? clean : clean[..24];
    }

    private static string CleanColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return "#E91B23";
        string clean = color.Trim();
        if (!clean.StartsWith('#')) clean = "#" + clean;
        return clean.Length == 7 ? clean : "#E91B23";
    }
}
