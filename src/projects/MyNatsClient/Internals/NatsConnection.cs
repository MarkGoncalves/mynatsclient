using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MyNatsClient.Internals.Extensions;

namespace MyNatsClient.Internals
{
    internal class NatsConnection : INatsConnection
    {
        private readonly Func<bool> _socketIsConnected;
        private readonly Func<bool> _canRead;

        private Socket _socket;
        private Stream _readStream;
        private Stream _writeStream;
        private Locker _writeStreamSync;
        private NatsOpStreamReader _reader;
        private NatsStreamWriter _writer;
        private CancellationToken _cancellationToken;
        private bool _isDisposed;

        public INatsServerInfo ServerInfo { get; }
        public bool IsConnected => _socketIsConnected();
        public bool CanRead => _canRead();

        internal NatsConnection(
            NatsServerInfo serverInfo,
            Socket socket,
            BufferedStream writeStream,
            BufferedStream readStream,
            NatsOpStreamReader reader,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(serverInfo, nameof(serverInfo));
            EnsureArg.IsNotNull(socket, nameof(socket));
            EnsureArg.IsNotNull(writeStream, nameof(writeStream));
            EnsureArg.IsNotNull(readStream, nameof(readStream));
            EnsureArg.IsNotNull(reader, nameof(reader));

            if (!socket.Connected)
                throw new ArgumentException("Socket is not connected.", nameof(socket));

            ServerInfo = serverInfo;

            _socket = socket;
            _writeStreamSync = new Locker();
            _writeStream = writeStream;
            _readStream = readStream;
            _reader = reader;
            _cancellationToken = cancellationToken;

            _writer = new NatsStreamWriter(_writeStream, ServerInfo.MaxPayload, _cancellationToken);

            _socketIsConnected = () => _socket != null && _socket.Connected;
            _canRead = () => _socketIsConnected() && _readStream != null && _readStream.CanRead && !_cancellationToken.IsCancellationRequested;
        }

        public void Dispose()
        {
            ThrowIfDisposed();

            Dispose(true);
            GC.SuppressFinalize(this);
            _isDisposed = true;
        }

        private void Dispose(bool disposing)
        {
            if (_isDisposed || !disposing)
                return;

            Try.All(
                () =>
                {
                    _readStream?.Dispose();
                    _readStream = null;
                },
                () =>
                {
                    _writeStream?.Dispose();
                    _writeStream = null;
                },
                () =>
                {
                    _socket?.Shutdown(SocketShutdown.Both);
                    _socket?.Dispose();
                    _socket = null;
                },
                () =>
                {
                    _writeStreamSync?.Dispose();
                    _writeStreamSync = null;
                });

            _reader = null;
            _writer = null;
        }

        public IEnumerable<IOp> ReadOp()
        {
            ThrowIfDisposed();

            ThrowIfNotConnected();

            return _reader.ReadOp();
        }

        public void WithWriteLock(Action<INatsStreamWriter> a)
        {
            ThrowIfDisposed();

            ThrowIfNotConnected();

            using (_writeStreamSync.Lock())
                a(_writer);
        }

        public async Task WithWriteLockAsync(Func<INatsStreamWriter, Task> a)
        {
            ThrowIfDisposed();

            ThrowIfNotConnected();

            using (await _writeStreamSync.LockAsync(_cancellationToken).ForAwait())
                await a(_writer).ForAwait();
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        private void ThrowIfNotConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Can not send. Connection has been disconnected.");
        }
    }
}