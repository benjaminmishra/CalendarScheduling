namespace DoctorAvailability;


public enum EventType
{
    Opening,
    Appointment
}

public class Event
{
    public int DoctorId { get; set; }
    
    public EventType Type { get; set; }
    
    public DateTime Start { get; set; }
    
    public DateTime End { get; set; }
}


public class AvailabilitiesResponse
{
    public int DoctorId { get; set; }

    public Dictionary<string, List<Availability>> AvailableSlots { get; set; } = new();
}

public record Availability(DateTime Start, DateTime End);