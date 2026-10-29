using System.Text.Json;
using Merp.Application.Persistence;
using Merp.Modules.Recruitment.Application.Candidates;
using Merp.Modules.Recruitment.Domain.Candidates;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Merp.Modules.Recruitment.Infrastructure.Candidates;

/// <summary>
/// The pool search (D50): the stored <c>search_vector</c> (Turkish dictionary, GIN index) matched with
/// <c>websearch_to_tsquery</c>, or a part of the name or e-mail while it is being typed.
/// </summary>
internal sealed class CandidateSearch : ICandidateSearch
{
    /// <summary>Shadow property of the generated tsvector column.</summary>
    public const string VectorProperty = "SearchVector";

    private const string Dictionary = "turkish";

    public IQueryable<Candidate> Matching(IQueryable<Candidate> candidates, string text)
    {
        var pattern = TextSearch.ContainsPattern(text);
        return candidates.Where(c =>
            EF.Property<NpgsqlTsVector>(c, VectorProperty).Matches(EF.Functions.WebSearchToTsQuery(Dictionary, text))
            || TextSearch.Matches(c.FirstName + " " + c.LastName, pattern)
            || TextSearch.Matches(c.Email, pattern));
    }

    public IQueryable<Candidate> WithTag(IQueryable<Candidate> candidates, string code)
    {
        // A JsonElement parameter, which the provider sends as jsonb: tags @> '["CODE"]'
        var tag = JsonSerializer.SerializeToElement(new[] { code });
        return candidates.Where(c => EF.Functions.JsonContains(c.Tags, tag));
    }
}
