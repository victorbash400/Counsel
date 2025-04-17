using Counsel.BackendApi.Models;
using Counsel.BackendApi.Services;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Counsel.BackendApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CalendarController : ControllerBase
    {
        private readonly Kernel _kernel;
        private readonly ILogger<CalendarController> _logger;
        private readonly DateResolutionService _dateResolutionService;

        public CalendarController(Kernel kernel, ILogger<CalendarController> logger, DateResolutionService dateResolutionService)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dateResolutionService = dateResolutionService ?? throw new ArgumentNullException(nameof(dateResolutionService));
        }

        [HttpPost("generate")]
        [ProducesResponseType(typeof(CalendarEventResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> Generate([FromBody] CalendarEventRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Query))
            {
                _logger.LogWarning("Invalid calendar event request received.");
                return BadRequest("Query cannot be null or empty.");
            }

            try
            {
                _logger.LogInformation("Processing calendar event query: {Query}", request.Query);

                // Use GPT-4o to parse the natural language query with enhanced prompt
                var prompt = $@"Extract event details from the following query. Return a JSON object with the following fields: 
                - title (string, event name)
                - dateText (string, the raw date/time text from the query like 'tomorrow', 'next Friday', etc.)
                - timeText (string, the raw time text from the query like '10 AM', '2:30 PM', etc.)
                - description (string, optional, event details)
                - durationMinutes (integer, optional, event duration in minutes - default to 60 if not specified)

                For ambiguous queries, extract as much information as possible.
                
                Query: {request.Query}";

                var response = await _kernel.InvokePromptAsync(prompt);
                var parsedEvent = JsonSerializer.Deserialize<ParsedEventDetails>(response.GetValue<string>(), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsedEvent == null || string.IsNullOrEmpty(parsedEvent.Title))
                {
                    _logger.LogWarning("Failed to extract valid event details from query: {Query}", request.Query);
                    return BadRequest("Could not extract valid event details from the query.");
                }

                // Resolve the start date using our DateResolutionService
                var currentDateTime = DateTime.Now;
                string dateTimeText = $"{parsedEvent.DateText} {parsedEvent.TimeText}".Trim();

                var startDateTime = await _dateResolutionService.ResolveRelativeDateAsync(dateTimeText, currentDateTime);

                // Calculate end time based on duration or default to 1 hour
                int durationMinutes = parsedEvent.DurationMinutes > 0 ? parsedEvent.DurationMinutes : 60;
                var endDateTime = startDateTime.AddMinutes(durationMinutes);

                _logger.LogInformation("Resolved date/time: Start={Start}, End={End}",
                    startDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    endDateTime.ToString("yyyy-MM-dd HH:mm:ss"));

                // Create ICS file using iCal.NET
                var calendar = new Ical.Net.Calendar();
                var calendarEvent = new CalendarEvent
                {
                    Summary = parsedEvent.Title,
                    Start = new CalDateTime(startDateTime.ToUniversalTime()),
                    End = new CalDateTime(endDateTime.ToUniversalTime()),
                    Description = parsedEvent.Description ?? string.Empty,
                    Uid = Guid.NewGuid().ToString()
                };
                calendar.Events.Add(calendarEvent);

                // Serialize ICS to string
                var serializer = new Ical.Net.Serialization.CalendarSerializer();
                var icsContent = serializer.SerializeToString(calendar);

                var responseModel = new CalendarEventResponse
                {
                    IcsContent = icsContent,
                    FileName = $"event_{DateTime.UtcNow:yyyyMMddHHmmss}.ics",
                    EventDetails = new EventDetailsResponse
                    {
                        Title = parsedEvent.Title,
                        StartDateTime = startDateTime,
                        EndDateTime = endDateTime,
                        Description = parsedEvent.Description
                    }
                };

                _logger.LogInformation("Calendar event generated successfully for query: {Query}", request.Query);
                return Ok(responseModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating calendar event for query: {Query}", request.Query);
                return StatusCode(500, "An error occurred while generating the calendar event.");
            }
        }

        private class ParsedEventDetails
        {
            public string Title { get; set; } = string.Empty;
            public string DateText { get; set; } = string.Empty;
            public string TimeText { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int DurationMinutes { get; set; } = 60;
        }
    }
}