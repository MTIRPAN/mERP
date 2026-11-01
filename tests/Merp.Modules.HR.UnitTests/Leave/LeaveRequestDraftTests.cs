using Merp.Modules.HR.Application.Leave;
using Merp.Modules.HR.Domain.Leave;

namespace Merp.Modules.HR.UnitTests.Leave;

/// <summary>What a leave request draft sends and what of the answer reaches the form (PLAN A11, D64).</summary>
public sealed class LeaveRequestDraftTests
{
    private static readonly LeaveTypeOption Annual = new(Guid.CreateVersion7(), "ANNUAL", "Yıllık izin", LeaveCategory.Annual, LeaveUnit.HalfDay, DurationBasis.WorkingDays, null, false, true, false);
    private static readonly LeaveTypeOption Hourly = new(Guid.CreateVersion7(), "EXCUSE_HOURLY", "Saatlik mazeret", LeaveCategory.Excuse, LeaveUnit.Hour, DurationBasis.Hours, null, false, true, false);
    private static readonly LeaveTypeOption Marriage = new(Guid.CreateVersion7(), "MARRIAGE", "Evlilik izni", LeaveCategory.Excuse, LeaveUnit.Day, DurationBasis.CalendarDays, null, true, true, true);
    private static readonly LeaveTypeOption[] Types = [Annual, Hourly, Marriage];

    private static LeaveRequestDraftResult Answer(
        string code = "ANNUAL",
        string start = "2026-11-06",
        string end = "2026-11-09",
        LeaveDayPart startPart = LeaveDayPart.Full,
        LeaveDayPart endPart = LeaveDayPart.Full,
        string startTime = "",
        string endTime = "",
        string eventDate = "",
        string note = "",
        IReadOnlyList<string>? questions = null) =>
        new(code, start, end, startPart, endPart, startTime, endTime, eventDate, note, questions ?? []);

    [Fact]
    public void TheInput_GivesTodayTheCalendarTheUsersTypesAndTheText()
    {
        var input = LeaveRequestDraftPrompt.BuildInput(new DateOnly(2026, 11, 1), Types, "Gelecek cuma ve pazartesi yıllık izin", "tr-TR");

        input.ShouldSatisfyAllConditions(
            i => i.ShouldStartWith("Output language: Turkish"),
            i => i.ShouldContain("Today: 2026-11-01 Sunday"),
            i => i.ShouldContain("<type code=\"ANNUAL\" unit=\"HalfDay\" fixed_length=\"no\">Yıllık izin</type>"),
            i => i.ShouldContain("<type code=\"MARRIAGE\" unit=\"Day\" fixed_length=\"yes\">Evlilik izni</type>"),
            i => i.ShouldContain("2026-11-06 Friday\n"),
            i => i.ShouldContain("2027-01-09 Saturday\n"),
            i => i.ShouldNotContain("2027-01-10"),
            i => i.ShouldEndWith("<employee_text>\nGelecek cuma ve pazartesi yıllık izin\n</employee_text>"));
    }

    [Fact]
    public void ADayRange_FillsTheTypeAndTheDates()
    {
        var draft = LeaveRequestDraftMapper.Map(Answer(code: "annual ", note: " Aile ziyareti ", questions: ["", "Yol izni gerekir mi?"]), Types, "model");

        draft.ShouldSatisfyAllConditions(
            d => d.LeaveTypeId.ShouldBe(Annual.Id),
            d => d.StartDate.ShouldBe(new DateOnly(2026, 11, 6)),
            d => d.EndDate.ShouldBe(new DateOnly(2026, 11, 9)),
            d => d.StartTime.ShouldBeNull(),
            d => d.Note.ShouldBe("Aile ziyareti"),
            d => d.Questions.ShouldBe(["Yol izni gerekir mi?"]),
            d => d.Model.ShouldBe("model"));
    }

    [Fact]
    public void WhatDoesNotFit_StaysEmpty()
    {
        var draft = LeaveRequestDraftMapper.Map(
            Answer(code: "SICK", start: "6 Kasım", end: "2026-11-01", startTime: "09:00", eventDate: "2026-11-01", note: new string('a', 600)),
            Types,
            "model");

        draft.ShouldSatisfyAllConditions(
            d => d.LeaveTypeId.ShouldBeNull(),
            d => d.StartDate.ShouldBeNull(),
            d => d.EndDate.ShouldBeNull(),
            d => d.StartTime.ShouldBeNull(),
            d => d.EventDate.ShouldBeNull(),
            d => d.Note!.Length.ShouldBe(LeaveRequest.NoteMaxLength));
    }

    [Fact]
    public void ASingleDay_HasNoEndDate_AndHalfDaysKeepOnlyTheirOwnSide()
    {
        var draft = LeaveRequestDraftMapper.Map(Answer(end: "2026-11-06", startPart: LeaveDayPart.Morning, endPart: LeaveDayPart.Morning), Types, "model");

        draft.EndDate.ShouldBeNull();
        draft.StartPart.ShouldBe(LeaveDayPart.Full);
        draft.EndPart.ShouldBe(LeaveDayPart.Morning);
    }

    [Fact]
    public void TimesOnlyForAnHourlyType_AndTheEventDayOnlyForAFixedLength_WithoutHalfDays()
    {
        var hourly = LeaveRequestDraftMapper.Map(Answer(code: "EXCUSE_HOURLY", end: "", startTime: "14:00", endTime: "16:30", startPart: LeaveDayPart.Afternoon), Types, "model");
        var marriage = LeaveRequestDraftMapper.Map(Answer(code: "MARRIAGE", end: "", eventDate: "2026-11-14", endPart: LeaveDayPart.Morning), Types, "model");

        hourly.ShouldSatisfyAllConditions(
            d => d.StartTime.ShouldBe(new TimeOnly(14, 0)),
            d => d.EndTime.ShouldBe(new TimeOnly(16, 30)),
            d => d.StartPart.ShouldBe(LeaveDayPart.Full));
        marriage.ShouldSatisfyAllConditions(d => d.EventDate.ShouldBe(new DateOnly(2026, 11, 14)), d => d.EndPart.ShouldBe(LeaveDayPart.Full));
    }
}
