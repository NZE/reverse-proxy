// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using System.Security.Cryptography;

namespace Yarp.ReverseProxy.Forwarder;

internal static class ProtocolHelper
{
    internal static readonly Version Http2Version = HttpVersion.Version20;
    internal static readonly Version Http11Version = HttpVersion.Version11;

    internal const string GrpcContentType = "application/grpc";

    public static bool IsHttp2OrGreater(string protocol) => HttpProtocol.IsHttp2(protocol) || HttpProtocol.IsHttp3(protocol);

    public static string GetVersionPolicy(HttpVersionPolicy policy)
    {
        return policy switch
        {
            HttpVersionPolicy.RequestVersionOrLower => "RequestVersionOrLower",
            HttpVersionPolicy.RequestVersionOrHigher => "RequestVersionOrHigher",
            HttpVersionPolicy.RequestVersionExact => "RequestVersionExact",
            _ => throw new NotImplementedException(policy.ToString()),
        };
    }

    /// <summary>
    /// Checks whether the provided content type header value represents a gRPC request.
    /// </summary>
    public static bool IsGrpcContentType(string? contentType) =>
        contentType is not null
        && contentType.StartsWith(GrpcContentType, StringComparison.OrdinalIgnoreCase)
        && MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
        && mediaType.MatchesMediaType(GrpcContentType);

    // GUID appended by the server as part of the security key response.  Defined in the RFC.
    private static readonly byte[] _wsServerGuidBytes = new byte[]
    {
            (byte)'2', (byte)'5', (byte)'8', (byte)'E', (byte)'A', (byte)'F', (byte)'A', (byte)'5', (byte)'-',
            (byte)'E', (byte)'9', (byte)'1', (byte)'4', (byte)'-',
            (byte)'4', (byte)'7', (byte)'D', (byte)'A', (byte)'-',
            (byte)'9', (byte)'5', (byte)'C', (byte)'A', (byte)'-',
            (byte)'C', (byte)'5', (byte)'A', (byte)'B', (byte)'0', (byte)'D', (byte)'C', (byte)'8', (byte)'5', (byte)'B', (byte)'1', (byte)'1'
    };

    /// <summary>
    /// Creates a security key for sending in the Sec-WebSocket-Key header.
    /// </summary>
    /// <returns>The request header security key.</returns>
    internal static string CreateSecWebSocketKey()
    {
        Span<byte> bytes = stackalloc byte[16];

        // Base64-encode a new Guid's bytes to get the security key
        var success = Guid.NewGuid().TryWriteBytes(bytes);
        Debug.Assert(success);
        var secKey = Convert.ToBase64String(bytes);
        return secKey;
    }

    /// <summary>
    /// Creates a pair of a security key for sending in the Sec-WebSocket-Key header and
    /// the associated response we expect to receive as the Sec-WebSocket-Accept header value.
    /// </summary>
    /// <returns>A key-value pair of the request header security key and expected response header value.</returns>
    internal static string CreateSecWebSocketAccept(string key)
    {
        Span<byte> bytes = stackalloc byte[24 /* Base64 guid length */ + _wsServerGuidBytes.Length];

        // Get the corresponding ASCII bytes for seckey+wsServerGuidBytes
        var encodedSecKeyLength = Encoding.ASCII.GetBytes(key, bytes);
        _wsServerGuidBytes.CopyTo(bytes.Slice(encodedSecKeyLength));

        // Hash the seckey+wsServerGuidBytes bytes
        SHA1.TryHashData(bytes, bytes, out var bytesWritten);
        Debug.Assert(bytesWritten == 20 /* SHA1 hash length */);
        var accept = Convert.ToBase64String(bytes[..bytesWritten]);

        // Return the security key + accept value
        return accept;
    }
}
