namespace Counsel.BackendApi.Models
{
    public class CalendarEventResponse
    {
        public string IcsContent { get; set; }
        public string FileName { get; set; }
        public EventDetailsResponse EventDetails { get; set; }
    }

    public class EventDetailsResponse
    {
        public string Title { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public string Description { get; set; }
    }
}