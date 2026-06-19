using Ows.Core.Notarization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<InMemoryReceiptService>();

var app = builder.Build();

app.MapPost("/sessions", (InMemoryReceiptService receiptService) =>
{
    var sessionId = receiptService.StartSession();
    return Results.Ok(new StartSessionResponse { SessionId = sessionId.Value });
});

app.MapPost("/sessions/{id}/checkpoints", (string id, CheckpointRequest request, InMemoryReceiptService receiptService) =>
{
    if (!string.Equals(id, request.SessionId, StringComparison.Ordinal))
    {
        return Results.BadRequest("Route session id does not match payload session id.");
    }

    try
    {
        var receipt = receiptService.SubmitCheckpoint(new Checkpoint
        {
            SessionId = new AssessmentSessionId(request.SessionId),
            SequenceNumber = request.SequenceNumber,
            TimelineHeadHash = request.TimelineHeadHash
        });
        return Results.Ok(receipt);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(exception.Message);
    }
});

app.MapGet("/sessions/{id}/receipts", (string id, InMemoryReceiptService receiptService) =>
{
    try
    {
        return Results.Ok(receiptService.GetReceiptChain(new AssessmentSessionId(id)));
    }
    catch (InvalidOperationException exception)
    {
        return Results.NotFound(exception.Message);
    }
});

app.Run();
