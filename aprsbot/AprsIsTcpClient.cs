using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client.Options;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AprsBot
{
    class AprsIsTcpClient : IAsyncDisposable
    {
        public AprsIsTcpClient(ILogger<AprsIsTcpClient> logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            client = new TcpClient();
        }

        readonly ILogger logger;
        readonly TcpClient client;

        TaskCompletionSource<bool> authenticationTaskCompletionSource;
        NetworkStream stream;
        StreamReader reader;
        StreamWriter writer;
        Task loopTask;

        string serverIdentification;
        string user;

        public async ValueTask DisposeAsync() => await DisconnectAsync(waitForTask: true);

        public async ValueTask DisconnectAsync(bool waitForTask)
        {
            await writer.DisposeAsync();
            reader.Dispose();
            stream.Dispose();
            client.Dispose();

            if (waitForTask)
            {
                await loopTask.ConfigureAwait(false);
            }

            authenticationTaskCompletionSource = null;
        }

        public async Task ConnectAsync(string hostname, int port, CancellationToken cancellationToken = default)
        {
            await client.ConnectAsync(hostname, port, cancellationToken);

            cancellationToken.Register(() => client.Close());

            stream = client.GetStream();

            writer = new StreamWriter(stream, Encoding.ASCII)
            {
                AutoFlush = true
            };

            reader = new StreamReader(stream, Encoding.ASCII);

            var identificationLine = await reader.ReadLineAsync();
            Debug.Assert(identificationLine.StartsWith("# "), "first message should be server ident");
            serverIdentification = identificationLine[2..];

            loopTask = Task.Run(async () => await MainLoopAsync(cancellationToken).ConfigureAwait(false), CancellationToken.None);
        }

        public async Task AuthenticateAsync(string user, string password, CancellationToken cancellationToken)
        {
            var newTcs = new TaskCompletionSource<bool>();
            var oldTcs = Interlocked.CompareExchange(ref authenticationTaskCompletionSource, newTcs, null);
            if (oldTcs != null)
            {
                throw new InvalidOperationException("AuthenticateAsync has already been called.");
            }

            cancellationToken.Register(() => newTcs.TrySetCanceled());

            this.user = user;

            await writer.WriteLineAsync($"user {user} pass {password}");
            await newTcs.Task;
        }

        const int ERROR_OPERATION_ABORTED = 995;

        async Task<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await reader.ReadLineAsync();
            }
            catch (IOException ex) when (cancellationToken.IsCancellationRequested && ex.InnerException is SocketException { NativeErrorCode: ERROR_OPERATION_ABORTED })
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw null; // unreachable
            }
            catch (ObjectDisposedException) when (reader.BaseStream is NetworkStream { Socket: { Connected: false } })
            {
                // We can't wait for the main loop task because we are being executed inside the main loop task.
                // If we wait for it then we will deadlock.
                await DisconnectAsync(waitForTask: false);
                return null;
            }
        }

        async Task MainLoopAsync(CancellationToken cancellationToken)
        {
            var authenticated = false;
            string line;

            try
            {
                while ((line = await ReadLineAsync(cancellationToken)) != null)
                {
                    if (!authenticated)
                    {
                        await HandlePreAuthMessageAsync(line, ref authenticated);
                    }
                    else
                    {
                        await HandlePostAuthMessageAsync(line, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        Task HandlePreAuthMessageAsync(string message, ref bool authenticated)
        {
            if (message.StartsWith($"# logresp {user} verified"))
            {
                authenticated = true;
                logger.LogInformation("Authenticated as {user}.", user);
            }
            else if (message.StartsWith($"# logresp {user} unverified"))
            {
                var ex = new AuthenticationFailureException();
                ExceptionDispatchInfo.SetCurrentStackTrace(ex);
                authenticationTaskCompletionSource.TrySetException(ex);
            }
            else
            {
                logger.LogWarning("Unexpected pre-auth message: {message}", message);
            }

            return Task.CompletedTask;
        }

        async Task HandlePostAuthMessageAsync(string message, CancellationToken cancellationToken)
        {
            if (message.StartsWith("# "))
            {
                if (message.IndexOf(serverIdentification) == 2)
                {
                    logger.LogDebug("Got server ping.");
                    return;
                }
                else
                {
                    logger.LogWarning("Unhandled control message: '{message}'", message);
                    return;
                }
            }

            if (!AprsPacket.TryParse(message, out var packet))
            {
                logger.LogWarning("Unhandled packet: '{message}'", message);
                return;
            }

            if (packet.BodyText.Length == 0)
            {
                logger.LogWarning("Empty message body!");
                return;
            }

            var packetType = packet.BodyText[0];

            switch (packetType)
            {
                case ':':
                    await HandleAprsMessageAsync(packet, cancellationToken);
                    break;

                default:
                    logger.LogWarning("Unsupported packet type: '{packetType}'", packetType);
                    break;
            };
        }

        async Task HandleAprsMessageAsync(AprsPacket packet, CancellationToken cancellationToken)
        {
            if (!packet.TryParseAprsMessage(out var message))
            {
                return;
            }


            if (message.ToCall != user)
            {
                logger.LogWarning("Recieved message for another user '{user}'!", message.ToCall);
                return;
            }

            logger.LogInformation("Message from {user}: {text}", packet.Header.FromCall, message.Text);
            try
            {
                var factory = new MqttFactory();
                using var client = factory.CreateMqttClient();
                var result = await client.ConnectAsync(
                    new MqttClientOptionsBuilder()
                    .WithTcpServer(MqttSettings.Server)
                    .Build(),
                cancellationToken);

                await client.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(MqttSettings.Topic)
                    .WithPayload("(oled:clear)")
                    .Build(),
                    cancellationToken);

                await client.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(MqttSettings.Topic)
                    .WithPayload($"(oled:text 0 30 {packet.Header.FromCall}:)")
                    .Build(),
                    cancellationToken);

                await client.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic(MqttSettings.Topic)
                    .WithPayload($"(oled:text 0 20 {message.Text})")
                    .Build(),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception handling incoming APRS message.");
                await RejectMessageAsync(packet, message, cancellationToken);
                return;
            }

            await AcknowledgeMessageAsync(packet, message, cancellationToken);
        }

        // Note for APRS: All callsigns must be padded to 9 digits by adding trailing spaces.

        async Task AcknowledgeMessageAsync(AprsPacket packet, AprsMessage message, CancellationToken cancellationToken)
            => await SendPacketAsync($":{packet.Header.FromCall,-9}:ack{message.Id}", cancellationToken);

        async Task RejectMessageAsync(AprsPacket packet, AprsMessage message, CancellationToken cancellationToken)
            => await SendPacketAsync($":{packet.Header.FromCall,-9}:rej{message.Id}", cancellationToken);

        async Task SendPacketAsync(string bodyText, CancellationToken cancellationToken)
        {
            var rawPacket = $"{user,-9}>APRS:{bodyText}";
            await writer.WriteLineAsync(rawPacket.AsMemory(), cancellationToken);
        }
    }
}
