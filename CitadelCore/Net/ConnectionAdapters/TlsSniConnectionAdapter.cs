﻿/*
* Copyright © 2017 Jesse Nicholson
* This Source Code Form is subject to the terms of the Mozilla Public
* License, v. 2.0. If a copy of the MPL was not distributed with this
* file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

using CitadelCore.Crypto;
using CitadelCore.Extensions;
using CitadelCore.Logging;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Adapter.Internal;
using StreamExtended;
using StreamExtended.Network;
using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CitadelCore.Net.ConnectionAdapters
{
    /// <summary>
    /// The TlsSniConnectionAdapter handles SNI parsing on newly connected clients by faking a peek
    /// read on initial connection. The class then also handles certificate spoofing, and finally
    /// attemtping to complete the TLS handshake with the downstream client.
    /// </summary>
    internal class TlsSniConnectionAdapter : IConnectionAdapter
    {
        public bool IsHttps => true;

        /// <summary>
        /// Holds our certificate store. This is responsible for spoofing, storing and retrieving TLS certificates.
        /// </summary>
        private SpoofedCertStore m_certStore;

        /// <summary>
        /// Returned whenever we're forcing the connection closed, due to error. 
        /// </summary>
        private static ClosedAdaptedConnection s_closedConnection = new ClosedAdaptedConnection();

        /// <summary>
        /// Permitted TLS protocols. 
        /// </summary>
        //private static readonly SslProtocols s_allowedTlsProtocols = SslProtocols.Tls11 | SslProtocols.Tls12;
        private static readonly SslProtocols s_allowedTlsProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;

        public TlsSniConnectionAdapter()
        {
            m_certStore = new SpoofedCertStore();
        }

        public Task<IAdaptedConnection> OnConnectionAsync(ConnectionAdapterContext context)
        {
            return Task.Run(() => InnerOnConnectionAsync(context));
        }

        // TODO: We need to build something special for the SSL.
        private async Task<IAdaptedConnection> InnerOnConnectionAsync(ConnectionAdapterContext context)
        {            
            // We start off by handing the connection stream off to a library that can do a peek read
            // (which is really just doing buffering tricks, not an actual peek read).
            // TODO: Implement CONNECT handling for the SSL filter.
            DefaultBufferPool pool = new DefaultBufferPool();
            var yourClientStream = new CustomBufferedStream(context.ConnectionStream, 4096);
            int firstByte = await yourClientStream.PeekByteAsync(0);

            if ((char)firstByte == 'C')
            {
                // This looks like it might be an HTTPS CONNECT request.
                char[] commandArray = new char[7];

                commandArray[0] = 'C';
                for (int i = 1; i < commandArray.Length; i++)
                {
                    commandArray[i] = (char)await yourClientStream.PeekByteAsync(i);
                }

                string command = new string(commandArray);

                if (command != "CONNECT")
                {
                    return null;
                }

                string httpPart = null;

                StreamReader reader = new StreamReader(context.ConnectionStream);

                string connectLine = await reader.ReadLineAsync();
                string connectHeaders = await reader.ReadToEndAsync();

                string[] lineParts = connectLine.Split(' ');
                httpPart = lineParts[2];

                string responseString = $"{httpPart} 200 OK\r\n\r\n";
                byte[] responseBytes = Encoding.UTF8.GetBytes(responseString);
                context.ConnectionStream.Write(responseBytes, 0, responseBytes.Length);

                reader = null;

                //Thread.Sleep(50); // TODO: Use a smarter method than this to wait for client reply.
            }

            yourClientStream = new CustomBufferedStream(context.ConnectionStream, 4096);

            LoggerProxy.Default.Info("Opened yourClientStream.");

            var clientSslHelloInfo = await SslTools.PeekClientHello(yourClientStream);

            LoggerProxy.Default.Info("Got clientSslHelloInfo.");

            switch(clientSslHelloInfo != null)
            {
                case true:
                {
                        string sniHost = clientSslHelloInfo.Extensions?.FirstOrDefault(x => x.Name == "server_name")?.Data;
              
                    if(string.IsNullOrEmpty(sniHost) || string.IsNullOrWhiteSpace(sniHost))
                    {
                        LoggerProxy.Default.Error("Failed to extract SNI hostname.");
                        return s_closedConnection;
                    }

                    try
                    {
                        var sslStream = new SslStream(yourClientStream, true,
                            (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
                            {
                            // TODO - Handle client certificates. They should be pushed to the
                            // upstream connection eventually.
                            if (certificate != null)
                                {
                                    LoggerProxy.Default.Info("CLIENT CERTIFICATE AVAILABLE!!!!!!!!!!!!!");
                                }

                                return true;
                            });

                        LoggerProxy.Default.Info("Getting spoofed certificate.");
                        // Spoof a cert for the extracted SNI hostname.
                        var spoofedCert = m_certStore.GetSpoofedCertificateForHost(sniHost);

                        LoggerProxy.Default.Info("Got spoofed certificate.");

                        try
                        {
                            // Try to handshake.
                            await sslStream.AuthenticateAsServerAsync(spoofedCert, false, s_allowedTlsProtocols, false);
                        }
                        catch(OperationCanceledException oe)
                        {
                            LoggerProxy.Default.Error("Failed to complete client TLS handshake because the operation was cancelled.");

                            LoggerProxy.Default.Error(oe);

                            sslStream.Dispose();
                            return s_closedConnection;
                        }
                        catch(IOException ex)
                        {
                            LoggerProxy.Default.Error("Failed to complete client TLS handshake because of IO exception.");

                            LoggerProxy.Default.Error(ex);

                            sslStream.Dispose();
                            return s_closedConnection;
                        }

                        // Always set the feature even though the cert might be null
                        context.Features.Set<ITlsConnectionFeature>(new TlsConnectionFeature
                        {
                            ClientCertificate = sslStream.RemoteCertificate != null ? sslStream.RemoteCertificate.ToV2Certificate() : null
                        });

                        return new HttpsAdaptedConnection(sslStream);
                    }
                    catch(Exception err)
                    {
                        LoggerProxy.Default.Error("Failed to complete client TLS handshake because of unknown exception.");

                        LoggerProxy.Default.Error(err);
                    }

                    return s_closedConnection;
                }

                default:
                {
                    LoggerProxy.Default.Info("Returning closed connection.");
                    return s_closedConnection;
                }
            }
        }

        private class HttpsAdaptedConnection : IAdaptedConnection
        {
            private readonly SslStream _sslStream;

            public HttpsAdaptedConnection(SslStream sslStream)
            {
                _sslStream = sslStream;
            }

            public Stream ConnectionStream => _sslStream;

            public void Dispose()
            {
                _sslStream.Dispose();
            }
        }

        private class ClosedAdaptedConnection : IAdaptedConnection
        {
            public Stream ConnectionStream { get; } = new ClosedStream();

            public void Dispose()
            {
            }
        }

        internal class ClosedStream : Stream
        {
            private static readonly Task<int> ZeroResultTask = Task.FromResult(result: 0);

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;

            public override long Length
            {
                get
                {
                    throw new NotSupportedException();
                }
            }

            public override long Position
            {
                get
                {
                    throw new NotSupportedException();
                }
                set
                {
                    throw new NotSupportedException();
                }
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return 0;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return ZeroResultTask;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }
    }
}