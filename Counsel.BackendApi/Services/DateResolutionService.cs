using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Counsel.BackendApi.Services
{
    public class DateResolutionService
    {
        private readonly ILogger<DateResolutionService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _braveSearchApiKey;

        public DateResolutionService(ILogger<DateResolutionService> logger, HttpClient httpClient, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _braveSearchApiKey = configuration["BraveSearch:ApiKey"];
        }

        public async Task<DateTime> ResolveRelativeDateAsync(string dateText, DateTime referenceDate)
        {
            // First, try to parse common date references locally
            if (TryResolveCommonDateReference(dateText, referenceDate, out DateTime resolvedDate))
            {
                return resolvedDate;
            }

            // If simple parsing fails and we have a Brave Search API key, try using it
            if (!string.IsNullOrEmpty(_braveSearchApiKey))
            {
                try
                {
                    return await ResolveDateWithBraveSearchAsync(dateText, referenceDate);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve date with Brave Search API. Falling back to local resolution.");
                }
            }

            // Final fallback: Use reference date as base and apply time if provided
            return ApplyTimeIfPresent(referenceDate, dateText);
        }

        private bool TryResolveCommonDateReference(string dateText, DateTime referenceDate, out DateTime resolvedDate)
        {
            resolvedDate = referenceDate;

            dateText = dateText.Trim().ToLowerInvariant();

            // Handle common cases
            if (dateText.Contains("today"))
            {
                resolvedDate = referenceDate.Date;
                return true;
            }

            if (dateText.Contains("tomorrow"))
            {
                resolvedDate = referenceDate.Date.AddDays(1);
                return true;
            }

            if (dateText.Contains("next week"))
            {
                resolvedDate = referenceDate.Date.AddDays(7);
                return true;
            }

            if (dateText.Contains("next month"))
            {
                resolvedDate = referenceDate.Date.AddMonths(1);
                return true;
            }

            // Handle day of week references
            var daysOfWeek = new Dictionary<string, DayOfWeek>
            {
                { "monday", DayOfWeek.Monday },
                { "tuesday", DayOfWeek.Tuesday },
                { "wednesday", DayOfWeek.Wednesday },
                { "thursday", DayOfWeek.Thursday },
                { "friday", DayOfWeek.Friday },
                { "saturday", DayOfWeek.Saturday },
                { "sunday", DayOfWeek.Sunday }
            };

            foreach (var day in daysOfWeek)
            {
                if (dateText.Contains(day.Key))
                {
                    int daysToAdd = ((int)day.Value - (int)referenceDate.DayOfWeek + 7) % 7;
                    if (daysToAdd == 0) // If today is the mentioned day, go to next week
                    {
                        daysToAdd = 7;
                    }
                    resolvedDate = referenceDate.Date.AddDays(daysToAdd);
                    return true;
                }
            }

            // Try parsing as a standard date
            if (DateTime.TryParse(dateText, out DateTime exactDate))
            {
                resolvedDate = exactDate;
                return true;
            }

            return false;
        }

        private async Task<DateTime> ResolveDateWithBraveSearchAsync(string dateText, DateTime referenceDate)
        {
            // Use Brave Search to try to resolve the date
            string query = $"What date is {dateText} relative to {referenceDate.ToString("yyyy-MM-dd")}?";

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri($"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}")
            };

            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Subscription-Token", _braveSearchApiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var searchResults = JsonSerializer.Deserialize<BraveSearchResponse>(content);

            // Extract date from the search results
            // This is a simplified approach, we'd need to parse the search results
            // more intelligently in a real implementation
            if (searchResults != null && searchResults.Results.Count > 0)
            {
                foreach (var result in searchResults.Results)
                {
                    // Look for date patterns in the description
                    if (TryExtractDateFromText(result.Description, out DateTime extractedDate))
                    {
                        return extractedDate;
                    }
                }
            }

            // If we couldn't extract a date, fall back to local resolution
            return ApplyTimeIfPresent(referenceDate, dateText);
        }

        private bool TryExtractDateFromText(string text, out DateTime extractedDate)
        {
            // Simplified date extraction logic
            // In a real implementation, this would be more sophisticated

            extractedDate = DateTime.Now;
            var dateFormats = new[]
            {
                "yyyy-MM-dd",
                "MM/dd/yyyy",
                "MMMM d, yyyy"
            };

            foreach (var format in dateFormats)
            {
                try
                {
                    // Try to find a date in the text
                    for (int i = 0; i < text.Length - 5; i++)
                    {
                        var potentialDate = text.Substring(i, Math.Min(20, text.Length - i));
                        if (DateTime.TryParseExact(potentialDate, format, CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out extractedDate))
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    // Ignore parsing exceptions
                }
            }

            return false;
        }

        private DateTime ApplyTimeIfPresent(DateTime baseDate, string timeText)
        {
            // Try to extract time information from the text
            foreach (var timeFormat in new[] { "h:mm tt", "h tt", "H:mm" })
            {
                try
                {
                    for (int i = 0; i < timeText.Length - 2; i++)
                    {
                        var potentialTime = timeText.Substring(i, Math.Min(10, timeText.Length - i));
                        if (DateTime.TryParseExact(potentialTime, timeFormat, CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out DateTime parsedTime))
                        {
                            return new DateTime(
                                baseDate.Year,
                                baseDate.Month,
                                baseDate.Day,
                                parsedTime.Hour,
                                parsedTime.Minute,
                                0);
                        }
                    }
                }
                catch
                {
                    // Ignore parsing exceptions
                }
            }

            return baseDate;
        }

        // Basic model for Brave Search response
        private class BraveSearchResponse
        {
            public List<SearchResult> Results { get; set; } = new List<SearchResult>();
        }

        private class SearchResult
        {
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }
    }
}