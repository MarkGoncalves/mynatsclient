using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using EnsureThat;
using MyNatsClient.Internals.Commands;
using MyNatsClient.Internals.Extensions;
using MyNatsClient.Ops;

namespace MyNatsClient.Internals
{
    internal class NatsConnectionManager : INatsConnectionManager
    {
        private static readonly ILogger Logger = LoggerManager.Resolve(typeof(NatsConnectionManager));

        public ISocketFactory SocketFactory { private get; set; }

        internal NatsConnectionManager(SocketFactory socketFactory)
        {
            EnsureArg.IsNotNull(socketFactory, nameof(socketFactory));

            SocketFactory = new SocketFactory();
        }

        public Tuple<INatsConnection, IList<IOp>> OpenConnection(ConnectionInfo connectionInfo, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(connectionInfo, nameof(connectionInfo));

            var hosts = new Queue<Host>(connectionInfo.Hosts.GetRandomized());
            while (!cancellationToken.IsCancellationRequested && hosts.Any())
            {
                var host = hosts.Dequeue();

                try
                {
                    return EstablishConnection(host, connectionInfo, cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error while connecting to {host}. Trying with next host (if any).", ex);
                }
            }

            throw NatsException.CouldNotEstablishAnyConnection();
        }

        private Tuple<INatsConnection, IList<IOp>> EstablishConnection(Host host, ConnectionInfo connectionInfo, CancellationToken cancellationToken)
        {
            Socket socket = null;
            BufferedStream writeStream = null;
            BufferedStream readStream = null;

            try
            {
                socket = SocketFactory.Create(connectionInfo.SocketOptions);
                socket.Connect(host, connectionInfo.SocketOptions.ConnectTimeoutMs, cancellationToken);

                readStream = new BufferedStream(socket.CreateReadStream(), socket.ReceiveBufferSize);

                var reader = new NatsOpStreamReader(readStream);
                var readOps = new List<IOp>();
                Func<IOp> readOne = () =>
                {
                    var op = reader.ReadOp().FirstOrDefault();
                    if (op != null)
                        readOps.Add(op);
                    return op;
                };

                var serverInfo = VerifyConnection(host, connectionInfo, socket, readOne);

                writeStream = new BufferedStream(socket.CreateWriteStream(), socket.SendBufferSize);

                return new Tuple<INatsConnection, IList<IOp>>(new NatsConnection(
                    serverInfo,
                    socket,
                    writeStream,
                    readStream,
                    reader,
                    cancellationToken), readOps);
            }
            catch
            {
                Swallow.Everything(
                        () =>
                        {
#if !NETSTANDARD1_6
                            readStream?.Close();
#endif
                            readStream?.Dispose();
                            readStream = null;
                        },
                        () =>
                        {
#if !NETSTANDARD1_6
                            writeStream?.Close();
#endif
                            writeStream?.Dispose();
                            writeStream = null;
                        },
                        () =>
                        {
                            if (socket == null)
                                return;

                            if (socket.Connected)
                                socket.Shutdown(SocketShutdown.Both);
#if !NETSTANDARD1_6
                            socket.Close();
#endif
                            socket.Dispose();
                            socket = null;
                        });

                throw;
            }
        }

        private static NatsServerInfo VerifyConnection(Host host, ConnectionInfo connectionInfo, Socket socket, Func<IOp> readOne)
        {
            if (!socket.Connected)
                throw NatsException.FailedToConnectToHost(host, "No connection could be established.");

            var op = readOne();
            if (op == null)
                throw NatsException.FailedToConnectToHost(host, "Expected to get INFO after establishing connection. Got nothing.");

            var infoOp = op as InfoOp;
            if (infoOp == null)
                throw NatsException.FailedToConnectToHost(host, $"Expected to get INFO after establishing connection. Got {op.GetType().Name}.");

            Logger.Debug($"Got INFO during connect. {infoOp.GetAsString()}");

            var serverInfo = NatsServerInfo.Parse(infoOp.Message);
            var credentials = host.HasNonEmptyCredentials() ? host.Credentials : connectionInfo.Credentials;
            if (serverInfo.AuthRequired && (credentials == null || credentials == Credentials.Empty))
                throw NatsException.MissingCredentials(host);

            socket.Send(ConnectCmd.Generate(connectionInfo.Verbose, credentials));
            socket.Send(PingCmd.Generate());

            op = readOne();
            if (op == null)
                throw NatsException.FailedToConnectToHost(host, "Expected to read something after CONNECT and PING. Got nothing.");

            if (op is ErrOp)
                throw NatsException.FailedToConnectToHost(host, $"Expected to get PONG after sending CONNECT and PING. Got {op.GetAsString()}.");

            if (!socket.Connected)
                throw NatsException.FailedToConnectToHost(host, "No connection could be established.");

            return serverInfo;
        }
    }
}