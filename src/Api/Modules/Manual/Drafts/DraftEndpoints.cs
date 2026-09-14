using Api.Infrastructure.Auth;
using Api.Infrastructure.Http;
using Api.Modules.Manual.Drafts.Extraction;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Api.Modules.Manual.Drafts;

public static class DraftEndpoints
{
    public static void MapDraftEndpoints(this RouteGroupBuilder api)
    {
        var manual=api.MapGroup("/manual").RequireAuthorization();
        manual.MapGet("/draft-capabilities", (IOptions<AiDraftOptions> options) =>
        {
            var o=options.Value;
            return TypedResults.Ok(new { available=o.IsAvailable, provider=o.Provider, o.MaxTextCharacters,o.MaxImageBytes,o.MaxImagePixels,o.MaxRows });
        });
        manual.MapGet("/draft-batches", async (HttpContext http,DraftService service) =>
            TypedResults.Ok(await service.List(http.User.GetUserId(),http.RequestAborted)));
        manual.MapGet("/draft-batches/{id:guid}", async (Guid id,HttpContext http,DraftService service) =>
            TypedResults.Ok(await service.Get(http.User.GetUserId(),id,http.RequestAborted)));
        manual.MapGet("/draft-game-search", async (string query,HttpContext http,DraftService service) =>
            TypedResults.Ok(await service.SearchCandidates(http.User.GetUserId(),query,http.RequestAborted)));
        manual.MapPost("/draft-batches", Create);
        manual.MapPatch("/draft-batches/{batch:guid}/rows/{row:guid}", async (Guid batch,Guid row,DraftUpdateRequest request,HttpContext http,DraftService service) =>
            TypedResults.Ok(await service.Update(http.User.GetUserId(),batch,row,request,http.RequestAborted)));
        manual.MapDelete("/draft-batches/{batch:guid}/rows/{row:guid}", async (Guid batch,Guid row,int revision,HttpContext http,DraftService service) =>
        {
            await service.Delete(http.User.GetUserId(),batch,row,revision,http.RequestAborted);
            return TypedResults.NoContent();
        });
        manual.MapPost("/draft-batches/{batch:guid}/accept", async (Guid batch,DraftAcceptRequest request,HttpContext http,DraftService service) =>
            TypedResults.Ok(await service.Accept(http.User.GetUserId(),batch,request,http.RequestAborted)));
    }

    private static async Task<IResult> Create(HttpContext http, DraftExtractionService service, IOptions<AiDraftOptions> options)
    {
        var o=options.Value;
        var maxBody=checked((long)o.MaxImageBytes + (long)o.MaxTextCharacters*4 + 65536);
        var limit=http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is {IsReadOnly:false}) limit.MaxRequestBodySize=maxBody;
        if (http.Request.ContentLength > maxBody) throw new AppProblemException(413,"Input exceeds the upload limit.");
        if (!http.Request.HasFormContentType) throw AppProblemException.Validation("Submit text or a screenshot as multipart form data.");
        IFormCollection form;
        try { form=await http.Request.ReadFormAsync(new FormOptions {MultipartBodyLengthLimit=maxBody,ValueLengthLimit=o.MaxTextCharacters*4+1024},http.RequestAborted); }
        catch(InvalidDataException) { throw AppProblemException.Validation("Invalid or oversized upload."); }
        if (!Guid.TryParse(form["requestId"],out var requestId)) throw AppProblemException.Validation("A request ID is required.");
        if (form.Files.Count>1) throw AppProblemException.Validation("Submit at most one screenshot.");
        var file=form.Files.SingleOrDefault();
        byte[]? image=null;
        if (file is not null)
        {
            if(file.Length>o.MaxImageBytes) throw new AppProblemException(413,"Screenshot exceeds the upload limit.");
            await using var stream=file.OpenReadStream();
            using var bytes=new MemoryStream();
            await stream.CopyToAsync(bytes,http.RequestAborted);
            image=bytes.ToArray();
        }
        return TypedResults.Ok(await service.Create(http.User.GetUserId(),requestId,form["timezone"].ToString(),
            form["text"].ToString(),image,file?.ContentType,http.RequestAborted));
    }
}
