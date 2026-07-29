using Mes.Domain.Identity;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Mes.Infrastructure.Seeding;
using Mes.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Mes.DbMigrator;

public static class MigratorApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var command = MigratorCommand.Parse(args);
            var secretsDirectory = Environment.GetEnvironmentVariable("MES_SECRETS_DIRECTORY")
                ?? "/run/secrets";
            var connectionString = Environment.GetEnvironmentVariable(
                "ConnectionStrings__MesDatabase")
                ?? ExternalSecretFile.ReadOptional(
                    secretsDirectory,
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
                    $"Database is current at {MesMigrationIds.IdempotentProductionOrderIngress}.");
                return 0;
            }

            if (command.Operation == MigratorOperation.BootstrapAdministrator)
            {
                var password = Environment.GetEnvironmentVariable("InitialAdmin__Password")
                    ?? ExternalSecretFile.ReadOptional(
                        secretsDirectory,
                        "InitialAdmin__Password");
                if (string.IsNullOrWhiteSpace(password))
                {
                    throw new ArgumentException(
                        "InitialAdmin__Password must be supplied through an external secret.");
                }

                var bootstrapper = new InitialAdministratorBootstrapper(
                    context,
                    new PasswordHasher<UserAccount>(),
                    TimeProvider.System);
                await bootstrapper.BootstrapAsync(
                    command.Username!,
                    command.DisplayName!,
                    password,
                    $"bootstrap-admin-{Guid.NewGuid():N}");
                Console.WriteLine(
                    $"Initial administrator '{command.Username}' was provisioned.");
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
