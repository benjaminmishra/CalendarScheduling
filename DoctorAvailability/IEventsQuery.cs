using OneOf.Types;

namespace DoctorAvailability;

public interface IEventsQuery
{
    Task<IEnumerable<Event>> GetEventsByDoctorIdOrderByStartAsync(
        int doctorId, 
        DateTime startDateTime, 
        DateTime endDateTime,
        CancellationToken ct);
}