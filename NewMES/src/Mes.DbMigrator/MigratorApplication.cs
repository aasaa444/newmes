using Mes.Infrastructure.Persistence;
using Mes.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Mes.DbMigrator;

public static class MigratorApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var command = MigratorCommand.Parse(args);
            var connectionString = Environment.GetEnvironmentVariable(
                "ConnectionStrings__MesDatabase");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.Error.WriteLine(
                    "ConnectionStrings__MesDatabase is required; no implicit database is used.");
                return 3;
            }

            var options = new DbContextOptionsBuilder<MesDbContext>()
                .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
                .Options;
            await using var context = new MesDbContext(options);

            if (command.Operation == MigratorOperation.Migrate)
            {
                Console.WriteLine("Applying controlled EF Core migrations...");
                await context.Database.MigrateAsync();
                Console.WriteLine(
                    $"Database is current at {MesMigrationIds.EvolutionBaseline}.");
                return 0;
            }

            var environmentName =
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? "Production";
            DemoSeedPolicy.EnsureAllowed(
                environmentName,
                command.ConfirmNonProduction);
            await new DemoDataInitializer(context).LoadAsync();
            Console.WriteLine("Explicit non-production demo data load completed.");
            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Database operation failed and readiness must remain blocked: {exception.Message}");
            return 4;
        }
    }
}
