using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Googols.Application.Ai;
using Microsoft.Extensions.Options;

namespace Googols.Infrastructure.Ai;

public sealed class AzureSearchOptions
{
    public const string Section = "AzureSearch";
    public string? Endpoint { get; set; }
    public string IndexName { get; set; } = "googols-content";
    public string? ApiKey { get; set; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Curriculum-scoped retrieval. The filter always pins status = 'Published' plus board, academic year,
/// grade, subject and language, so a Class 8 Kerala student never receives CBSE or draft material.
/// Index fields: id, title, text, kind, status, boardCode, academicYear, grade, subjectId, chapterId, topicId, language.
/// </summary>
public sealed class AzureSearchRetriever(IOptions<AzureSearchOptions> options) : ICurriculumRetriever
{
    private readonly AzureSearchOptions _o = options.Value;
    private SearchClient? _client;

    public async Task<IReadOnlyList<RetrievedPassage>> SearchAsync(StudentLearningContext c, string query, int top, CancellationToken ct)
    {
        if (!_o.IsConfigured)
        {
            return [];
        }
        _client ??= new SearchClient(new Uri(_o.Endpoint!), _o.IndexName, new AzureKeyCredential(_o.ApiKey!));

        var filter = SearchFilter.Create(
            $"status eq 'Published' and boardCode eq {c.BoardCode} and academicYear eq {c.AcademicYear} and grade eq {c.Grade} and subjectId eq {c.SubjectId.ToString()} and language eq {c.Language}");
        if (c.ChapterId is { } chapterId)
        {
            filter += SearchFilter.Create($" and chapterId eq {chapterId.ToString()}");
        }

        var opts = new SearchOptions { Filter = filter, Size = Math.Clamp(top, 1, 10) };
        Response<SearchResults<SearchDocument>> response = await _client.SearchAsync<SearchDocument>(query, opts, ct);

        var passages = new List<RetrievedPassage>();
        await foreach (var r in response.Value.GetResultsAsync().WithCancellation(ct))
        {
            var d = r.Document;
            var kind = Enum.TryParse<SourceKind>(d.GetString("kind"), out var k) ? k : SourceKind.ApprovedMaterial;
            passages.Add(new RetrievedPassage(
                Guid.TryParse(d.GetString("id"), out var id) ? id : Guid.Empty,
                d.GetString("title") ?? "",
                d.GetString("text") ?? "",
                kind,
                r.Score ?? 0));
        }
        return passages;
    }
}
