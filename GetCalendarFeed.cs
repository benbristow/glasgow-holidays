using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using HtmlAgilityPack.CssSelectors.NetCore;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GlasgowHolidays;

public static class GetCalendarFeed
{
    [FunctionName(nameof(GetCalendarFeed))]
    public static async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)]
        HttpRequest req, ILogger log)
    {
        var doc = await GetHtmlDocument("https://www.glasgow.gov.uk/article/3741/Public-General-Holidays");

        var calendar = new Calendar();
        calendar.Events.AddRange(ExtractCalendarEvents(doc));

        return new FileContentResult(
            Encoding.UTF8.GetBytes(new CalendarSerializer().SerializeToString(calendar)),
            "text/calendar")
        {
            FileDownloadName = "glasgowHolidays.ics"
        };
    }

    private static async Task<HtmlDocument> GetHtmlDocument(string url)
    {
        var client = new HttpClient();
        var document = new HtmlDocument();
        var html = await client.GetStringAsync(url);
        document.LoadHtml(html);
        return document;
    }

    private static IEnumerable<CalendarEvent> ExtractCalendarEvents(HtmlDocument doc)
        => doc.QuerySelectorAll("table[summary=holidays] tr")
            .SelectMany(row =>
            {
                var datesColumn = row.QuerySelector("td:nth-child(3)");
                var datesColumnPTags = datesColumn.QuerySelectorAll("p");
                var dateNodes = datesColumnPTags.Any() ? datesColumnPTags : new[] { datesColumn };

                return dateNodes.Select(dateTextNode => new CalendarEvent
                {
                    Summary = HtmlEntity.DeEntitize(row.QuerySelector("td:nth-child(1) td").InnerText),
                    Start = new CalDateTime(DateTime.Parse(
                        HtmlEntity.DeEntitize(dateTextNode.InnerText),
                        System.Globalization.CultureInfo.InvariantCulture)),
                    IsAllDay = true
                });
            });
}