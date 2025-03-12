using Moq;
using NUnit.Framework.Legacy;

namespace DoctorAvailability.Tests;

[TestFixture]
public class FindAvailabilityServiceTests
{
    private Mock<IEventsQuery> _queryMock;
    private FindAvailabilityService _service;
    private CancellationToken _ct;

    [SetUp]
    public void Setup()
    {
        _queryMock = new Mock<IEventsQuery>();
        _service = new FindAvailabilityService(_queryMock.Object);
        _ct = CancellationToken.None;
    }

    [Test]
    public async Task FindAvailableSlots_StartDateInPast_ReturnsError()
    {
        // Arrange: set a start date in the past.
        var pastDate = DateTime.UtcNow.AddDays(-1);
        // Act
        var result = await _service.FindAvailableSlots(1, pastDate, _ct);
        // Assert: expect an error with the proper message.
        result.Switch(
            dict => Assert.Fail("Expected error, but got dictionary."),
            notFound => Assert.Fail("Expected error, but got NotFound."),
            error => Assert.That(error.Value, Is.EqualTo("Start date cannot be in the past"))
        );
    }

    [Test]
    public async Task FindAvailableSlots_NoEventsFound_ReturnsNotFound()
    {
        // Arrange: future start date and an empty event list.
        var startDate = DateTime.UtcNow.AddDays(1).Date;
        _queryMock.Setup(q => q.GetEventsByDoctorIdOrderByStartAsync(
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), _ct))
            .ReturnsAsync(new List<Event>());
        // Act
        var result = await _service.FindAvailableSlots(1, startDate, _ct);
        // Assert: expect a NotFound result.
        result.Switch(
            dict => Assert.Fail("Expected NotFound, but got dictionary."),
            notFound => Assert.Pass(),
            error => Assert.Fail("Expected NotFound, but got error.")
        );
    }

    [Test]
    public async Task FindAvailableSlots_AppointmentDoesNotOverlap_ReturnsFullOpening()
    {
        // Arrange:
        // Opening: 9:00-12:00; Appointment that does NOT overlap (after opening).
        var startDate = new DateTime(2025, 03, 12);
        var opening = new Event { Type = EventType.Opening, Start = startDate.AddHours(9), End = startDate.AddHours(12) };
        var appointment = new Event { Type = EventType.Appointment, Start = startDate.AddHours(12).AddMinutes(1), End = startDate.AddHours(13) };

        _queryMock.Setup(q => q.GetEventsByDoctorIdOrderByStartAsync(
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), _ct))
            .ReturnsAsync(new List<Event> { opening, appointment });
        // Act
        var result = await _service.FindAvailableSlots(1, startDate, _ct);
        // Assert:
        result.Switch(
            dict =>
            {
                // Notice: key is computed using startDate.AddDays(7).Date.ToString("yy-MM-dd")
                var key = startDate.Date.ToString("yy-MM-dd");
                Assert.That(dict.ContainsKey(key), Is.True);
                var availabilities = dict[key];
                // Expect full opening available: 9:00-12:00.
                Assert.That(availabilities.Count, Is.EqualTo(1));
                Assert.That(availabilities[0].Start.ToString("H:mm"), Is.EqualTo("9:00"));
                Assert.That(availabilities[0].End.ToString("H:mm"), Is.EqualTo("12:00"));
            },
            notFound => Assert.Fail("Expected dictionary, but got NotFound."),
            error => Assert.Fail("Expected dictionary, but got error.")
        );
    }

    [Test]
    public async Task FindAvailableSlots_AppointmentOverlappingMiddle_SplitsOpening()
    {
        // Arrange:
        // Opening: 9:00-12:00; Appointment: 9:30-10:00.
        var startDate = new DateTime(2025, 03, 12);
        var opening = new Event { Type = EventType.Opening, Start = startDate.AddHours(9), End = startDate.AddHours(12) };
        var appointment = new Event { Type = EventType.Appointment, Start = startDate.AddHours(9).AddMinutes(30), End = startDate.AddHours(10) };

        _queryMock.Setup(q => q.GetEventsByDoctorIdOrderByStartAsync(
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), _ct))
            .ReturnsAsync(new List<Event> { opening, appointment });
        // Act
        var result = await _service.FindAvailableSlots(1, startDate, _ct);
        // Assert:
        result.Switch(
            dict =>
            {
                var key = startDate.Date.ToString("yy-MM-dd");
                Assert.That(dict.ContainsKey(key), Is.True);
                var availabilities = dict[key];
                // Expect two intervals: 9:00-9:30 and 10:00-12:00.
                Assert.That(availabilities.Count, Is.EqualTo(2));
                Assert.That(availabilities[0].Start.ToString("H:mm"), Is.EqualTo("9:00"));
                Assert.That(availabilities[0].End.ToString("H:mm"), Is.EqualTo("9:30"));
                Assert.That(availabilities[1].Start.ToString("H:mm"), Is.EqualTo("10:00"));
                Assert.That(availabilities[1].End.ToString("H:mm"), Is.EqualTo("12:00"));
            },
            notFound => Assert.Fail("Expected dictionary, but got NotFound."),
            error => Assert.Fail("Expected dictionary, but got error.")
        );
    }

    [Test]
    public async Task FindAvailableSlots_AppointmentCoversEntireOpening_ReturnsNoAvailability()
    {
        // Arrange:
        // Opening: 9:00-12:00; Appointment: 9:00-12:00 (covers the entire opening).
        var startDate = new DateTime(2025, 03, 12);
        var opening = new Event { Type = EventType.Opening, Start = startDate.AddHours(9), End = startDate.AddHours(12) };
        var appointment = new Event { Type = EventType.Appointment, Start = startDate.AddHours(9), End = startDate.AddHours(12) };

        _queryMock.Setup(q => q.GetEventsByDoctorIdOrderByStartAsync(
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), _ct))
            .ReturnsAsync(new List<Event> { opening, appointment });
        // Act
        var result = await _service.FindAvailableSlots(1, startDate, _ct);
        // Assert:
        result.Switch(
            dict =>
            {
                var key = startDate.Date.ToString("yy-MM-dd");
                Assert.That(dict.ContainsKey(key));
                var availabilities = dict[key];
                // No available intervals should remain.
                Assert.That(availabilities, Is.Empty);
            },
            notFound => Assert.Fail("Expected dictionary, but got NotFound."),
            error => Assert.Fail("Expected dictionary, but got error.")
        );
    }

    [Test]
    public async Task FindAvailableSlots_MultipleOpeningsAndAppointments_SplitsCorrectly()
    {
        // Arrange:
        // Two openings on the same day:
        // Opening1: 9:00-11:00; Opening2: 13:00-15:00.
        // Appointment1: 9:30-10:00 (splits opening1).
        // Appointment2: 13:30-14:00 (splits opening2).
        var startDate = new DateTime(2025, 03, 12);
        var opening1 = new Event { Type = EventType.Opening, Start = startDate.AddHours(9), End = startDate.AddHours(11) };
        var opening2 = new Event { Type = EventType.Opening, Start = startDate.AddHours(13), End = startDate.AddHours(15) };
        var appointment1 = new Event { Type = EventType.Appointment, Start = startDate.AddHours(9).AddMinutes(30), End = startDate.AddHours(10) };
        var appointment2 = new Event { Type = EventType.Appointment, Start = startDate.AddHours(13).AddMinutes(30), End = startDate.AddHours(14) };

        _queryMock.Setup(q => q.GetEventsByDoctorIdOrderByStartAsync(
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), _ct))
            .ReturnsAsync(new List<Event> { opening1, opening2, appointment1, appointment2 });
        // Act
        var result = await _service.FindAvailableSlots(1, startDate, _ct);
        // Assert:
        result.Switch(
            dict =>
            {
                var key = startDate.Date.ToString("yy-MM-dd");
                ClassicAssert.IsTrue(dict.ContainsKey(key));
                var availabilities = dict[key];
                // For opening1: expect 9:00-9:30 and 10:00-11:00.
                // For opening2: expect 13:00-13:30 and 14:00-15:00.
                Assert.That(availabilities.Count, Is.EqualTo(4));
                Assert.That(availabilities[0].Start.ToString("H:mm"), Is.EqualTo("9:00"));
                Assert.That(availabilities[0].End.ToString("H:mm"), Is.EqualTo("9:30"));
                Assert.That(availabilities[1].Start.ToString("H:mm"), Is.EqualTo("10:00"));
                Assert.That(availabilities[1].End.ToString("H:mm"), Is.EqualTo("11:00"));
                Assert.That(availabilities[2].Start.ToString("H:mm"), Is.EqualTo("13:00"));
                Assert.That(availabilities[2].End.ToString("H:mm"), Is.EqualTo("13:30"));
                Assert.That(availabilities[3].Start.ToString("H:mm"), Is.EqualTo("14:00"));
                Assert.That(availabilities[3].End.ToString("H:mm"), Is.EqualTo("15:00"));
            },
            notFound => Assert.Fail("Expected dictionary, but got NotFound."),
            error => Assert.Fail("Expected dictionary, but got error.")
        );
    }

    [Test]
    public async Task FindAvailableSlots_AppointmentAtBoundary_DoesNotAffectAvailability()
    {
        // Arrange:
        // Opening: 9:00-12:00.
        // Appointments that end exactly at 9:00 and start exactly at 12:00 should not affect availability.
        var startDate = new DateTime(2025, 03, 12);
        var opening = new Event { Type = EventType.Opening, Start = startDate.AddHours(9), End = startDate.AddHours(12) };
        var appointment1 = new Event { Type = EventType.Appointment, Start = startDate.AddHours(8), End = startDate.AddHours(9) };
        var appointment2 = new Event { Type = EventType.Appointment, Start = startDate.AddHours(12), End = startDate.AddHours(13) };

        _queryMock.Setup(q => q.GetEventsByDoctorIdOrderByStartAsync(
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), _ct))
            .ReturnsAsync(new List<Event> { opening, appointment1, appointment2 });
        // Act
        var result = await _service.FindAvailableSlots(1, startDate, _ct);
        // Assert:
        result.Switch(
            dict =>
            {
                var key = startDate.Date.ToString("yy-MM-dd");
                Assert.That(dict.ContainsKey(key), Is.True);
                var availabilities = dict[key];
                // The opening should remain entirely available.
                Assert.That(availabilities.Count, Is.AtLeast(1));
                Assert.That(availabilities[0].Start.ToString("H:mm"), Is.EqualTo("9:00"));
                Assert.That(availabilities[0].End.ToString("H:mm"), Is.EqualTo("12:00"));
            },
            _ => Assert.Fail("Expected dictionary, but got NotFound."),
            _ => Assert.Fail("Expected dictionary, but got error.")
        );
    }

    [Test]
public async Task FindAvailableSlots_MultipleDays_AvailabilityForEachDay()
{
    // Arrange:
    // Create events for multiple days within the 7-day window.
    // Day 1 (startDate): Opening 9:00-12:00 with an appointment from 10:00-10:30.
    // Day 2: Opening 9:00-12:00 with an appointment from 9:30-10:00.
    // Day 3: Opening 9:00-12:00 with an appointment from 11:00-11:30.
    // Days 4-7: No events.
    var startDate = new DateTime(2025, 03, 12);
    var events = new List<Event>
    {
        // Day 1
        new Event { Type = EventType.Opening, Start = startDate.AddHours(9), End = startDate.AddHours(12) },
        new Event { Type = EventType.Appointment, Start = startDate.AddHours(10), End = startDate.AddHours(10).AddMinutes(30) },
        
        // Day 2
        new Event { Type = EventType.Opening, Start = startDate.AddDays(1).AddHours(9), End = startDate.AddDays(1).AddHours(12) },
        new Event { Type = EventType.Appointment, Start = startDate.AddDays(1).AddHours(9).AddMinutes(30), End = startDate.AddDays(1).AddHours(10) },
        
        // Day 3
        new Event { Type = EventType.Opening, Start = startDate.AddDays(2).AddHours(9), End = startDate.AddDays(2).AddHours(12) },
        new Event { Type = EventType.Appointment, Start = startDate.AddDays(2).AddHours(11), End = startDate.AddDays(2).AddHours(11).AddMinutes(30) },
    };

    _queryMock.Setup(q => q.GetEventsByDoctorIdOrderByStartAsync(
        It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), _ct))
        .ReturnsAsync(events);

    // Act
    var result = await _service.FindAvailableSlots(1, startDate, _ct);

    // Assert:
    result.Switch(
        dict =>
        {
            // Expect exactly 7 keys for the 7 days.
            Assert.That(dict.Count, Is.EqualTo(7), "Expected 7 days in the result");

            for (int i = 0; i < 7; i++)
            {
                var day = startDate.AddDays(i);
                var key = day.ToString("yy-MM-dd");
                Assert.That(dict.ContainsKey(key), Is.True, $"Expected key for {key} in dictionary.");
                var availabilities = dict[key];

                if (i == 0)
                {
                    // Day 1: Opening 9:00-12:00 with appointment 10:00-10:30
                    // Expected intervals: 9:00-10:00 and 10:30-12:00.
                    Assert.That(availabilities.Count, Is.EqualTo(2), $"Expected 2 availabilities for day {key}");
                    Assert.That(availabilities[0].Start.ToString("H:mm"), Is.EqualTo("9:00"));
                    Assert.That(availabilities[0].End.ToString("H:mm"), Is.EqualTo("10:00"));
                    Assert.That(availabilities[1].Start.ToString("H:mm"), Is.EqualTo("10:30"));
                    Assert.That(availabilities[1].End.ToString("H:mm"), Is.EqualTo("12:00"));
                }
                else if (i == 1)
                {
                    // Day 2: Opening 9:00-12:00 with appointment 9:30-10:00
                    // Expected intervals: 9:00-9:30 and 10:00-12:00.
                    Assert.That(availabilities.Count, Is.EqualTo(2), $"Expected 2 availabilities for day {key}");
                    Assert.That(availabilities[0].Start.ToString("H:mm"), Is.EqualTo("9:00"));
                    Assert.That(availabilities[0].End.ToString("H:mm"), Is.EqualTo("9:30"));
                    Assert.That(availabilities[1].Start.ToString("H:mm"), Is.EqualTo("10:00"));
                    Assert.That(availabilities[1].End.ToString("H:mm"), Is.EqualTo("12:00"));
                }
                else if (i == 2)
                {
                    // Day 3: Opening 9:00-12:00 with appointment 11:00-11:30
                    // Expected intervals: 9:00-11:00 and 11:30-12:00.
                    Assert.That(availabilities.Count, Is.EqualTo(2), $"Expected 2 availabilities for day {key}");
                    Assert.That(availabilities[0].Start.ToString("H:mm"), Is.EqualTo("9:00"));
                    Assert.That(availabilities[0].End.ToString("H:mm"), Is.EqualTo("11:00"));
                    Assert.That(availabilities[1].Start.ToString("H:mm"), Is.EqualTo("11:30"));
                    Assert.That(availabilities[1].End.ToString("H:mm"), Is.EqualTo("12:00"));
                }
                else
                {
                    // For days 4 through 7, there are no events, so expect an empty availability list.
                    Assert.That(availabilities.Count, Is.EqualTo(0), $"Expected no availabilities for day {key}");
                }
            }
        },
        notFound => Assert.Fail("Expected dictionary, but got NotFound."),
        error => Assert.Fail("Expected dictionary, but got error.")
    );
}
}