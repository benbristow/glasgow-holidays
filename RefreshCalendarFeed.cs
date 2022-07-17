using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using HtmlAgilityPack;
using HtmlAgilityPack.CssSelectors.NetCore;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.Azure.WebJobs;

namespace GlasgowHolidays;

public static class RefreshCalendarFeed
{
    [FunctionName(nameof(RefreshCalendarFeed))]
    public static async Task RunAsync(
        [TimerTrigger(
            "0 0 0 * * *"
#if DEBUG
            , RunOnStartup=true
#endif
            )] TimerInfo timerInfo)
    {
        var doc = await GetHtmlDocumentAsync("https://www.glasgow.gov.uk/article/3741/Public-General-Holidays");
        var calendar = GetCalendar(doc);
        await UpdateBlobStorageAsync(calendar);
    }

    private static async Task<HtmlDocument> GetHtmlDocumentAsync(string url)
    {
        var client = new HttpClient();
        var document = new HtmlDocument();
        var html = await client.GetStringAsync(url);
        document.LoadHtml(html);
        return document;
    }

    private static Calendar GetCalendar(HtmlDocument doc)
    {
        var calendar = new Calendar();
        calendar.Events.AddRange(doc.QuerySelectorAll("table[summary=holidays] tr")
            .SelectMany(row =>
            {
                var datesColumn = row.QuerySelector("td:nth-child(3)");
                var datesColumnPTags = datesColumn.QuerySelectorAll("p");
                var dateNodes = datesColumnPTags.Any() ? datesColumnPTags : new[] { datesColumn };

                return dateNodes.Select(dateTextNode =>
                {
                    var dateTime = DateTime.Parse(
                        HtmlEntity.DeEntitize(dateTextNode.InnerText),
                        System.Globalization.CultureInfo.InvariantCulture);

                    return new CalendarEvent
                    {
                        Summary = HtmlEntity.DeEntitize(row.QuerySelector("td:nth-child(1) td").InnerText),
                        // Move days of the week that land on a weekday to the next weekday
                        Start = new CalDateTime(dateTime.DayOfWeek switch
                        {
                            DayOfWeek.Saturday => dateTime.AddDays(2),
                            DayOfWeek.Sunday => dateTime.AddDays(3),
                            _ => dateTime
                        }),
                        IsAllDay = true
                    };
                });
            }));

        return calendar;
    }

    private static async Task UpdateBlobStorageAsync(Calendar calendar)
    {
        var blobContainerClient = new BlobContainerClient(
            Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING"),
            Environment.GetEnvironmentVariable("AZURE_STORAGE_BLOB_CONTAINER_NAME"));

        var blobClient = blobContainerClient.GetBlobClient("glasgowHolidays.ics");

        await blobClient.UploadAsync(
            BinaryData.FromString(new CalendarSerializer().SerializeToString(calendar)),
            overwrite: true);
    }
}
