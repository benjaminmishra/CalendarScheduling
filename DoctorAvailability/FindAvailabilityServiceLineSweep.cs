using System.Runtime.InteropServices.JavaScript;
using OneOf.Types;
using OneOf;

namespace DoctorAvailability;

public class FindAvailabilityService : IFindAvailabilityService
{
    private const int NoOfDaysLookAhead = 7;
    
    private readonly IEventsQuery _query;
    
    // Define a type only for internal use
    public record SweepEvent (DateTime Time, int DeltaOpening, int DeltaAppointment);

    public FindAvailabilityService(IEventsQuery query)
    {
        _query = query;
    }

    public async Task<OneOf<IDictionary<string, List<Availability>>, NotFound, Error<string>>> FindAvailableSlots(
        int doctorId, 
        DateTime startDateTime,
        CancellationToken ct)
    {
        if (startDateTime.Date < DateTime.UtcNow.Date)
            return new Error<string>("Start date cannot be in the past");
        
        var endDateTime = startDateTime.AddDays(NoOfDaysLookAhead);

        var events = (await _query.GetEventsByDoctorIdOrderByStartAsync(doctorId, startDateTime, endDateTime, ct)).ToList();
        if (!events.Any())
            return new NotFound();
        
        // group by date

        var eventsByDate = events
            .GroupBy(e => e.Start.Date)
            .ToDictionary(g => g.Key.ToString("yy-MM-dd"), g => g.ToList());

        var result = new Dictionary<string, List<Availability>>();

        for (var i = 0; i < NoOfDaysLookAhead; i++)
        {
            var day = startDateTime.AddDays(i);
            var dayKey = day.Date.ToString("yy-MM-dd");
            result[dayKey] = new();
            
            // get the events for the day, if it does not exist then we skip to the next day and leave this day empty
            if(!eventsByDate.TryGetValue(dayKey,out var eventsForDay))
                continue;
            
            // create sweepe events for that day 
            var sweepEvents = eventsForDay.SelectMany(e => e.Type switch
            {
                EventType.Appointment => new List<SweepEvent>
                    {
                        new SweepEvent(e.Start, 0, 1),
                        new SweepEvent(e.End, 0, -1)
                    },
                EventType.Opening => new List<SweepEvent>
                    {
                        new SweepEvent(e.Start, 1, 0),
                        new SweepEvent(e.End, -1, 0)
                    },
                _ => Enumerable.Empty<SweepEvent>()
            }).ToList();
            
            // order the sweep events, appointment events get more pririty, but closing events get less priority that start events
            sweepEvents.Sort((curr, next) =>
            {
                // compare by time
                var cmp = curr.Time.CompareTo(next.Time);
                if (cmp != 0)
                    return cmp;
                
                

            });
            
            
            foreach (var opening in dayOpenings)
            {
                // start with full open interval and as we go along we divide this up
                var intervals = new List<Interval> { new (opening.Start,opening.End) };
                foreach (var appointment in dayAppointments)
                {
                    // if the appointment is completely outside the opening skip it 
                    if (appointment.Start >= appointment.End || appointment.End <= opening.Start)
                        continue;
                    
                    var newIntervals = new List<Interval>();
                    foreach (var interval in intervals)
                    {
                        // if the appointment doesn't overlap then add the whole interval
                        if (appointment.Start >= interval.End || appointment.End <= interval.Start)
                        {
                            newIntervals.Add(interval);
                        }
                        else
                        {
                            // there is an overlap
                            // if there is time before appointment start then add that
                            if(interval.Start < appointment.Start)
                                newIntervals.Add(interval with { End = appointment.Start });
                            
                            // if there is time after the appointment end then add that
                            if(appointment.End < interval.End)
                                newIntervals.Add(interval with { Start = appointment.End });
                        }
                    }
                    // update the list of intervals with new divided up intervals
                    intervals = newIntervals;
                    // if no intervals then left to process for this appointment
                    if(intervals.Count == 0)
                        break;
                }
                
                // Convert the remaining intervals to slots with formatted times.
                foreach (var interval in intervals)
                {
                    result[dayKey].Add(new Availability(interval.Start, interval.End));
                }
            }
        }
        
        return result;
    }

    private IEnumerable<Event> MergeOverlappingAppointments(List<Event> appointments)
    {
        var merged = new List<Event>();
        
        if (!appointments.Any())
            return Array.Empty<Event>();

        var currentApp = appointments.First();
        
        foreach (var appointment in appointments.OrderBy(a => a.Start).Skip(1))
        {
            if (currentApp.End >= appointment.Start)
            {
                currentApp.End = appointment.End;
            }
            else
            {
                merged.Add(currentApp);
                currentApp = appointment;
            }
        }
        
        merged.Add(currentApp);
        return merged;
    }
}