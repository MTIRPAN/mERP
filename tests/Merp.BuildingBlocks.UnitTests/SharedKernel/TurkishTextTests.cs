using Merp.SharedKernel.Text;

namespace Merp.BuildingBlocks.UnitTests.SharedKernel;

public sealed class TurkishTextTests
{
    [Fact]
    public void Fold_KeepsLength_AndFoldsTurkishLetters()
    {
        const string input = "İIıiŞşĞğÜüÖöÇçÂâÎîÛû";

        var folded = TurkishText.Fold(input);

        folded.ShouldBe("iiiissgguuoocc" + "aaiiuu");
        folded.Length.ShouldBe(input.Length);
    }

    [Theory]
    [InlineData("İSTANBUL", "istanbul")]
    [InlineData("Istanbul", "istanbul")]
    [InlineData("IBM Türkiye", "ibm turkiye")]
    [InlineData("Çağrı Ödeme", "cagri odeme")]
    public void Fold_MatchesRegardlessOfCasingAndCulture(string input, string expected) =>
        TurkishText.Fold(input).ShouldBe(expected);

    [Fact]
    public void FoldTables_HaveTheSameLength() => TurkishText.FoldFrom.Length.ShouldBe(TurkishText.FoldTo.Length);

    [Fact]
    public void SqlFold_TranslatesThenLowers() =>
        TurkishText.SqlFold("title").ShouldBe($"lower(translate(title, '{TurkishText.FoldFrom}', '{TurkishText.FoldTo}'))");
}
