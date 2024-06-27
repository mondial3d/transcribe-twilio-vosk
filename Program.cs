using Microsoft.AspNetCore.HttpOverrides;
using System.Net.WebSockets;
using System.Text.Json;
using Twilio.AspNet.Core;
using Twilio.TwiML;

var builder = WebApplication.CreateBuilder(args);

// Configure services to forward headers when behind a proxy
builder.Services.Configure<ForwardedHeadersOptions>(
    options => options.ForwardedHeaders = ForwardedHeaders.All
);

var app = builder.Build();

// Use middleware for forwarded headers and WebSockets
app.UseForwardedHeaders();
app.UseWebSockets();

// Define a simple GET endpoint
app.MapGet("/", () => "Hello World!");

// Define a POST endpoint for Twilio's Voice Connect that streams to WebSocket
app.MapPost("/voice", (HttpRequest request) =>
{
    var response = new VoiceResponse();
    var connect = new Twilio.TwiML.Voice.Connect();
    connect.Stream(url: $"wss://{request.Host}/stream");
    response.Append(connect);
    return Results.Content(response.ToString(), "application/xml");
});

// WebSocket endpoint that echoes back received messages
app.MapGet("/stream", async (HttpContext context, IHostApplicationLifetime appLifetime) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await Echo(webSocket, appLifetime);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

async Task Echo(
    WebSocket webSocket,
    IHostApplicationLifetime appLifetime
)
{
    var buffer = new byte[1024 * 4];
    var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

    while (!receiveResult.CloseStatus.HasValue &&
           !appLifetime.ApplicationStopping.IsCancellationRequested)
    {
        var jsonDocument = JsonSerializer.Deserialize<JsonDocument>(buffer.AsSpan(0, receiveResult.Count));
        var eventMessage = jsonDocument.RootElement.GetProperty("event").GetString();
        Console.WriteLine($"Event: {eventMessage}");

        // Echo the received message back to the sender
        await webSocket.SendAsync(new ArraySegment<byte>(buffer, 0, receiveResult.Count), receiveResult.MessageType, receiveResult.EndOfMessage, CancellationToken.None);

        receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    }

    if (receiveResult.CloseStatus.HasValue)
    {
        await webSocket.CloseAsync(
            receiveResult.CloseStatus.Value,
            receiveResult.CloseStatusDescription,
            CancellationToken.None);
    }
}

app.Run();
