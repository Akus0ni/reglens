using Npgsql;
using Testcontainers.PostgreSql;

namespace RegLens.Infrastructure.Tests;

/// <summary>One pgvector container for the test run; every test gets its own fresh database.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "pgvector/pgvector:pg17";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();
    private readonly List<NpgsqlDataSource> _dataSources = [];

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async Task<NpgsqlDataSource> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await using (var admin = NpgsqlDataSource.Create(_container.GetConnectionString()))
        {
            await using var create = admin.CreateCommand($"CREATE DATABASE {name}");
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlDataSourceBuilder(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString);
        builder.UseVector();
        var dataSource = builder.Build();
        _dataSources.Add(dataSource);
        return dataSource;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var dataSource in _dataSources)
        {
            await dataSource.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresTests : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
