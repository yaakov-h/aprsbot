using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AprsBot
{
    static class AprsSettings
    {
        // For the user to fill out (or to be at least made configurable at runtime)
        // Note: This uses the APRS-IS system and is therefore for licenced amateur radio operators only.

        public const string User = "N0CALL";
        public const string Password = "";

        // North America: noam.aprs2.net
        // South America: soam.aprs2.net
        // Europe & Africa: euro.aprs2.net
        // Asia: asia.aprs2.net
        // Oceania: aunz.aprs2.net
        public const string Server = "aunz.aprs2.net";
        public const int Port = 14580;
    }

    static class MqttSettings
    {
        // See docs at http://www.openhardwareconf.org/wiki/Swagbadge2021_MQTT#OLED_messages

        public const string Server = "101.181.46.180";
        public const string Topic = "public/esp32_<your id here>/0/in";
    }

    partial class Program
    {
        static bool cancelled;

        static async Task Main()
        {
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, e) =>
            {
                // Ctrl+C should trigger the CancellationTokenSource so that we can perform a graceful
                // shutdown, but only the first one. A second Ctrl+C should perform a hard shutdown of the app.
                if (!cancelled)
                {
                    cts.Cancel();
                    e.Cancel = true;
                    cancelled = true;
                }
            };

            await using var serviceProvider = new ServiceCollection()
                .AddLogging(lb => lb.AddSimpleConsole())
                .AddScoped<AprsIsTcpClient>()
                .BuildServiceProvider();

            using var scope = serviceProvider.CreateScope();

            // This is purely decorative so that we know Ctrl+C has registered.
            // If the app hangs after this, something has deadlocked in the shutdown handlers.
            cts.Token.Register(() =>
            {
                using var s = serviceProvider.CreateScope();
                var logger = s.ServiceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Shutting down...");
            });

            await using var client = serviceProvider.GetRequiredService<AprsIsTcpClient>();

            try
            {
                cts.Token.ThrowIfCancellationRequested();

                await client.ConnectAsync(AprsSettings.Server, AprsSettings.Port, cts.Token);
                await client.AuthenticateAsync(AprsSettings.User, AprsSettings.Password, cts.Token);

                var tcs = new TaskCompletionSource<object>();
                cts.Token.Register(() => tcs.TrySetCanceled());

                await tcs.Task;
            }
            catch (AuthenticationFailureException)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogError("Invalid username and/or password");
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
            {
            }
        }
    }
}
