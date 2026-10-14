using Merp.Modules.Platform.Domain.Imports;

namespace Merp.Modules.Platform.UnitTests.Imports;

public sealed class ImportJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidFile_IsReady_AndCommitsOnce()
    {
        var job = New(queued: false);
        job.Status.ShouldBe(ImportStatus.Validating);

        job.Validated([]);
        job.Status.ShouldBe(ImportStatus.Ready);

        job.StartCommit().ShouldBeTrue();
        job.Status.ShouldBe(ImportStatus.Committing);
        job.StartCommit().ShouldBeFalse();

        job.Committed(3, [], Now);
        job.Status.ShouldBe(ImportStatus.Completed);
        job.CommittedRows.ShouldBe(3);
        job.CompletedAt.ShouldBe(Now);
        job.StartCommit().ShouldBeFalse();
    }

    [Fact]
    public void FileWithErrors_IsInvalid_AndCannotBeCommitted()
    {
        var job = New(queued: true);
        job.Status.ShouldBe(ImportStatus.Queued);
        job.StartValidation();

        job.Validated([new(2, "code", "x"), new(2, "name", "y"), new(5, null, "z")]);

        job.Status.ShouldBe(ImportStatus.Invalid);
        job.ErrorRows.ShouldBe(2);
        job.StartCommit().ShouldBeFalse();
    }

    [Fact]
    public void FailedBatch_LeavesTheJobPartiallyCompleted_AndErrorsAreCapped()
    {
        var job = New(queued: false);
        job.Validated([]);
        job.StartCommit();

        job.Committed(100, [.. Enumerable.Range(2, ImportJob.MaxErrors + 10).Select(r => new ImportJobError(r, null, "rolled back"))], Now);

        job.Status.ShouldBe(ImportStatus.PartiallyCompleted);
        job.Errors.Count.ShouldBe(ImportJob.MaxErrors);
        job.ErrorRows.ShouldBe(ImportJob.MaxErrors + 10);
    }

    private static ImportJob New(bool queued) =>
        ImportJob.Create(ImportJobId.From(Guid.CreateVersion7()), "platform.branches", Guid.CreateVersion7(), "tr-TR", Guid.CreateVersion7(), "subeler.xlsx", 3, queued);
}
