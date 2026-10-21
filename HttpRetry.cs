static class HttpRetry
{
    private static readonly TimeSpan[] Delays =
    {
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromSeconds(1.2)
    };

    public static async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        TimeSpan attemptTimeout,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var attemptSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptSource.CancelAfter(attemptTimeout);

            try
            {
                var response = await send(attemptSource.Token);
                if (!IsTransientStatusCode(response.StatusCode) || attempt >= Delays.Length)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (Exception ex) when (IsTransientException(ex) && attempt < Delays.Length)
            {
                // retry below
            }

            await Task.Delay(Delays[attempt], cancellationToken);
        }
    }

    private static bool IsTransientException(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException;

    private static bool IsTransientStatusCode(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;
}
