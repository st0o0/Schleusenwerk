using Microsoft.Data.Sqlite;
using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

public sealed class SqliteConfigurationStore : IConfigurationStore, IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection? _keepAlive;

    public SqliteConfigurationStore(string connectionString)
    {
        _connectionString = connectionString;

        if (connectionString.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase))
        {
            _keepAlive = new SqliteConnection(connectionString);
            _keepAlive.Open();
        }

        InitializeSchema();
    }

    public void Dispose()
    {
        _keepAlive?.Dispose();
    }

    private void InitializeSchema()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS proxy_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                default_request_timeout_ms INTEGER NOT NULL,
                max_connections_per_upstream INTEGER NOT NULL,
                force_https_globally INTEGER NOT NULL,
                snapshot_interval INTEGER NOT NULL,
                stage TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS domain_configs (
                domain_name TEXT PRIMARY KEY,
                is_wildcard INTEGER NOT NULL,
                http_redirect TEXT NOT NULL,
                redirect_url TEXT,
                force_https INTEGER NOT NULL,
                preserve_host_header INTEGER NOT NULL,
                request_timeout_ms INTEGER NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public Task<ProxySettings> GetSettingsAsync(CancellationToken ct = default)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT default_request_timeout_ms, max_connections_per_upstream, force_https_globally, snapshot_interval, stage FROM proxy_settings WHERE id = 1";

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return Task.FromResult(ProxySettings.Default);
        }

        var settings = new ProxySettings
        {
            DefaultRequestTimeout = TimeSpan.FromMilliseconds(reader.GetInt64(0)),
            MaxConnectionsPerUpstream = reader.GetInt32(1),
            ForceHttpsGlobally = reader.GetInt64(2) != 0,
            SnapshotInterval = reader.GetInt32(3),
            Stage = Enum.Parse<AcmeStage>(reader.GetString(4)),
        };

        return Task.FromResult(settings);
    }

    public Task UpdateSettingsAsync(ProxySettings settings, CancellationToken ct = default)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO proxy_settings (id, default_request_timeout_ms, max_connections_per_upstream, force_https_globally, snapshot_interval, stage)
            VALUES (1, @timeout, @maxConn, @forceHttps, @snapInterval, @stage)
            ON CONFLICT(id) DO UPDATE SET
                default_request_timeout_ms = @timeout,
                max_connections_per_upstream = @maxConn,
                force_https_globally = @forceHttps,
                snapshot_interval = @snapInterval,
                stage = @stage
            """;
        cmd.Parameters.AddWithValue("@timeout", (long)settings.DefaultRequestTimeout.TotalMilliseconds);
        cmd.Parameters.AddWithValue("@maxConn", settings.MaxConnectionsPerUpstream);
        cmd.Parameters.AddWithValue("@forceHttps", settings.ForceHttpsGlobally ? 1 : 0);
        cmd.Parameters.AddWithValue("@snapInterval", settings.SnapshotInterval);
        cmd.Parameters.AddWithValue("@stage", settings.Stage.ToString());
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DomainConfig>> GetAllDomainsAsync(CancellationToken ct = default)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT domain_name, is_wildcard, http_redirect, redirect_url, force_https, preserve_host_header, request_timeout_ms FROM domain_configs";

        var domains = new List<DomainConfig>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            domains.Add(ReadDomainConfig(reader));
        }

        return Task.FromResult<IReadOnlyList<DomainConfig>>(domains);
    }

    public Task<DomainConfig?> GetDomainAsync(DomainName name, CancellationToken ct = default)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT domain_name, is_wildcard, http_redirect, redirect_url, force_https, preserve_host_header, request_timeout_ms FROM domain_configs WHERE domain_name = @name";
        cmd.Parameters.AddWithValue("@name", name.Value);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return Task.FromResult<DomainConfig?>(null);
        }

        return Task.FromResult<DomainConfig?>(ReadDomainConfig(reader));
    }

    public Task UpsertDomainAsync(DomainConfig config, CancellationToken ct = default)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO domain_configs (domain_name, is_wildcard, http_redirect, redirect_url, force_https, preserve_host_header, request_timeout_ms)
            VALUES (@name, @wildcard, @redirect, @redirectUrl, @forceHttps, @preserveHost, @timeout)
            ON CONFLICT(domain_name) DO UPDATE SET
                is_wildcard = @wildcard,
                http_redirect = @redirect,
                redirect_url = @redirectUrl,
                force_https = @forceHttps,
                preserve_host_header = @preserveHost,
                request_timeout_ms = @timeout
            """;
        cmd.Parameters.AddWithValue("@name", config.DomainName.Value);
        cmd.Parameters.AddWithValue("@wildcard", config.DomainName.IsWildcard ? 1 : 0);
        cmd.Parameters.AddWithValue("@redirect", config.HttpRedirect.ToString());
        cmd.Parameters.AddWithValue("@redirectUrl", (object?)config.RedirectUrl?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@forceHttps", config.ForceHttps ? 1 : 0);
        cmd.Parameters.AddWithValue("@preserveHost", config.PreserveHostHeader ? 1 : 0);
        cmd.Parameters.AddWithValue("@timeout", (long)config.RequestTimeout.TotalMilliseconds);
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public Task RemoveDomainAsync(DomainName name, CancellationToken ct = default)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM domain_configs WHERE domain_name = @name";
        cmd.Parameters.AddWithValue("@name", name.Value);
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static DomainConfig ReadDomainConfig(SqliteDataReader reader)
    {
        var domainName = DomainName.Parse(reader.GetString(0));
        return new DomainConfig
        {
            DomainName = domainName,
            HttpRedirect = Enum.Parse<RedirectMode>(reader.GetString(2)),
            RedirectUrl = reader.IsDBNull(3) ? null : new Uri(reader.GetString(3)),
            ForceHttps = reader.GetInt64(4) != 0,
            PreserveHostHeader = reader.GetInt64(5) != 0,
            RequestTimeout = TimeSpan.FromMilliseconds(reader.GetInt64(6)),
        };
    }
}
