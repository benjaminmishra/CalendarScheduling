using OneOf.Types;
using OneOf;

namespace DoctorAvailability;

public interface IFindAvailabilityService
{
    Task<OneOf<IDictionary<string, List<Availability>>, NotFound, Error<string>>> FindAvailableSlots(
        int doctorId,
        DateTime startDateTime,
        CancellationToken ct);
}