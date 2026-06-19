using Ows.Core.Notarization;

var builder = WebApplication.CreateBuilder(args);
var receiptStorePath = Path.Combine(builder.Environment.ContentRootPath, ".ows-verifier", "receipts.json");
builder.Services.AddSingleton(new JsonFileReceiptService(receiptStorePath));

var app = builder.Build();

app.MapPost("/sessions", (JsonFileReceiptService receiptService) =>
{
    var sessionId = receiptService.StartSession();
    return Results.Ok(new StartSessionResponse { SessionId = sessionId.Value });
});

app.MapPost("/sessions/{id}/checkpoints", (string id, CheckpointRequest request, JsonFileReceiptService receiptService) =>
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

app.MapGet("/sessions/{id}/receipts", (string id, JsonFileReceiptService receiptService) =>
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

app.MapGet("/sessions/{id}/head", (string id, JsonFileReceiptService receiptService) =>
{
    try
    {
        var receiptChain = receiptService.GetReceiptChain(new AssessmentSessionId(id));
        var lastReceipt = receiptChain.Receipts.LastOrDefault();

        return Results.Ok(new SessionHeadResponse
        {
            SessionId = receiptChain.SessionId.Value,
            LastSequenceNumber = lastReceipt?.SequenceNumber ?? 0,
            LastTimelineHeadHash = lastReceipt?.TimelineHeadHash ?? Ows.Core.Events.OwsEventChain.GenesisPreviousEventHash,
            LastReceiptHash = lastReceipt?.ReceiptHash ?? ReceiptChainVerifier.GenesisPreviousReceiptHash
        });
    }
    catch (InvalidOperationException exception)
    {
        return Results.NotFound(exception.Message);
    }
});

app.Run();
